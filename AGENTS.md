# 项目知识库 — ContractManager

**生成时间：** 2026-06-16
**Commit：** 97a8ad1
**分支：** main

## 概述

基于 **WPF (.NET 7)** 的 Windows 单机桌面合同管理工具。核心功能：合同 CRUD、到期分级提醒（Toast / 气泡 / 企业微信 Webhook）、合同续签与历史追踪、附件归档、ICS 日历导出、系统托盘常驻、Windows 任务计划定时 `--check` 巡检。

技术栈：`net7.0-windows10.0.17763` + WPF + WinForms (NotifyIcon) + `Microsoft.Data.Sqlite` (手写 SQL，无 EF Core) + `Microsoft.Toolkit.Uwp.Notifications` (Toast)。MVVM 架构，手动 DI（无容器）。

## 结构

```
ContractManager/
├── App.xaml(.cs)                 # 入口：单实例 Mutex、--check 模式、托盘、服务初始化、开机自启
├── ContractManager.csproj        # 单项目，无 .sln
├── publish.bat                   # dotnet publish -c Release -r win-x64（框架依赖）
├── Models/
│   ├── ContractRecord.cs         # 合同 POCO（group_id 实现续签链）
│   ├── AttachmentRecord.cs       # 附件 POCO
│   └── ContractStatusInfo.cs     # 带运行时计算字段的视图模型（Remaining/Status/SortKey）
├── ViewModels/
│   ├── BaseViewModel.cs          # INotifyPropertyChanged + SetProperty<T>
│   └── MainViewModel.cs          # 主逻辑；含 RelayCommand、ContractViewModel、状态着色 Brush
├── Views/                        # XAML 窗口/对话框（code-behind 直接持有服务引用）
│   ├── MainWindow.xaml(.cs)      # DataGrid 合同列表，绑定 MainViewModel
│   ├── ContractDialog.xaml(.cs)  # 新增/编辑/续签（isEdit/isRenew 双标志）
│   ├── DetailDialog.xaml(.cs)    # 详情 + 附件 + ICS 导出
│   └── SettingsDialog.xaml(.cs)  # 配置 + 备份导入导出 + 任务计划注册
├── Services/
│   ├── DatabaseService.cs        # SQLite 连接（WAL，单连接复用），建表+迁移+CRUD+VACUUM INTO 备份
│   ├── ConfigManager.cs          # %APPDATA%/ContractManager/config.json，线程锁+原子写
│   └── ReminderService.cs        # schtasks 注册、CheckExpiring、企业微信 Webhook、ICS 生成
├── Converters/StatusColorConverter.cs
├── Styles/ContractStyles.xaml    # DataGrid/按钮/状态色样式
└── ico/favicon.ico
```

## 查找指南

| 我要…… | 去哪里 | 备注 |
|---|---|---|
| 改数据库 schema | `Services/DatabaseService.cs` `CreateTables()` + `MigrateSchema()` | 迁移用 `PRAGMA table_info` 增量 `ALTER TABLE` |
| 加配置项 | `Services/ConfigManager.cs` 默认字典 + 便捷属性 + SettingsDialog UI | 三处都要改 |
| 加新对话框 | `Views/` 建 XAML，在 `MainViewModel` 加 `RelayCommand` 调用 | 对话框直接 `new` 出来，不走 DI |
| 改状态分级阈值 | `DatabaseService.GetAllCurrentWithStatus()` | `<0` 过期 / `≤ReminderDays` 即将到期 / `≤ReminderDays*2` 预警 / 其余正常 |
| 改 Toast/Webhook/ICS | `Services/ReminderService.cs` | `--check` 入口在 `App.RunCheckAndNotify()` |
| 改续签/历史链 | `DatabaseService.RenewContract()` / `GetContractHistory()` | 用 `group_id` 串联，旧记录 `is_current=0` |
| 改任务计划命令 | `ReminderService.RegisterDailyCheckTask()` | 任务名硬编码 `ContractManager_Reminder` |
| 改单实例/托盘/自启 | `App.xaml.cs` | Mutex 名 `ContractManager_SingleInstance_Mutex`；信号 `ContractManager_ShowWindow_Signal` |

## CODE MAP（关键符号）

| 符号 | 类型 | 位置 | 角色 |
|---|---|---|---|
| `App` | class | `App.xaml.cs:18` | 入口；`OnStartup` 分支 `--check` vs GUI |
| `App.OnStartup` | method | `App.xaml.cs:34` | 互斥锁 + 托盘 + 窗口信号监听 + 服务装配 |
| `App.RunCheckAndNotify` | method | `App.xaml.cs:181` | `--check` 模式：Toast + Webhook，不弹 GUI |
| `App.IsMainInstanceRunning` | method | `App.xaml.cs:236` | 通过命名 EventWaitHandle 探测主实例 |
| `App.CheckAutoStart` | method | `App.xaml.cs:315` | 写 `HKCU\...\Run` 注册表 |
| `DatabaseService` | class | `Services/DatabaseService.cs:10` | 单 `SqliteConnection` 复用，`IDisposable` |
| `DatabaseService.MigrateSchema` | method | `Services/DatabaseService.cs:67` | 幂等增量加列 + 回填 reminder_date |
| `DatabaseService.GetAllCurrentWithStatus` | method | `Services/DatabaseService.cs:408` | 按剩余天数排序的状态视图 |
| `DatabaseService.RenewContract` | method | `Services/DatabaseService.cs:186` | 旧合同置 `is_current=0`，新合同继承 `group_id` |
| `DatabaseService.ExportBackup`/`ImportBackup` | method | `Services/DatabaseService.cs:457/468` | `VACUUM INTO` 导出；导入需关闭并重开连接 |
| `ConfigManager` | class | `Services/ConfigManager.cs:14` | 默认值+磁盘合并；`Save()` 原子 `.tmp`→`Move` |
| `ConfigManager.MigrateFromOldLocation` | method | `Services/ConfigManager.cs:54` | 首次运行从 exe 目录迁移到 `%APPDATA%` |
| `ReminderService` | class | `Services/ReminderService.cs:13` | 持有单例 `HttpClient` |
| `ReminderService.RegisterDailyCheckTask` | method | `Services/ReminderService.cs:27` | `schtasks /create /tn ContractManager_Reminder` |
| `ReminderService.CheckExpiring` | method | `Services/ReminderService.cs:129` | 返回中文消息列表 |
| `ReminderService.SendWeChatWebhook` | method | `Services/ReminderService.cs:158` | POST JSON，校验 `errcode==0` |
| `ReminderService.GenerateIcsForContract(s)` | method | `Services/ReminderService.cs:185/228` | ICS 含 `VALARM` 提前 5 分钟 |
| `MainViewModel` | class | `ViewModels/MainViewModel.cs:127` | 持有 `_db`/`_config`，`DispatcherTimer` 5 分钟刷新 |
| `RelayCommand` | class | `ViewModels/MainViewModel.cs:16` | 挂接 `CommandManager.RequerySuggested` |
| `ContractViewModel` | class | `ViewModels/MainViewModel.cs:45` | 含 `RowBackground`/`RowForeground` 状态着色 |

## 约定（本项目特有）

- **日期一律 `yyyy-MM-dd` 字符串**，解析用 `DateTime.TryParseExact(..., CultureInfo.InvariantCulture, ...)`。不要换 ISO 或本地化格式，否则状态计算全部失效。
- **合同续签链**用 `group_id` 串联（首次插入时 `group_id = id`）；只有 `is_current=1` 的行参与列表与提醒。删除按 `group_id` 级联全链。
- **状态字符串字面量** `expired` / `expiring` / `warning` / `normal` 在 `DatabaseService`、`ContractViewModel`、`StatusColorConverter` 多处硬编码 switch；改一处会断，需同步改全部。
- **运行时数据目录**恒为 `%APPDATA%/ContractManager/`（`config.json`、`contracts.db`、`attachments/`）。`ConfigManager` 构造时会从 exe 旧目录迁移一次。
- **手动 DI**：`App.OnStartup` 里 `new ConfigManager()` → `new DatabaseService(dbPath)` → `new MainWindow(db, config)`；`MainViewModel` 构造函数注入。无 DI 容器，新增服务沿用此模式。
- **SQLite 单连接**：`DatabaseService` 全生命周期复用一个 `SqliteConnection`（WAL 模式），未做线程安全；`ImportBackup` 临时关闭再重开。
- **配置原子写**：`ConfigManager.Save()` 写 `.tmp` 再 `File.Move(overwrite:true)`。
- **`Nullable>enable`** 已开，但服务字段大量 `!` 强断言（`_db!`、`_config!`）；新代码保持 nullable 正确性，不要新增 `as any`/`!` 掩盖。

## 反模式（本项目禁止 / 需注意）

- **不要把 `--check` 模式拆成独立进程/项目**：当前设计让 `--check` 进程在主实例缺席时「就地接管」成为主实例（复用已初始化的 `_db`/`_config`），改动这一时序会破坏单实例保证。
- **不要给 `SqliteConnection` 加并行多线程访问**：连接非线程安全，`ReminderService.SendWeChatWebhook` 的 `.ContinueWith` 回调里**不要**触碰 `_db`。
- **不要用 EF Core 替换 `DatabaseService`**：schema 迁移依赖现有 `PRAGMA table_info` 增量逻辑；`VACUUM INTO` 备份与 `ImportBackup` 的「关闭-替换-重开」流程与裸连接紧耦合。
- **`MainWindow.Closing` 被 code-behind 取消并 `Hide()`**（最小化到托盘）；若要真正退出必须走 `App.ExitApplication()` → `Shutdown()`。
- **`App.DispatcherUnhandledException` 已吞异常并弹框**，`args.Handled=true`；调试时若遇静默失败，先在此处下断点。
- **`schtasks` 超时硬编码 10000ms**，任务计划操作失败时仅返回 `(false, error)`，不会抛 — 排查「提醒没弹」先查任务计划是否真的注册（`schtasks /query /tn ContractManager_Reminder`）。

## 独特实现

- **双模式入口**：同一 exe 既是 GUI 主程序又是巡检 worker，靠 `args[0]=="--check"` 分流；`--check` 进程通过命名事件探测主实例，存在则纯通知后退出，不存在则就地升级为主实例（避免双进程）。
- **托盘清理仪式**：`CleanupNotifyIcon` 顺序 `Visible=false` → `Application.DoEvents()`（让 Shell 处理 `NIM_DELETE`）→ `Dispose()`，解决任务计划拉起后托盘幽灵图标残留。
- **续签 = 软删除 + 新插入**：旧记录 `is_current=0` 保留为历史，新记录继承 `group_id`，列表/详情通过 `group_id` 重组链路。
- **状态色双通道**：`ContractViewModel.RowBackground`/`RowForeground` 直接返回 `SolidColorBrush`，XAML 绑定；同时 `Converters/StatusColorConverter` 提供反向兜底。

## 命令

```bash
# 构建（需 .NET 7 SDK + Windows 10 17763+）
dotnet build

# 调试运行
dotnet run

# 模拟定时巡检（不启动 GUI，弹 Toast + 推 Webhook）
dotnet run -- --check

# 发布（框架依赖，win-x64）
dotnet publish -c Release --self-contained false -r win-x64 -o ./publish
# 或
.\publish.bat
```

> 无单元测试项目、无 CI/CD、无 Docker、无代码签名。`.gitignore` 仅忽略 `bin/`/`obj/`/`publish/`。

## 备注

- 运行时数据库/配置/附件均在 `%APPDATA%/ContractManager/`，不在仓库内；调试本机首次运行时 `ConfigManager` 会自动创建该目录。
- README.md 是权威用户文档（功能、配置项表、任务计划说明），本文件面向「修改代码」的工程师，两者互补不重复。
- `bin/`、`obj/`、`publish/` 为生成产物，含跨平台 runtime native 库（SQLite 等），勿手动改动。
