# ContractManager - 合同管理系统

基于 WPF 的 Windows 桌面合同管理工具，支持合同到期提醒、附件管理和企业微信 Webhook 通知。

## 功能

- **合同管理** — 新增、编辑、删除合同，支持合同续签和历史记录追踪
- **到期提醒** — 自定义预警天数，按剩余天数自动分级显示状态（已过期 / 即将到期 / 预警 / 正常）
- **Windows 通知** — 通过 Windows 任务计划定时执行 `--check` 模式，弹出 Toast 通知
- **企业微信通知** — 到期合同自动推送 Webhook 消息到企业微信群
- **日历订阅** — 导出 `.ics` 日历文件，导入 Outlook/日历应用
- **附件管理** — 合同附件自动归档存储
- **系统托盘** — 最小化到托盘运行，开机自启动
- **单实例** — 互斥锁确保只运行一个实例，重复启动自动激活窗口

## 技术栈

- .NET 7 (WPF)
- SQLite (Microsoft.Data.Sqlite)
- Windows Toast Notifications (Microsoft.Toolkit.Uwp.Notifications)
- MVVM 架构

## 项目结构

```
ContractManager/
├── App.xaml / App.xaml.cs    # 应用入口，托盘图标，单实例控制
├── Models/
│   ├── ContractRecord.cs     # 合同数据模型
│   ├── AttachmentRecord.cs   # 附件数据模型
│   └── ContractStatusInfo.cs # 合同状态视图模型
├── ViewModels/
│   ├── BaseViewModel.cs      # MVVM 基类
│   └── MainViewModel.cs      # 主窗口视图模型
├── Views/
│   ├── MainWindow.xaml       # 主窗口
│   ├── ContractDialog.xaml   # 合同编辑对话框
│   ├── DetailDialog.xaml     # 合同详情对话框
│   └── SettingsDialog.xaml   # 设置对话框
├── Services/
│   ├── DatabaseService.cs    # SQLite 数据库服务
│   ├── ConfigManager.cs      # JSON 配置管理
│   └── ReminderService.cs    # 提醒、Webhook、ICS 日历服务
├── Converters/               # XAML 值转换器
├── Styles/                   # XAML 样式资源
└── ico/                      # 应用图标
```

## 构建与发布

**环境要求：** .NET 7 SDK，Windows 10 17763+

```bash
# 构建
dotnet build

# 发布（依赖框架模式）
dotnet publish -c Release -r win-x64 -o ./publish
```

或直接运行 `publish.bat`。

## 配置

配置文件存储在 `%APPDATA%/ContractManager/config.json`：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `storage_path` | 附件存储目录 | `%APPDATA%/ContractManager/attachments` |
| `default_reminder_days` | 默认预警天数 | 90 |
| `webhook_enabled` | 启用 Webhook 通知 | false |
| `webhook_url` | 企业微信 Webhook 地址 | — |
| `reminder_time` | 每日检查时间 | 09:00 |
| `auto_start` | 开机自启动 | true |

## 定时提醒

程序在设置中注册 Windows 任务计划（`ContractManager_Reminder`），每日定时以 `--check` 模式启动：

1. 检查即将到期/已过期的合同
2. 弹出 Windows Toast 通知
3. 如已启用，推送 Webhook 消息到企业微信
4. 如主实例已在运行，进程自动退出；否则接管为主实例

## License

Private - All Rights Reserved
