using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using ContractManager.Services;
using ContractManager.Views;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ContractManager
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;
        private static bool _ownsMutex;
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;
        private ConfigManager? _config;
        private DatabaseService? _db;
        private ReminderService? _reminderService;
        private UpdateService? _updateService;
        private EventWaitHandle? _signalEvent;

        /// <summary>
        /// 暴露 NotifyIcon 供其他组件使用（如提醒气泡通知）。
        /// </summary>
        public NotifyIcon? NotifyIcon => _notifyIcon;

        /// <summary>
        /// 暴露 UpdateService 供 MainViewModel/SettingsDialog 取用
        /// （沿用 NotifyIcon 注入模式）。
        /// </summary>
        public UpdateService? UpdateService => _updateService;

        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += (sender, args) =>
            {
                System.Windows.MessageBox.Show($"程序出现未处理异常:\n{args.Exception.Message}\n\n详细信息:\n{args.Exception}",
                                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);

            bool isCheckMode = e.Args.Length > 0 && e.Args[0] == "--check";

            // === --check 模式：初始化服务 → 弹 Toast → 检测主实例 ===
            if (isCheckMode)
            {
                try
                {
                    _config = new ConfigManager();
                    var appDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "ContractManager"
                    );
                    Directory.CreateDirectory(appDir);
                    var dbPath = Path.Combine(appDir, "contracts.db");

                    _db = new DatabaseService(dbPath);
                    _reminderService = new ReminderService();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"初始化失败: {ex.Message}");
                    Shutdown(1);
                    return;
                }

                // 执行合同检查、弹 Toast、发 Webhook
                RunCheckAndNotify();

                // 主实例已在运行 → 当前进程直接退出（始终只有 1 个进程）
                if (IsMainInstanceRunning())
                {
                    _db?.Dispose();
                    _db = null;
                    _reminderService?.Dispose();
                    _reminderService = null;
                    Shutdown();
                    return;
                }

                // 主实例没运行 → 当前进程直接接管成为主实例，继续往下走
                // _config / _db / _reminderService 已初始化，无需重新创建
            }

            // === 单实例互斥锁 ===
            const string mutexName = "ContractManager_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                // 通知已有实例显示窗口
                try
                {
                    using var signal = EventWaitHandle.OpenExisting("ContractManager_ShowWindow_Signal");
                    signal.Set();
                }
                catch { }
                Shutdown();
                return;
            }

            // === 启动 GUI ===
            try
            {
                // 非 --check 模式需要初始化服务（--check 已在上面初始化过了）
                if (!isCheckMode)
                {
                    _config = new ConfigManager();
                    var appDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "ContractManager"
                    );
                    Directory.CreateDirectory(appDir);
                    var dbPath = Path.Combine(appDir, "contracts.db");

                    _db = new DatabaseService(dbPath);
                    _reminderService = new ReminderService();
                }

                // Setup system tray
                InitializeNotifyIcon();

                // 监听来自其他实例的激活信号
                StartWindowSignalListener();

                // Show main window
                _mainWindow = new MainWindow(_db!, _config!);
                _mainWindow.Closing += (s, args) =>
                {
                    // Minimize to tray instead of closing
                    args.Cancel = true;
                    ((Window)s!).Hide();
                };
                _mainWindow.Show();

                // 装配 UpdateService（无论是否 --check 接管路径，都确保主实例持有）
                _updateService ??= new UpdateService(_config!);

                // Auto-start check
                CheckAutoStart();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "合同管理系统"
            };

            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ico", "favicon.ico");
            if (System.IO.File.Exists(iconPath))
                _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            else
                _notifyIcon.Icon = SystemIcons.Application;

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("显示", null, (_, _) => ShowMainWindow());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("关于", null, (_, _) => ShowAbout());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("退出", null, (_, _) => ExitApplication());

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
            _notifyIcon.BalloonTipClicked += (_, _) => ShowMainWindow();
        }

        /// <summary>
        /// --check 模式：检查合同到期 → Toast 通知 → Webhook。
        /// 不创建 NotifyIcon，不启动新进程，不调用 Shutdown。
        /// 调用方根据 IsMainInstanceRunning() 决定是否继续成为主实例。
        /// </summary>
        private void RunCheckAndNotify()
        {
            if (_reminderService == null || _db == null || _config == null)
                return;

            try
            {
                var messages = _reminderService.CheckExpiring(_db, _config);

                // === Windows Toast notification (native bubble, no tray icon) ===
                ShowToastNotification(messages);

                // === Send webhook if enabled and contracts are expiring ===
                if (messages.Count > 0 && _config.WebhookEnabled && !string.IsNullOrEmpty(_config.WebhookUrl))
                {
                    var fullText = string.Join("\n\n", messages);
                    var webhookContent = $"【合同到期提醒】\n\n发现 {messages.Count} 个需要预警的合同：\n\n{fullText}";
                    _reminderService.SendWeChatWebhook(_config.WebhookUrl, webhookContent).Wait();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 使用 Windows Toast 通知显示合同到期提醒（无托盘图标）。
        /// </summary>
        private static void ShowToastNotification(List<string> messages)
        {
            try
            {
                if (messages.Count == 0)
                {
                    new ToastContentBuilder()
                        .AddText("合同到期提醒")
                        .AddText("当前没有需要预警的合同，一切正常！")
                        .Show();
                }
                else
                {
                    var summary = string.Join("\n", messages.Select(m => m.Split('\n')[0]));
                    new ToastContentBuilder()
                        .AddText($"合同到期提醒（{messages.Count} 个合同）")
                        .AddText(summary)
                        .Show();
                }
            }
            catch { /* Toast failure is non-critical */ }
        }

        /// <summary>
        /// 通过命名事件检测主实例是否已在运行。
        /// </summary>
        private static bool IsMainInstanceRunning()
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting("ContractManager_ShowWindow_Signal");
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 安全清理 NotifyIcon：先隐藏、刷新消息队列、再释放资源。
        /// 解决 Windows 任务计划拉起时通知中心清除后托盘图标残留的问题。
        /// </summary>
        private static void CleanupNotifyIcon(ref NotifyIcon? icon)
        {
            if (icon == null) return;
            try
            {
                icon.Visible = false;
                // 让 Windows Shell 消息队列有机会处理 NIM_DELETE 通知
                System.Windows.Forms.Application.DoEvents();
                icon.Dispose();
            }
            catch { }
            icon = null;
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
                return;

            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        /// <summary>
        /// 启动后台线程监听来自其他实例的激活窗口信号。
        /// </summary>
        private void StartWindowSignalListener()
        {
            _signalEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "ContractManager_ShowWindow_Signal");
            Task.Run(() =>
            {
                while (_signalEvent.WaitOne())
                {
                    Dispatcher.Invoke(() => ShowMainWindow());
                }
            });
        }

        private void ShowAbout()
        {
            var version = GetAssemblyVersion();
            System.Windows.MessageBox.Show(
                $"合同管理系统 v{version}\n\n" +
                "用于管理合同信息和到期提醒。\n\n" +
                "华雄赞助\n\n" +
                "Copyright © 2026 王哥出品",
                "关于",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// 读取 AssemblyInformationalVersion（来自 csproj &lt;Version&gt;），
        /// 去掉可能的 source revision metadata（如 "2.3.0+abc"）。
        /// </summary>
        private static string GetAssemblyVersion()
        {
            var attr = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var v = attr?.InformationalVersion ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }

        private void ExitApplication()
        {
            var icon = _notifyIcon;
            _notifyIcon = null;
            CleanupNotifyIcon(ref icon);
            _db?.Dispose();
            _reminderService?.Dispose();
            Shutdown();
        }

        private void CheckAutoStart()
        {
            try
            {
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                var exePath = Assembly.GetExecutingAssembly().Location;
                if (exePath.EndsWith(".dll"))
                    exePath = exePath.Replace(".dll", ".exe");

                if (_config?.AutoStart == true)
                {
                    if (key.GetValue("ContractManager") == null)
                        key.SetValue("ContractManager", $"\"{exePath}\"");
                }
                else
                {
                    // 配置关闭时删除注册表项
                    if (key.GetValue("ContractManager") != null)
                        key.DeleteValue("ContractManager", false);
                }
                key.Close();
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var icon = _notifyIcon;
            _notifyIcon = null;
            CleanupNotifyIcon(ref icon);
            _db?.Dispose();
            _reminderService?.Dispose();
            _updateService?.Dispose();
            _signalEvent?.Dispose();
            if (_ownsMutex)
            {
                try { _mutex?.ReleaseMutex(); } catch { }
            }
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
