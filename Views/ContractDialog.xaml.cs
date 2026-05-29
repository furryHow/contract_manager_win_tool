using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ContractManager.Models;
using ContractManager.Services;

namespace ContractManager.Views
{
    public partial class ContractDialog : Window
    {
        private readonly ConfigManager _config;
        private readonly string _mode;
        private readonly List<string> _attachmentFiles = new();
        private ContractRecord? _originalContract;
        private bool _initialized;

        public ContractRecord? Contract { get; private set; }

        public ContractDialog(ContractRecord? contract, ConfigManager config, bool isEdit = false, bool isRenew = false)
        {
            InitializeComponent();

            _config = config;
            _originalContract = contract;

            if (isRenew)
            {
                _mode = "Renew";
                HeaderText.Text = "续签合同";
                Title = "续签合同";
                LoadContractData(contract!);
            }
            else if (isEdit)
            {
                _mode = "Edit";
                HeaderText.Text = "编辑合同";
                Title = "编辑合同";
                LoadContractData(contract!);
            }
            else if (contract != null)
            {
                _mode = "Edit";
                HeaderText.Text = "编辑合同";
                Title = "编辑合同";
                LoadContractData(contract);
            }
            else
            {
                _mode = "Add";
                HeaderText.Text = "新增合同";
                Title = "新增合同";
                ReminderDaysSlider.Value = config.DefaultReminderDays;
            }

            _initialized = true;
            UpdateReminderDatePreview();
        }

        private void LoadContractData(ContractRecord contract)
        {
            NameTextBox.Text = contract.Name;

            if (DateTime.TryParseExact(contract.StartDate, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var startDate))
                StartDatePicker.SelectedDate = startDate;

            if (DateTime.TryParseExact(contract.EndDate, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var endDate))
                EndDatePicker.SelectedDate = endDate;

            ReminderDaysSlider.Value = contract.ReminderDays;
            NotesTextBox.Text = contract.Notes ?? "";

            if (!string.IsNullOrEmpty(contract.StoragePath) && Directory.Exists(contract.StoragePath))
            {
                try
                {
                    var files = Directory.GetFiles(contract.StoragePath);
                    foreach (var f in files)
                    {
                        _attachmentFiles.Add(f);
                        AttachmentsListBox.Items.Add(Path.GetFileName(f));
                    }
                }
                catch { }
            }

            UpdateReminderDatePreview();
        }

        private void EndDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateReminderDatePreview();
        }

        private void ReminderDaysSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initialized) return;
            UpdateReminderDatePreview();
        }

        private void UpdateReminderDatePreview()
        {
            if (ReminderDatePreview == null) return;
            var endDate = EndDatePicker.SelectedDate;
            if (endDate.HasValue)
            {
                var reminderDate = endDate.Value.AddDays(-(int)ReminderDaysSlider.Value);
                ReminderDatePreview.Text = reminderDate.ToString("yyyy-MM-dd");
            }
            else
            {
                ReminderDatePreview.Text = "选择结束日期后自动计算";
            }
        }

        private void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择附件",
                Multiselect = true,
                Filter = "所有文件 (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!_attachmentFiles.Contains(file))
                    {
                        _attachmentFiles.Add(file);
                        AttachmentsListBox.Items.Add(Path.GetFileName(file));
                    }
                }
            }
        }

        private void AttachmentsListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (File.Exists(file) && !_attachmentFiles.Contains(file))
                    {
                        _attachmentFiles.Add(file);
                        AttachmentsListBox.Items.Add(Path.GetFileName(file));
                    }
                }
            }
        }

        private void AttachmentsListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && AttachmentsListBox.SelectedIndex >= 0)
            {
                var index = AttachmentsListBox.SelectedIndex;
                if (index >= 0 && index < _attachmentFiles.Count)
                {
                    _attachmentFiles.RemoveAt(index);
                    AttachmentsListBox.Items.RemoveAt(index);
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text.Trim();
            var startDate = StartDatePicker.SelectedDate;
            var endDate = EndDatePicker.SelectedDate;

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入合同名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            if (!startDate.HasValue)
            {
                MessageBox.Show("请选择开始日期。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                StartDatePicker.Focus();
                return;
            }

            if (!endDate.HasValue)
            {
                MessageBox.Show("请选择结束日期。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                EndDatePicker.Focus();
                return;
            }

            if (endDate <= startDate)
            {
                MessageBox.Show("结束日期必须晚于开始日期。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                EndDatePicker.Focus();
                return;
            }

            var basePath = _config.GetStoragePath();
            var groupId = _originalContract?.GroupId;
            string storagePath;

            if (_mode == "Edit" && !string.IsNullOrEmpty(_originalContract?.StoragePath))
            {
                // 编辑模式：保留原有存储路径
                storagePath = _originalContract.StoragePath;
            }
            else
            {
                // 新增/续签模式：在配置路径下创建以合同名命名的子目录
                var safeName = SanitizeDirectoryName(name);
                storagePath = Path.Combine(basePath, safeName);
            }

            if (!Directory.Exists(storagePath))
                Directory.CreateDirectory(storagePath);

            Contract = new ContractRecord
            {
                Id = _originalContract?.Id ?? 0,
                GroupId = groupId,
                Name = name,
                StartDate = startDate.Value.ToString("yyyy-MM-dd"),
                EndDate = endDate.Value.ToString("yyyy-MM-dd"),
                ReminderDays = (int)ReminderDaysSlider.Value,
                ReminderDate = DatabaseService.CalcReminderDate(endDate.Value.ToString("yyyy-MM-dd"), (int)ReminderDaysSlider.Value),
                IsCurrent = true,
                Notes = NotesTextBox.Text,
                StoragePath = storagePath
            };

            if (_attachmentFiles.Count > 0)
            {
                foreach (var srcFile in _attachmentFiles)
                {
                    if (File.Exists(srcFile))
                    {
                        var destFile = Path.Combine(storagePath, Path.GetFileName(srcFile));
                        if (!File.Exists(destFile))
                        {
                            try { File.Copy(srcFile, destFile); }
                            catch { }
                        }
                    }
                }
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 清理合同名称中的非法文件名字符，确保可作为目录名使用。
        /// </summary>
        private static string SanitizeDirectoryName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name)
            {
                safe.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }
            var result = safe.ToString().Trim();
            if (string.IsNullOrEmpty(result))
                result = "未命名合同";
            return result;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}