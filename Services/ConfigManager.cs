using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContractManager.Services
{
    /// <summary>
    /// JSON configuration manager.
    /// Stores config at %APPDATA%/ContractManager/config.json.
    /// Thread-safe read/write with file locking.
    /// </summary>
    public class ConfigManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        private readonly string _configFilePath;
        private readonly string _appDir;
        private readonly object _lock = new();
        private Dictionary<string, object?> _data;

        public ConfigManager()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            _appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ContractManager"
            );
            Directory.CreateDirectory(_appDir);
            _configFilePath = Path.Combine(_appDir, "config.json");

            // Initialize with defaults
            _data = new Dictionary<string, object?>
            {
                ["storage_path"] = "",
                ["default_reminder_days"] = 90,
                ["webhook_enabled"] = false,
                ["webhook_url"] = "",
                ["reminder_time"] = "09:00",
                ["auto_start"] = true,
                ["update_repo"] = "furryHow/contract_manager_win_tool",
                ["update_check_on_startup"] = false,
                ["update_skip_version"] = "",
            };

            MigrateFromOldLocation(exeDir);
            Load();
        }

        /// <summary>
        /// Migrate config.json and contracts.db from old exe directory to APPDATA (first run only).
        /// </summary>
        private void MigrateFromOldLocation(string exeDir)
        {
            var oldConfig = Path.Combine(exeDir, "config.json");
            var oldDb = Path.Combine(exeDir, "contracts.db");
            var newConfig = _configFilePath;
            var newDb = Path.Combine(_appDir, "contracts.db");

            // Migrate config.json — only if target doesn't exist
            if (File.Exists(oldConfig) && !File.Exists(newConfig))
            {
                try { File.Copy(oldConfig, newConfig, overwrite: false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            // Migrate contracts.db — only if target doesn't exist
            if (File.Exists(oldDb) && !File.Exists(newDb))
            {
                try { File.Copy(oldDb, newDb, overwrite: false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Load config from disk, merging with defaults.
        /// </summary>
        private void Load()
        {
            if (!File.Exists(_configFilePath))
                return;

            lock (_lock)
            {
                try
                {
                    using var fs = new FileStream(
                        _configFilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read
                    );
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, object?>>(fs);
                    if (loaded != null)
                    {
                        foreach (var kvp in loaded)
                        {
                            _data[kvp.Key] = kvp.Value;
                        }
                    }
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
        }

        /// <summary>
        /// Save config to disk with exclusive file lock.
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                var tmpPath = _configFilePath + ".tmp";
                try
                {
                    // Write to temp file first, then atomically replace
                    using (var fs = new FileStream(
                        tmpPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None
                    ))
                    {
                        JsonSerializer.Serialize(fs, _data, JsonOptions);
                    }

                    File.Move(tmpPath, _configFilePath, overwrite: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                finally
                {
                    if (File.Exists(tmpPath))
                    {
                        try { File.Delete(tmpPath); }
                        catch (IOException) { }
                    }
                }
            }
        }

        // ---- Typed getters / setters ----

        public string? GetString(string key)
        {
            lock (_lock)
            {
                if (_data.TryGetValue(key, out var val) && val is JsonElement je)
                    return je.GetString();
                return val?.ToString();
            }
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            lock (_lock)
            {
                if (_data.TryGetValue(key, out var val))
                {
                    if (val is JsonElement je)
                        return je.TryGetInt32(out var i) ? i : defaultValue;
                    if (val is int iv) return iv;
                    if (val is long lv) return (int)lv;
                }
                return defaultValue;
            }
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            lock (_lock)
            {
                if (_data.TryGetValue(key, out var val))
                {
                    if (val is JsonElement je)
                        return je.GetBoolean();
                    if (val is bool bv) return bv;
                }
                return defaultValue;
            }
        }

        public void Set(string key, object? value)
        {
            lock (_lock)
            {
                _data[key] = value;
            }
            Save();
        }

        // ---- Convenience properties ----

        public string StoragePath
        {
            get => GetString("storage_path") ?? "";
            set => Set("storage_path", value);
        }

        public int DefaultReminderDays
        {
            get => GetInt("default_reminder_days", 90);
            set => Set("default_reminder_days", value);
        }

        public bool WebhookEnabled
        {
            get => GetBool("webhook_enabled");
            set => Set("webhook_enabled", value);
        }

        public string WebhookUrl
        {
            get => GetString("webhook_url") ?? "";
            set => Set("webhook_url", value);
        }

        public string ReminderTime
        {
            get => GetString("reminder_time") ?? "09:00";
            set => Set("reminder_time", value);
        }

        public bool AutoStart
        {
            get => GetBool("auto_start", true);
            set => Set("auto_start", value);
        }

        public string UpdateRepo
        {
            get => GetString("update_repo") ?? "furryHow/contract_manager_win_tool";
            set => Set("update_repo", value);
        }

        public bool UpdateCheckOnStartup
        {
            get => GetBool("update_check_on_startup");
            set => Set("update_check_on_startup", value);
        }

        public string UpdateSkipVersion
        {
            get => GetString("update_skip_version") ?? "";
            set => Set("update_skip_version", value);
        }

        /// <summary>
        /// Get the attachment storage path, ensuring the directory exists.
        /// Falls back to %APPDATA%/ContractManager/attachments.
        /// </summary>
        public string GetStoragePath()
        {
            var path = StoragePath;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path;

            // Default to APPDATA/ContractManager/attachments
            path = Path.Combine(_appDir, "attachments");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
