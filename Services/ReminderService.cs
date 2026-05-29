using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ContractManager.Models;

namespace ContractManager.Services
{
    public class ReminderService : IDisposable
    {
        private readonly HttpClient _httpClient = new();
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }

        public (bool Success, string Message) RegisterDailyCheckTask(string exePath, string reminderTime)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/delete /tn \"ContractManager_Reminder\" /f",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var delProc = Process.Start(psi);
                delProc?.WaitForExit(10000);

                var fullCmd = $"{exePath} --check";
                var createPsi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/create /tn \"ContractManager_Reminder\" /tr \"{fullCmd}\" /sc daily /st {reminderTime} /f",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(createPsi);
                if (proc == null)
                    return (false, "Failed to start schtasks");

                var output = proc.StandardOutput.ReadToEnd();
                var error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(10000);

                if (proc.ExitCode == 0)
                    return (true, $"Daily check task registered at {reminderTime}");
                return (false, error);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public (bool Success, string Message) UnregisterDailyCheckTask()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = "/delete /tn \"ContractManager_Reminder\" /f",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                    return (false, "Failed to start schtasks");

                var error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(10000);

                if (proc.ExitCode == 0)
                    return (true, "Scheduled task removed");
                return (false, error);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// 检查 Windows 任务计划是否已注册。
        /// </summary>
        public bool IsTaskRegistered()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = "/query /tn \"ContractManager_Reminder\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                return output.Contains("ContractManager_Reminder");
            }
            catch
            {
                return false;
            }
        }

        public List<string> CheckExpiring(DatabaseService db, ConfigManager config)
        {
            var contracts = db.GetExpiringContracts();
            if (contracts.Count == 0)
                return new List<string>();

            var messages = new List<string>();
            var today = DateTime.Today;

            foreach (var c in contracts)
            {
                if (!DateTime.TryParseExact(c.EndDate, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                {
                    messages.Add($"合同预警：{c.Name}\n到期日期：{c.EndDate}（剩余天数未知）");
                    continue;
                }
                var remaining = (end - today).Days;
                if (remaining < 0)
                    messages.Add($"合同已过期：{c.Name}\n到期日期：{c.EndDate}（已过期 {-remaining} 天）");
                else if (remaining == 0)
                    messages.Add($"合同今天到期：{c.Name}\n到期日期：{c.EndDate}（今天到期）");
                else
                    messages.Add($"合同即将到期：{c.Name}\n到期日期：{c.EndDate}（剩余 {remaining} 天）");
            }

            return messages;
        }

        public async Task<bool> SendWeChatWebhook(string webhookUrl, string content)
        {
            try
            {
                var payload = new
                {
                    msgtype = "text",
                    text = new { content }
                };
                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(webhookUrl, httpContent);
                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement.TryGetProperty("errcode", out var code) && code.GetInt32() == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> TestWebhook(string webhookUrl)
        {
            return await SendWeChatWebhook(webhookUrl, "ContractManager Webhook test OK");
        }

        public string GenerateIcsForContract(ContractRecord contract, int? reminderDays = null)
        {
            if (!DateTime.TryParseExact(contract.EndDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
                return "";

            var rdays = reminderDays ?? contract.ReminderDays;
            var reminderDate = endDate.AddDays(-rdays);
            var dtstamp = DateTime.Now.ToString("yyyyMMddTHHmmssZ");
            var uid = $"contract-mgr-{contract.Id}-remind@local";

            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//ContractManager//CN");
            sb.AppendLine("CALSCALE:GREGORIAN");
            sb.AppendLine("METHOD:PUBLISH");

            if (reminderDate > DateTime.Today.AddDays(-1))
            {
                var dtStart = new DateTime(reminderDate.Year, reminderDate.Month, reminderDate.Day, 9, 0, 0);
                var dtEnd = new DateTime(reminderDate.Year, reminderDate.Month, reminderDate.Day, 10, 0, 0);
                sb.AppendLine("BEGIN:VEVENT");
                sb.AppendLine($"UID:{uid}");
                sb.AppendLine($"DTSTAMP:{dtstamp}");
                sb.AppendLine($"DTSTART:{dtStart:yyyyMMddTHHmmss}");
                sb.AppendLine($"DTEND:{dtEnd:yyyyMMddTHHmmss}");
                sb.AppendLine($"SUMMARY:Contract expiring - {contract.Name}");
                sb.AppendLine($"DESCRIPTION:Contract \"{contract.Name}\" expires on {contract.EndDate} (in {rdays} days). Period: {contract.StartDate} - {contract.EndDate}");
                sb.AppendLine("STATUS:CONFIRMED");
                sb.AppendLine("TRANSP:OPAQUE");
                sb.AppendLine("BEGIN:VALARM");
                sb.AppendLine("TRIGGER:-PT5M");
                sb.AppendLine("ACTION:DISPLAY");
                sb.AppendLine($"DESCRIPTION:Contract \"{contract.Name}\" expiring in {rdays} days!");
                sb.AppendLine("END:VALARM");
                sb.AppendLine("END:VEVENT");
            }

            sb.AppendLine("END:VCALENDAR");
            return sb.ToString();
        }

        public string GenerateIcsForContracts(IEnumerable<ContractRecord> contracts, int? reminderDays = null)
        {
            var dtstamp = DateTime.Now.ToString("yyyyMMddTHHmmssZ");
            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//ContractManager//CN");
            sb.AppendLine("CALSCALE:GREGORIAN");
            sb.AppendLine("METHOD:PUBLISH");

            foreach (var c in contracts)
            {
                if (!DateTime.TryParseExact(c.EndDate, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
                    continue;

                var rdays = reminderDays ?? c.ReminderDays;
                var reminderDate = endDate.AddDays(-rdays);
                var uid = $"contract-mgr-{c.Id}-remind@local";

                if (reminderDate > DateTime.Today.AddDays(-1))
                {
                    var dtStart = new DateTime(reminderDate.Year, reminderDate.Month, reminderDate.Day, 9, 0, 0);
                    var dtEnd = new DateTime(reminderDate.Year, reminderDate.Month, reminderDate.Day, 10, 0, 0);
                    sb.AppendLine("BEGIN:VEVENT");
                    sb.AppendLine($"UID:{uid}");
                    sb.AppendLine($"DTSTAMP:{dtstamp}");
                    sb.AppendLine($"DTSTART:{dtStart:yyyyMMddTHHmmss}");
                    sb.AppendLine($"DTEND:{dtEnd:yyyyMMddTHHmmss}");
                    sb.AppendLine($"SUMMARY:Contract expiring - {c.Name}");
                    sb.AppendLine($"DESCRIPTION:Contract \"{c.Name}\" expires on {c.EndDate} (in {rdays} days). Period: {c.StartDate} - {c.EndDate}");
                    sb.AppendLine("STATUS:CONFIRMED");
                    sb.AppendLine("BEGIN:VALARM");
                    sb.AppendLine("TRIGGER:-PT5M");
                    sb.AppendLine("ACTION:DISPLAY");
                    sb.AppendLine($"DESCRIPTION:Contract \"{c.Name}\" expiring in {rdays} days!");
                    sb.AppendLine("END:VALARM");
                    sb.AppendLine("END:VEVENT");
                }
            }

            sb.AppendLine("END:VCALENDAR");
            return sb.ToString();
        }
    }
}
