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

        public SettingsDialog(ConfigManager config)
        {
            InitializeComponent();

            _config = config;
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
