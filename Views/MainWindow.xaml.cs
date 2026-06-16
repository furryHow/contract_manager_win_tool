using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ContractManager.Models;
using ContractManager.Services;
using ContractManager.ViewModels;

namespace ContractManager.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(DatabaseService db, ConfigManager config)
        {
            InitializeComponent();

            _viewModel = new MainViewModel(db, config);
            _viewModel.OwnerWindow = this;
            _viewModel.NotifyIcon = (Application.Current as App)?.NotifyIcon;
            DataContext = _viewModel;

            Loaded += (_, _) =>
            {
                _viewModel.LoadContracts();
                _viewModel.StartAutoRefresh();
            };
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _viewModel.StopAutoRefresh();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "合同管理系统 v2.0\n\n" +
                "功能说明：\n" +
                "• 新增合同：添加新的合同记录\n" +
                "• 续签：为已有合同创建新版本\n" +
                "• 编辑：修改合同信息\n" +
                "• 删除：删除合同记录\n" +
                "• 刷新：重新加载合同列表\n" +
                "• 设置：配置存储路径、Webhook等\n\n" +
                "到期状态说明：\n" +
                "• 已过期（红色）：合同已到期\n" +
                "• 即将到期（橙色）：30天内到期\n" +
                "• 需关注（黄色）：60天内到期\n" +
                "• 正常（绿色）：60天以上",
                "帮助",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void RenewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ContractViewModel cvm)
            {
                _viewModel.SelectedContract = cvm;
                _viewModel.RenewContractCommand.Execute(null);
            }
        }

        private void DetailButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ContractViewModel cvm)
            {
                _viewModel.SelectedContract = cvm;
                _viewModel.ViewDetailCommand.Execute(null);
            }
        }

        private void PaidAmount_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.Tag is ContractViewModel cvm)
            {
                if (decimal.TryParse(tb.Text, out var paidAmount) && paidAmount >= 0)
                {
                    _viewModel.UpdatePaidAmount(cvm.Id, paidAmount);
                    cvm.PaidAmount = paidAmount;
                }
                else
                {
                    MessageBox.Show("请输入有效的非负数值。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    tb.Text = cvm.PaidAmount.ToString("N2");
                }
            }
        }
    }
}