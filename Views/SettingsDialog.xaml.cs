using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using ContractManager.Services;

namespace ContractManager.Views
{
    public partial class SettingsDialog : Window
    {
        private readonly ConfigManager _config;
        private readonly ReminderService _reminderService;
        private readonly DatabaseService? _db;

        /// <summary>
        /// Whether a backup import was performed and the caller should reload data.
        /// </summary>
        public bool BackupImported { get; private set; }

        public SettingsDialog(ConfigManager config) : this(config, null) { }

        public SettingsDialog(ConfigManager config, DatabaseService? db)
        {
            InitializeComponent();

            _config = config;
            _db = db;
            _reminderService = new ReminderService();

            LoadSettings();
            CheckTaskStatus();
        }

        private void LoadSettings()
        {
            StoragePathTextBox.Text = _config.StoragePath;
            DefaultReminderDaysSlider.Value = _config.DefaultReminderDays;
            WebhookEnabledCheckBox.IsChecked = _config.WebhookEnabled;
            WebhookUrlTextBox.Text = _config.WebhookUrl;
            ReminderTimeTextBox.Text = _config.ReminderTime;
            AutoStartCheckBox.IsChecked = _config.AutoStart;
        }

        private void CheckTaskStatus()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks", "/query /tn \"ContractManager_Reminder\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(5000);
                var output = p?.StandardOutput.ReadToEnd() ?? "";

                if (output.Contains("ContractManager_Reminder"))
                {
                    TaskStatusText.Text = $"状态: 已注册 (每天 {_config.ReminderTime})";
                }
                else
                {
                    TaskStatusText.Text = "状态: 未注册";
                }
            }
            catch (Exception ex)
            {
                TaskStatusText.Text = $"状态检查失败: {ex.Message}";
            }
        }

        private void BrowseStoragePathButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择文件存储目录",
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrEmpty(StoragePathTextBox.Text) && Directory.Exists(StoragePathTextBox.Text))
            {
                dialog.SelectedPath = StoragePathTextBox.Text;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                StoragePathTextBox.Text = dialog.SelectedPath;
            }
        }

        private async void TestWebhookButton_Click(object sender, RoutedEventArgs e)
        {
            var url = WebhookUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                WebhookTestResult.Text = "请输入 Webhook URL";
                WebhookTestResult.Foreground = System.Windows.Media.Brushes.Orange;
                return;
            }

            WebhookTestResult.Text = "正在测试...";
            WebhookTestResult.Foreground = System.Windows.Media.Brushes.Gray;

            var result = await _reminderService.TestWebhook(url);

            if (result)
            {
                WebhookTestResult.Text = "✓ 连接成功";
                WebhookTestResult.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                WebhookTestResult.Text = "✗ 连接失败";
                WebhookTestResult.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private async void RegisterTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var time = ReminderTimeTextBox.Text.Trim();
            if (string.IsNullOrEmpty(time))
            {
                MessageBox.Show("请输入提醒时间。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var exePath = Assembly.GetExecutingAssembly().Location;
            if (exePath.EndsWith(".dll"))
            {
                exePath = exePath.Replace(".dll", ".exe");
            }

            var result = await Task.Run(() => _reminderService.RegisterDailyCheckTask(exePath, time));

            if (result.Success)
            {
                TaskStatusText.Text = $"状态: {result.Message}";
                MessageBox.Show(result.Message, "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                TaskStatusText.Text = $"状态: {result.Message}";
                MessageBox.Show(result.Message, "失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UnregisterTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var result = _reminderService.UnregisterDailyCheckTask();

            if (result.Success)
            {
                TaskStatusText.Text = "状态: 已取消";
                MessageBox.Show(result.Message, "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(result.Message, "失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _config.StoragePath = StoragePathTextBox.Text.Trim();
            _config.DefaultReminderDays = (int)DefaultReminderDaysSlider.Value;
            _config.WebhookEnabled = WebhookEnabledCheckBox.IsChecked ?? false;
            _config.WebhookUrl = WebhookUrlTextBox.Text.Trim();
            _config.ReminderTime = ReminderTimeTextBox.Text.Trim();
            _config.AutoStart = AutoStartCheckBox.IsChecked ?? false;
            _config.Save();

            DialogResult = true;
            Close();
        }

        private void ExportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null)
            {
                MessageBox.Show("数据库服务不可用。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "SQLite 数据库|*.db|所有文件|*.*",
                DefaultExt = ".db",
                FileName = $"ContractManager_备份_{DateTime.Now:yyyyMMdd_HHmmss}.db"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _db.ExportBackup(dialog.FileName);
                    BackupStatusText.Text = $"✓ 备份已导出至: {dialog.FileName}";
                    BackupStatusText.Foreground = System.Windows.Media.Brushes.Green;
                    MessageBox.Show($"备份已成功导出至:\n{dialog.FileName}", "导出成功",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    BackupStatusText.Text = $"✗ 导出失败: {ex.Message}";
                    BackupStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    MessageBox.Show($"导出备份失败:\n{ex.Message}", "导出失败",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null)
            {
                MessageBox.Show("数据库服务不可用。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQLite 数据库|*.db|所有文件|*.*",
                DefaultExt = ".db",
                Title = "选择备份文件"
            };

            if (dialog.ShowDialog() == true)
            {
                var confirm = MessageBox.Show(
                    "导入备份将覆盖当前所有合同数据，此操作不可恢复！\n\n确定要继续吗？",
                    "确认导入",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes) return;

                try
                {
                    _db.ImportBackup(dialog.FileName);
                    BackupImported = true;
                    BackupStatusText.Text = $"✓ 备份已成功导入";
                    BackupStatusText.Foreground = System.Windows.Media.Brushes.Green;
                    MessageBox.Show("备份已成功导入，数据已恢复。", "导入成功",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    BackupStatusText.Text = $"✗ 导入失败: {ex.Message}";
                    BackupStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    MessageBox.Show($"导入备份失败:\n{ex.Message}", "导入失败",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void GetWebhookLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
