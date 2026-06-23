using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ContractManager.Models;
using ContractManager.Services;

namespace ContractManager.Views
{
    public partial class DetailDialog : Window
    {
        private readonly long _contractId;
        private readonly DatabaseService _db;
        private readonly ConfigManager _config;

        public DetailDialog(long contractId, DatabaseService db, ConfigManager config)
        {
            InitializeComponent();

            _contractId = contractId;
            _db = db;
            _config = config;

            LoadContractDetails();
            LoadPaymentRecords();
            LoadContractHistory();
        }

        private void LoadContractDetails()
        {
            var contract = _db.GetContract(_contractId);
            if (contract == null)
            {
                MessageBox.Show("合同不存在或已被删除。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            Title = $"合同详情 - {contract.Name}";
            HeaderText.Text = $"合同详情 - {contract.Name}";

            NameText.Text = contract.Name;
            DateRangeText.Text = $"{contract.StartDate} - {contract.EndDate}";
            TotalAmountText.Text = contract.TotalAmount.ToString("N2");
            PaidAmountText.Text = contract.PaidAmount.ToString("N2");
            UnpaidAmountText.Text = (contract.TotalAmount - contract.PaidAmount).ToString("N2");
            ReminderDaysText.Text = $"提前 {contract.ReminderDays} 天";
            ReminderDateText.Text = contract.ReminderDate ?? "-";
            StoragePathText.Text = contract.StoragePath ?? "";
            NotesText.Text = string.IsNullOrEmpty(contract.Notes) ? "无" : contract.Notes;

            var attachments = _db.GetAttachments(_contractId);
            foreach (var att in attachments)
            {
                AttachmentsListBox.Items.Add(att.OriginalName ?? att.FileName);
            }

            if (string.IsNullOrEmpty(contract.StoragePath))
            {
                StoragePathText.Text = _config.GetStoragePath();
            }
        }

        private void LoadContractHistory()
        {
            var contracts = _db.GetContractHistory(_contractId);
            var historyItems = new List<HistoryItem>();

            foreach (var c in contracts.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id))
            {
                historyItems.Add(new HistoryItem
                {
                    Id = c.Id,
                    Name = c.Name,
                    Period = $"{c.StartDate} - {c.EndDate}",
                    TotalAmount = c.TotalAmount,
                    PaidAmount = c.PaidAmount,
                    CreatedAt = c.CreatedAt,
                    Notes = c.Notes ?? ""
                });
            }

            HistoryGrid.ItemsSource = historyItems;
        }

        private void LoadPaymentRecords()
        {
            var payments = _db.GetPaymentRecords(_contractId);
            PaymentGrid.ItemsSource = payments;

            var totalPaid = _db.GetTotalPaidAmount(_contractId);
            TotalPaidText.Text = totalPaid.ToString("N2") + " 元";

            var contract = _db.GetContract(_contractId);
            if (contract != null)
            {
                var unpaid = contract.TotalAmount - totalPaid;
                UnpaidText.Text = unpaid.ToString("N2") + " 元";
            }
        }

        private void AddPaymentButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PaymentInputDialog();
            if (dialog.ShowDialog() == true)
            {
                _db.AddPaymentRecord(_contractId, dialog.Amount, dialog.PaymentDate, dialog.Notes);
                LoadPaymentRecords();
            }
        }

        private void DeletePaymentButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is long paymentId)
            {
                var result = MessageBox.Show("确定要删除这条付款记录吗？", "确认删除",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _db.DeletePaymentRecord(paymentId);
                    LoadPaymentRecords();
                }
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var path = StoragePathText.Text;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(_config.GetStoragePath()))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _config.GetStoragePath(),
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("文件夹不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AttachmentsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AttachmentsListBox.SelectedItem is string fileName)
            {
                var storagePath = StoragePathText.Text;
                if (!string.IsNullOrEmpty(storagePath) && File.Exists(Path.Combine(storagePath, fileName)))
                {
                    var filePath = Path.Combine(storagePath, fileName);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private class HistoryItem
        {
            public long Id { get; set; }
            public string Name { get; set; } = "";
            public string Period { get; set; } = "";
            public decimal TotalAmount { get; set; }
            public decimal PaidAmount { get; set; }
            public string CreatedAt { get; set; } = "";
            public string Notes { get; set; } = "";
        }
    }
}