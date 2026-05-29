using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ContractManager.Models;
using ContractManager.Services;

namespace ContractManager.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action<object?> execute, Func<bool> canExecute)
            : this(execute, canExecute != null ? (Func<object?, bool>)(_ => canExecute()) : null)
        {
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }

    public class ContractViewModel : BaseViewModel
    {
        private readonly ContractStatusInfo _contract;

        public ContractViewModel(ContractStatusInfo contract)
        {
            _contract = contract;
        }

        public long Id => _contract.Id;
        public long? GroupId => _contract.GroupId;
        public string Name => _contract.Name;
        public string StartDate => _contract.StartDate;
        public string EndDate => _contract.EndDate;
        public string ReminderDate => _contract.ReminderDate ?? "";
        public int ReminderDays => _contract.ReminderDays;
        public string? Notes => _contract.Notes;
        public string? StoragePath => _contract.StoragePath;
        public int Remaining => _contract.Remaining;
        public string Status => _contract.Status;
        public string StatusText => _contract.StatusText;

        public string DateRange => $"{StartDate} - {EndDate}";

        public string DisplayRemaining => Remaining switch
        {
            < 0 => $"已过期 {-Remaining} 天",
            0 => "今天到期",
            1 => "明天到期",
            _ => $"{Remaining} 天"
        };

        public string DisplayStatusText => Status switch
        {
            "expired" => "已过期",
            "expiring" => "即将到期",
            "warning" => "需关注",
            "normal" => "正常",
            _ => StatusText
        };

        public SolidColorBrush RowBackground => Status switch
        {
            "expired" => new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xE0)),
            "expiring" => new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0)),
            "warning" => new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xE7)),
            "normal" => new SolidColorBrush(Colors.White),
            _ => new SolidColorBrush(Colors.White)
        };

        public SolidColorBrush RowForeground => Status switch
        {
            "expired" => new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)),
            "expiring" => new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)),
            "warning" => new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B)),
            "normal" => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            _ => new SolidColorBrush(Colors.Black)
        };

        public ContractStatusInfo ToContractStatusInfo() => _contract;
    }

    public class MainViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly ConfigManager _config;
        private readonly DispatcherTimer _refreshTimer;

        public MainViewModel(DatabaseService db, ConfigManager config)
        {
            _db = db;
            _config = config;
            Contracts = new ObservableCollection<ContractViewModel>();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _refreshTimer.Tick += (_, _) => LoadContracts();

            InitializeCommands();
        }

        public ObservableCollection<ContractViewModel> Contracts { get; }

        private ContractViewModel? _selectedContract;
        public ContractViewModel? SelectedContract
        {
            get => _selectedContract;
            set => SetProperty(ref _selectedContract, value);
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        private int _expiringCount;
        public int ExpiringCount
        {
            get => _expiringCount;
            set => SetProperty(ref _expiringCount, value);
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private DateTime _lastRefresh = DateTime.Now;
        public DateTime LastRefresh
        {
            get => _lastRefresh;
            set => SetProperty(ref _lastRefresh, value);
        }

        public string LastRefreshText => $"刷新时间: {LastRefresh:HH:mm:ss}";

        public Window? OwnerWindow { get; set; }

        /// <summary>
        /// 系统托盘图标，用于显示气泡通知。
        /// </summary>
        public System.Windows.Forms.NotifyIcon? NotifyIcon { get; set; }

        // ---- Commands ----
        public ICommand AddContractCommand { get; private set; } = null!;
        public ICommand RenewContractCommand { get; private set; } = null!;
        public ICommand EditContractCommand { get; private set; } = null!;
        public ICommand DeleteContractCommand { get; private set; } = null!;
        public ICommand OpenFolderCommand { get; private set; } = null!;
        public ICommand OpenSettingsCommand { get; private set; } = null!;
        public ICommand RefreshCommand { get; private set; } = null!;
        public ICommand ViewDetailCommand { get; private set; } = null!;
        public ICommand CheckRemindersCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            AddContractCommand = new RelayCommand(ExecuteAddContract);
            RenewContractCommand = new RelayCommand(ExecuteRenewContract, _ => SelectedContract != null);
            EditContractCommand = new RelayCommand(ExecuteEditContract, _ => SelectedContract != null);
            DeleteContractCommand = new RelayCommand(ExecuteDeleteContract, _ => SelectedContract != null);
            OpenFolderCommand = new RelayCommand(ExecuteOpenFolder, _ => SelectedContract != null);
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);
            RefreshCommand = new RelayCommand(ExecuteRefresh);
            ViewDetailCommand = new RelayCommand(ExecuteViewDetail, _ => SelectedContract != null);
            CheckRemindersCommand = new RelayCommand(ExecuteCheckReminders);
        }

        public void LoadContracts()
        {
            try
            {
                var contracts = _db.GetAllCurrentWithStatus();
                Contracts.Clear();

                int total = 0;
                int expiring = 0;

                foreach (var c in contracts)
                {
                    var vm = new ContractViewModel(c);
                    Contracts.Add(vm);
                    total++;

                    if (c.Status == "expired" || c.Status == "expiring" || c.Status == "warning")
                        expiring++;
                }

                TotalCount = total;
                ExpiringCount = expiring;
                StatusText = $"共 {total} 个合同，{expiring} 个需要关注";
                LastRefresh = DateTime.Now;
                OnPropertyChanged(nameof(LastRefreshText));
            }
            catch (Exception ex)
            {
                StatusText = $"加载失败: {ex.Message}";
            }
        }

        public void StartAutoRefresh()
        {
            _refreshTimer.Start();
        }

        public void StopAutoRefresh()
        {
            _refreshTimer.Stop();
        }

        private void ExecuteAddContract(object? _)
        {
            var dialog = new Views.ContractDialog(null, _config);
            dialog.Owner = OwnerWindow ?? Application.Current.MainWindow;
            if (dialog.ShowDialog() == true && dialog.Contract != null)
            {
                var contract = dialog.Contract;
                var id = _db.AddContract(contract.Name, contract.StartDate, contract.EndDate, contract.ReminderDays, contract.Notes, contract.StoragePath);

                LoadContracts();
            }
        }

        private void ExecuteRenewContract(object? _)
        {
            if (SelectedContract == null) return;
            var cvm = SelectedContract;

            var contract = new ContractRecord
            {
                Id = cvm.Id,
                GroupId = cvm.GroupId,
                Name = cvm.Name,
                StartDate = cvm.StartDate,
                EndDate = cvm.EndDate,
                ReminderDays = cvm.ReminderDays,
                Notes = cvm.Notes,
                StoragePath = cvm.StoragePath
            };
            var dialog = new Views.ContractDialog(contract, _config, isRenew: true);
            dialog.Owner = OwnerWindow ?? Application.Current.MainWindow;
            if (dialog.ShowDialog() == true && dialog.Contract != null)
            {
                var renewed = dialog.Contract;
                _db.RenewContract(cvm.Id, renewed.Name, renewed.StartDate, renewed.EndDate, renewed.Notes, renewed.StoragePath);

                LoadContracts();
            }
        }

        private void ExecuteEditContract(object? _)
        {
            if (SelectedContract == null) return;
            var cvm = SelectedContract;

            var contract = new ContractRecord
            {
                Id = cvm.Id,
                Name = cvm.Name,
                StartDate = cvm.StartDate,
                EndDate = cvm.EndDate,
                ReminderDays = cvm.ReminderDays,
                Notes = cvm.Notes,
                StoragePath = cvm.StoragePath
            };
            var dialog = new Views.ContractDialog(contract, _config, isEdit: true);
            dialog.Owner = OwnerWindow ?? Application.Current.MainWindow;
            if (dialog.ShowDialog() == true && dialog.Contract != null)
            {
                var c = dialog.Contract;
                _db.UpdateContract(c.Id, c.Name, c.StartDate, c.EndDate, c.ReminderDays, c.Notes, c.StoragePath);

                LoadContracts();
            }
        }

        private void ExecuteDeleteContract(object? _)
        {
            if (SelectedContract == null) return;

            var result = System.Windows.MessageBox.Show(
                $"确定要删除合同「{SelectedContract.Name}」吗？\n此操作不可恢复。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _db.DeleteContract(SelectedContract.Id);
                LoadContracts();
            }
        }

        private void ExecuteOpenFolder(object? _)
        {
            if (SelectedContract?.StoragePath != null && Directory.Exists(SelectedContract.StoragePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelectedContract.StoragePath,
                    UseShellExecute = true
                });
            }
            else
            {
                var storagePath = _config.GetStoragePath();
                if (Directory.Exists(storagePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = storagePath,
                        UseShellExecute = true
                    });
                }
            }
        }

        private void ExecuteOpenSettings(object? _)
        {
            var dialog = new Views.SettingsDialog(_config);
            dialog.Owner = OwnerWindow ?? Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        private void ExecuteRefresh(object? _)
        {
            LoadContracts();
        }

        private void ExecuteViewDetail(object? _)
        {
            if (SelectedContract == null) return;

            var dialog = new Views.DetailDialog(SelectedContract.Id, _db, _config);
            dialog.Owner = OwnerWindow ?? Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        private void ExecuteCheckReminders(object? _)
        {
            try
            {
                // 自动检查并注册任务计划（如果尚未注册）
                AutoRegisterTaskIfNeeded();

                using var reminder = new ReminderService();
                var messages = reminder.CheckExpiring(_db, _config);

                if (messages.Count == 0)
                {
                    // 无预警合同：显示气泡提示
                    ShowBalloonTip("合同到期提醒", "当前没有需要预警的合同，一切正常！", System.Windows.Forms.ToolTipIcon.Info);

                    System.Windows.MessageBox.Show(
                        "当前没有需要预警的合同，一切正常！",
                        "提醒检查",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    var fullText = string.Join("\n\n", messages);

                    // 气泡弹窗提醒
                    ShowBalloonTip(
                        $"合同到期提醒（{messages.Count} 个合同）",
                        string.Join("\n", messages.Select(m => m.Split('\n')[0])),
                        System.Windows.Forms.ToolTipIcon.Warning);

                    // 企业微信推送
                    if (_config.WebhookEnabled && !string.IsNullOrEmpty(_config.WebhookUrl))
                    {
                        var webhookContent = $"【合同到期提醒】\n\n发现 {messages.Count} 个需要预警的合同：\n\n{fullText}";
                        reminder.SendWeChatWebhook(_config.WebhookUrl, webhookContent).ContinueWith(task =>
                        {
                            if (task.IsFaulted || !task.Result)
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    System.Windows.MessageBox.Show(
                                        "企业微信推送失败，请检查 Webhook 配置。",
                                        "推送提醒",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning);
                                });
                            }
                        });
                    }

                    // 弹窗显示（中文友好格式）
                    System.Windows.MessageBox.Show(
                        $"发现 {messages.Count} 个需要预警的合同：\n\n{fullText}",
                        "合同到期提醒",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"提醒检查失败: {ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowBalloonTip(string title, string text, System.Windows.Forms.ToolTipIcon icon)
        {
            try
            {
                if (NotifyIcon != null)
                {
                    NotifyIcon.BalloonTipTitle = title;
                    NotifyIcon.BalloonTipText = text;
                    NotifyIcon.BalloonTipIcon = icon;
                    NotifyIcon.ShowBalloonTip(10000);
                }
            }
            catch
            {
                // NotifyIcon 不可用时静默忽略
            }
        }

        /// <summary>
        /// 检查定时任务是否已注册，未注册则自动注册。
        /// </summary>
        private void AutoRegisterTaskIfNeeded()
        {
            try
            {
                using var reminder = new ReminderService();
                if (reminder.IsTaskRegistered())
                    return;

                var exePath = Assembly.GetExecutingAssembly().Location;
                if (exePath.EndsWith(".dll"))
                    exePath = exePath.Replace(".dll", ".exe");

                var time = _config.ReminderTime;
                var result = reminder.RegisterDailyCheckTask(exePath, time);

                if (result.Success)
                {
                    StatusText = $"已自动注册定时任务：{result.Message}";
                }
            }
            catch
            {
                // 自动注册失败不影响提醒功能
            }
        }
    }
}