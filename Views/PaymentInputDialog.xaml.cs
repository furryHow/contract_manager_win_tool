using System;
using System.Globalization;
using System.Windows;

namespace ContractManager.Views
{
    public partial class PaymentInputDialog : Window
    {
        public decimal Amount { get; private set; }
        public string PaymentDate { get; private set; } = "";
        public string? Notes { get; private set; }

        public PaymentInputDialog()
        {
            InitializeComponent();
            PaymentDatePicker.SelectedDate = DateTime.Today;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 验证金额
            if (!decimal.TryParse(AmountTextBox.Text.Trim(), out var amount) || amount <= 0)
            {
                MessageBox.Show("请输入有效的付款金额（大于0）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                AmountTextBox.Focus();
                return;
            }

            // 验证日期
            if (!PaymentDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("请选择付款日期。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                PaymentDatePicker.Focus();
                return;
            }

            Amount = amount;
            PaymentDate = PaymentDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd");
            Notes = NotesTextBox.Text;

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
