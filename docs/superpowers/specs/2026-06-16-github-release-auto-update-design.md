# 在线自动更新功能设计（GitHub Release）

**日期：** 2026-06-16
**状态：** 待实施
**关联：** brainstorming session

## 1. 背景与动机

ContractManager 当前是绿色版分发（publish 文件夹打包），用户更新需要手动下载新版、解压替换。希望降低更新门槛——在设置对话框加一个按钮，点击后自动从 GitHub Release 拉取新版本并完成本地更新。

约束：**无服务器**（GitHub Release 即分发源，零运维成本）。

## 2. 目标

- 设置对话框提供「检查更新」按钮，点击后检测 GitHub 最新 Release
- 有新版时展示 changelog，用户确认后自动下载 + 覆盖安装 + 重启
- **零外部依赖**（纯手写，不引入第三方更新库），符合项目"手写极简"风格
- 失败可回滚，不损坏现有安装
- 数据目录 `%APPDATA%/ContractManager/` 全程不受影响

## 3. 非目标（YAGNI）

- 增量/差量更新（全量替换足够，框架依赖体积小）
- 代码签名（SmartScreen 警告不影响功能）
- 多通道/灰度发布
- 强制更新（始终用户主动触发）
- 启动时强制检查（提供配置项，默认关闭）

## 4. 前置条件 / 假设

- 仓库 `furryHow/contract_manager_win_tool` 为 **public**（已确认），匿名调用 GitHub API 无需 token
- 用户安装方式为**绿色版解压到任意目录**（已确认），安装目录不在 Program Files，无需 UAC
- 数据目录与 exe 目录分离（已验证：`%APPDATA%/ContractManager/` 存放 config.json / contracts.db / attachments）
- 目标用户使用 Windows 10 17763+（支持 `Expand-Archive`，与项目目标框架一致）

## 5. 架构

新增一个服务 `UpdateService`，遵循项目手动 DI 模式（`App.OnStartup` 实例化，传入视图）。updater 脚本由主程序在运行时**动态生成**，不作为 Content 文件维护（避免运行时被锁、且要进 zip 包的麻烦）。

### 5.1 组件清单

| 类型 | 路径 | 职责 |
|---|---|---|
| 新增服务 | `Services/UpdateService.cs` | GitHub API 调用、版本比对、下载、生成并启动 updater 脚本 |
| 新增模型 | `Models/UpdateInfo.cs` | 更新信息 POCO：Version、Changelog、DownloadUrl、IsPrerelease、ReleaseDate |
| 改 | `ContractManager.csproj` | 加 `<Version>2.1.0</Version>` 建立版本基准 |
| 改 | `Services/ConfigManager.cs` | 加 `update_repo` / `update_check_on_startup` / `update_skip_version` 配置项 + 便捷属性 |
| 改 | `App.xaml.cs` | 实例化 `UpdateService` 传给 `SettingsDialog`；「关于」对话框改读 Assembly 版本 |
| 改 | `Views/SettingsDialog.xaml(.cs)` | 加「检查更新」按钮、进度条、changelog 弹窗 |

## 6. 详细设计

### 6.1 UpdateService

持有单例 `HttpClient`（沿用 `ReminderService` 模式，`IDisposable`）。

公开方法：

- `Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)`
  - `GET https://api.github.com/repos/{update_repo}/releases/latest`
  - **必须带 header**：`Accept: application/vnd.github+json`、`User-Agent: ContractManager`（GitHub API 强制要求 UA，否则 403）
  - 解析 JSON：`tag_name`、`body`（changelog）、`assets[0].browser_download_url`、`prerelease`、`published_at`
  - 比对本地版本 vs tag（见下文「版本比对」）
  - 比对 `update_skip_version`，相等则视为无新版
  - 返回 `null` 表示无新版（本地已是最新或被跳过）

- `Task DownloadUpdateAsync(UpdateInfo info, string localZipPath, IProgress<double>? progress, CancellationToken ct = default)`
  - 流式下载到指定路径（调用方传 `%TEMP%\cm_update.zip`）
  - 通过 `IProgress<double>` 报告百分比进度

- `void ApplyUpdate(string localZipPath)`
  - 拼接 updater 脚本字符串，写到 `%TEMP%\cm_updater.bat`
  - `Process.Start` 启动（`UseShellExecute=false`，`CreateNoWindow=true`）
  - 调用方（SettingsDialog）随后通过 `(Application.Current as App)?.ExitApplication()` 触发主程序正常退出（清理托盘、释放 `_db`/`_reminderService`，最后 `Shutdown()`）。updater 脚本会轮询等待主进程退出后接管

**版本比对**：
- 本地版本读 `AssemblyInformationalVersionAttribute`（来自 csproj `<Version>`）
- 远端 tag 去掉 `v` 前缀（如 `v2.2.0` → `2.2.0`）
- 用 `Version.TryParse` 语义化比较；解析失败降级为字符串相等比对（不至于崩溃）

### 6.2 动态生成的 updater 脚本

由 `UpdateService.ApplyUpdate` 运行时拼接，写入 `%TEMP%\cm_updater.bat`，参数内嵌（zip 路径、安装目录、exe 名）。核心逻辑：

```
1. 等待主进程退出（轮询 tasklist /fi "imagename eq ContractManager.exe"，每秒一次，最多 30 次）
   - 超时 → echo 提示 + pause + exit /b 1
2. 备份: if exist "%TARGET%.bak" rmdir /s /q; xcopy "%TARGET%" "%TARGET%.bak\" /e/i/q/y
3. 解压覆盖: powershell -NoProfile -Command "Expand-Archive -Path '%ZIP%' -DestinationPath '%TARGET%' -Force"
   - errorlevel 1 → rmdir /s /q "%TARGET%" → move "%TARGET%.bak" "%TARGET%" → echo 已回滚 → pause → exit /b 1
4. 清理: rmdir /s /q "%TARGET%.bak"; del "%ZIP%"
5. 重启: start "" "%TARGET%\ContractManager.exe"
6. 自删: (goto) 2>nul & del "%~f0"
```

**zip 结构要求（关键）**：zip 根目录必须是 exe + dll + ico（不能套一层目录，如 `ContractManager_v2.2.0/ContractManager.exe`），否则 `Expand-Archive` 会解压出子目录导致失效。发布流程须保证。

**为什么动态生成而非维护独立 bat 文件**：
- 不占用 Content 文件槽位（无需进 zip 包）
- 主程序可把当前安装路径精确内嵌进脚本，无需运行时再传参解析
- 副本运行，安装目录里的原文件可被自由覆盖

### 6.3 SettingsDialog UI 改动

- 在设置页底部新增「检查更新」按钮
- 点击 → 异步调 `CheckForUpdateAsync`
  - 无新版 → MessageBox "当前已是最新版本 (vX.Y.Z)"
  - 有新版 → 弹自定义对话框（或复用现有对话框风格）展示 changelog + 按钮：
    - 「立即更新」→ 在 SettingsDialog 内嵌的进度条区域显示下载进度（`IProgress<double>` 报告百分比），调 `DownloadUpdateAsync`
    - 「以后再说」→ 关闭
    - 「跳过此版本」→ 写 `update_skip_version` 配置
- 下载完成 → 调 `ApplyUpdate` → `Application.ExitApplication()`（主程序退出后 updater 接管）
- 错误 → MessageBox 提示，不崩溃

### 6.4 版本基准建立

- `ContractManager.csproj` 加 `<Version>2.1.0</Version>`（本次功能作为 minor 升级）
- `App.ShowAbout()`（`App.xaml.cs:297`）改为读 `AssemblyInformationalVersionAttribute`，去掉硬编码 `v2.0`
- 发布时 tag 与 `<Version>` 对齐（tag 加 `v` 前缀）

## 7. 配置项

`ConfigManager` 新增（沿用 webhook 模式，三处同步改：默认字典 + 便捷属性 + SettingsDialog UI）：

| 配置项 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `update_repo` | string | `furryHow/contract_manager_win_tool` | GitHub 仓库全名 |
| `update_check_on_startup` | bool | `false` | 启动时自动检查（默认关，避免拖慢启动） |
| `update_skip_version` | string | `""` | 用户选择跳过的版本号 |

## 8. 错误处理矩阵

| 失败场景 | 处理 |
|---|---|
| 网络不通 / GitHub 不可达 | `CheckForUpdateAsync` 抛异常 → UI 提示"检查更新失败，请检查网络" |
| 已是最新版 | 返回 null → UI 提示"当前已是最新版本 (vX.Y.Z)" |
| 下载中断 / 校验失败 | 删除半成品 zip，提示重试 |
| GitHub API 速率限制（60/小时/IP） | 正常使用不触发；命中则提示"请求过于频繁，请稍后再试" |
| 主进程 30 秒内未退出 | updater 中止，保留 .bak，提示用户手动处理 |
| 解压覆盖失败 | 自动回滚 .bak，提示"更新失败已还原" |
| 版本号格式不规范 | 降级为字符串比对 |
| 本地被跳过的版本 | `CheckForUpdateAsync` 内比对 `update_skip_version`，相等则视为无新版 |

## 9. 发布流程（今后操作）

```
1. 改 csproj <Version>2.2.0</Version>，提交
2. dotnet publish -c Release -r win-x64 -o ./publish
3. 把 publish/ 内容打包为 ContractManager_v2.2.0.zip
   ⚠️ zip 根目录必须是 exe+dll+ico，不能套目录
4. git tag v2.2.0 && git push origin v2.2.0
5. GitHub 创建 Release（tag 选 v2.2.0），上传 zip 作为 asset
   - Release body = changelog（用户在更新对话框看到的）
```

## 10. 测试策略

项目无单元测试基础设施，本次不引入。手动端到端测试为主：
- 打测试 release（如 v9.9.9），走完整流程验证「检测→下载→覆盖→重启」
- 故意制造失败（断网、损坏 zip、锁文件）验证回滚
- 验证 .bak 回滚后程序可正常启动

## 11. 依赖与风险

- **无新外部依赖**（纯 .NET BCL + PowerShell `Expand-Archive`）
- GitHub API 匿名速率限制 60/小时/IP：单机正常使用不会触发
- 国内访问 GitHub 速度：Release asset 下载走 fastly CDN，速度看运营商；非本设计可解决的问题，必要时可后续加镜像源配置项
- `Expand-Archive` 在 Windows 10 17763+ 可用，与项目目标框架一致
