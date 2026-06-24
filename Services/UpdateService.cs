using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContractManager.Models;

namespace ContractManager.Services
{
    /// <summary>
    /// 在线自动更新服务（GitHub Release 分发）。
    /// 零外部依赖，纯 .NET BCL + PowerShell Expand-Archive。
    /// </summary>
    public class UpdateService : IDisposable
    {
        private readonly HttpClient _httpClient = new();
        private readonly ConfigManager _config;
        private bool _disposed;

        public UpdateService(ConfigManager config)
        {
            _config = config;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// 当前本地版本（来自 AssemblyInformationalVersion），去掉 source revision metadata。
        /// </summary>
        public static string CurrentVersion
        {
            get
            {
                var attr = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var v = attr?.InformationalVersion ?? "0.0.0";
                var plus = v.IndexOf('+');
                return plus > 0 ? v[..plus] : v;
            }
        }

        /// <summary>
        /// 检查 GitHub 最新 Release 是否有新版本。
        /// 返回 null 表示无新版（本地最新、被跳过、或仓库未配置）。
        /// 抛 HttpRequestException / JsonException 时由调用方捕获提示。
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
        {
            var repo = _config.UpdateRepo;
            if (string.IsNullOrWhiteSpace(repo))
                return null;

            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repo}/releases/latest");
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            // GitHub API 强制要求 User-Agent，否则 403
            request.Headers.UserAgent.ParseAdd("ContractManager");

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            UpdateInfo? info;
            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            {
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;

                var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
                var remoteVersion = tagName.TrimStart('v', 'V');
                var body = root.TryGetProperty("body", out var bodyEl)
                    ? bodyEl.GetString() ?? "" : "";
                var prerelease = root.TryGetProperty("prerelease", out var preEl)
                    && preEl.GetBoolean();
                var publishedAt = root.TryGetProperty("published_at", out var pubEl)
                    && pubEl.TryGetDateTime(out var dt) ? dt : DateTime.MinValue;

                string downloadUrl = "";
                if (root.TryGetProperty("assets", out var assetsEl)
                    && assetsEl.GetArrayLength() > 0)
                {
                    downloadUrl = assetsEl[0].TryGetProperty("browser_download_url", out var urlEl)
                        ? urlEl.GetString() ?? "" : "";
                }

                info = new UpdateInfo
                {
                    Version = remoteVersion,
                    Changelog = body,
                    DownloadUrl = downloadUrl,
                    IsPrerelease = prerelease,
                    ReleaseDate = publishedAt
                };
            }

            // 比对被跳过的版本：相等则视为无新版
            var skip = _config.UpdateSkipVersion;
            if (!string.IsNullOrEmpty(skip) && skip == info.Version)
                return null;

            // 版本比对：本地不新于远端则无新版
            if (!IsNewer(info.Version, CurrentVersion))
                return null;

            return info;
        }

        /// <summary>
        /// 判断 remote 是否严格新于 local。
        /// 用 Version.TryParse 语义化比较；任一解析失败降级为字符串不等比对。
        /// </summary>
        private static bool IsNewer(string remote, string local)
        {
            if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
                return r > l;
            return !string.Equals(remote, local, StringComparison.Ordinal);
        }

        /// <summary>
        /// 流式下载 zip 到指定路径，通过 IProgress&lt;double&gt; 报告百分比（0-100）。
        /// 若 localZipPath 已存在则先删除（清理上次半成品）。
        /// </summary>
        public async Task DownloadUpdateAsync(
            UpdateInfo info, string localZipPath,
            IProgress<double>? progress, CancellationToken ct = default)
        {
            if (File.Exists(localZipPath))
            {
                try { File.Delete(localZipPath); } catch { }
            }

            using var response = await _httpClient.GetAsync(
                info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var content = await response.Content.ReadAsStreamAsync(ct);
            await using var fs = new FileStream(
                localZipPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0 && progress != null)
                {
                    progress.Report((double)read / total * 100.0);
                }
            }
        }

        /// <summary>
        /// 生成 updater.bat 脚本并启动。
        /// 调用方随后必须触发主程序正常退出（App.ExitApplication），
        /// updater 脚本会轮询等待主进程退出后接管：备份→解压覆盖→失败回滚→重启→自删。
        /// </summary>
        public void ApplyUpdate(string localZipPath)
        {
            var installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            var exeName = Path.GetFileName(
                Environment.ProcessPath ?? "ContractManager.exe");

            var scriptPath = Path.Combine(Path.GetTempPath(), "cm_updater.bat");
            File.WriteAllText(scriptPath, BuildUpdaterScript(localZipPath, installDir, exeName));

            var psi = new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }

        /// <summary>
        /// 拼接 updater.bat 内容。路径用双引号包裹以兼容空格。
        /// 核心流程：等待主进程退出(≤30s) → 备份 → Expand-Archive 覆盖 → 失败回滚 → 清理 → 重启 → 自删。
        /// </summary>
        private static string BuildUpdaterScript(string zipPath, string installDir, string exeName)
        {
            var zip = Quote(zipPath);
            var target = Quote(installDir);
            var bak = Quote(installDir + ".bak");
            var exe = Quote(installDir + "\\" + exeName);

            return "@echo off\r\n"
                + "chcp 65001 >nul\r\n"
                + "set \"ZIP=" + zip + "\"\r\n"
                + "set \"TARGET=" + target + "\"\r\n"
                + "set \"BAK=" + bak + "\"\r\n"
                + "set \"EXE=" + exe + "\"\r\n"
                + "\r\n"
                + "echo 正在等待程序退出...\r\n"
                + "set /a count=0\r\n"
                + ":waitloop\r\n"
                + "tasklist /fi \"imagename eq " + exeName + "\" 2>nul | find /i \"" + exeName + "\" >nul\r\n"
                + "if %errorlevel%==0 (\r\n"
                + "    set /a count+=1\r\n"
                + "    if %count% geq 30 (\r\n"
                + "        echo 等待超时：程序在 30 秒内未退出，更新已取消。\r\n"
                + "        echo 请手动关闭程序后重新运行更新。\r\n"
                + "        pause\r\n"
                + "        exit /b 1\r\n"
                + "    )\r\n"
                + "    timeout /t 1 /nobreak >nul\r\n"
                + "    goto waitloop\r\n"
                + ")\r\n"
                + "\r\n"
                + "echo 正在备份当前版本...\r\n"
                + "if exist %BAK% rmdir /s /q %BAK%\r\n"
                + "xcopy %TARGET% %BAK% /e /i /q /y >nul\r\n"
                + "if %errorlevel% neq 0 (\r\n"
                + "    echo 备份失败，更新已取消。\r\n"
                + "    pause\r\n"
                + "    exit /b 1\r\n"
                + ")\r\n"
                + "\r\n"
                + "echo 正在解压新版本...\r\n"
                + "powershell -NoProfile -Command \"Expand-Archive -Path %ZIP% -DestinationPath %TARGET% -Force\"\r\n"
                + "if %errorlevel% neq 0 (\r\n"
                + "    echo 解压失败，正在回滚...\r\n"
                + "    rmdir /s /q %TARGET%\r\n"
                + "    move %BAK% %TARGET% >nul\r\n"
                + "    echo 已回滚到旧版本。\r\n"
                + "    pause\r\n"
                + "    exit /b 1\r\n"
                + ")\r\n"
                + "\r\n"
                + "echo 正在清理...\r\n"
                + "rmdir /s /q %BAK% 2>nul\r\n"
                + "del %ZIP% 2>nul\r\n"
                + "\r\n"
                + "echo 更新完成，正在重启程序...\r\n"
                + "start \"\" %EXE%\r\n"
                + "\r\n"
                + "(goto) 2>nul & del \"%~f0\"\r\n";

            static string Quote(string p) => "\"" + p + "\"";
        }
    }
}
