using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ContractManager.Models;
using Microsoft.Data.Sqlite;

namespace ContractManager.Services
{
    public class DatabaseService : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection _connection;
        private bool _disposed;

        public DatabaseService(string dbPath)
        {
            _dbPath = dbPath;
            _connection = new SqliteConnection("Data Source=" + dbPath);
            _connection.Open();

            using var init = _connection.CreateCommand();
            init.CommandText = "PRAGMA foreign_keys = ON";
            init.ExecuteNonQuery();

            init.CommandText = "PRAGMA journal_mode = WAL";
            init.ExecuteNonQuery();

            CreateTables();
        }

        private void CreateTables()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS contracts (" +
                "    id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "    group_id INTEGER," +
                "    name TEXT NOT NULL," +
                "    start_date TEXT NOT NULL," +
                "    end_date TEXT NOT NULL," +
                "    reminder_days INTEGER DEFAULT 90," +
                "    reminder_date TEXT," +
                "    is_current INTEGER DEFAULT 1," +
                "    notes TEXT," +
                "    storage_path TEXT," +
                "    created_at TEXT DEFAULT (datetime('now','localtime'))," +
                "    total_amount REAL DEFAULT 0," +
                "    paid_amount REAL DEFAULT 0" +
                ")";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE IF NOT EXISTS attachments (" +
                "    id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "    contract_id INTEGER NOT NULL," +
                "    file_name TEXT NOT NULL," +
                "    file_path TEXT NOT NULL," +
                "    original_name TEXT," +
                "    file_size INTEGER," +
                "    created_at TEXT DEFAULT (datetime('now','localtime'))," +
                "    FOREIGN KEY (contract_id) REFERENCES contracts(id) ON DELETE CASCADE" +
                ")";
            cmd.ExecuteNonQuery();
            MigrateSchema();
        }


        private void MigrateSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(contracts)";
            var cols = new HashSet<string>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    cols.Add(reader.GetString(1));
            }

            if (!cols.Contains("storage_path"))
            {
                cmd.CommandText = "ALTER TABLE contracts ADD COLUMN storage_path TEXT";
                cmd.ExecuteNonQuery();
            }
            if (!cols.Contains("reminder_date"))
            {
                cmd.CommandText = "ALTER TABLE contracts ADD COLUMN reminder_date TEXT";
                cmd.ExecuteNonQuery();
                BackfillReminderDates();
            }
            if (!cols.Contains("total_amount"))
            {
                cmd.CommandText = "ALTER TABLE contracts ADD COLUMN total_amount REAL DEFAULT 0";
                cmd.ExecuteNonQuery();
            }
            if (!cols.Contains("paid_amount"))
            {
                cmd.CommandText = "ALTER TABLE contracts ADD COLUMN paid_amount REAL DEFAULT 0";
                cmd.ExecuteNonQuery();
            }
        }

        private void BackfillReminderDates()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, end_date, reminder_days FROM contracts WHERE reminder_date IS NULL";
            using var reader = cmd.ExecuteReader();
            var updates = new List<(long Id, string? Rd)>();

            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var endDate = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var reminderDays = reader.IsDBNull(2) ? 90 : reader.GetInt32(2);
                updates.Add((id, CalcReminderDate(endDate, reminderDays)));
            }

            using var up = _connection.CreateCommand();
            up.CommandText = "UPDATE contracts SET reminder_date = @rd WHERE id = @id";
            foreach (var (id, rd) in updates)
            {
                up.Parameters.Clear();
                up.Parameters.AddWithValue("@rd", rd ?? (object?)DBNull.Value);
                up.Parameters.AddWithValue("@id", id);
                up.ExecuteNonQuery();
            }
        }


        private ContractRecord ReadContract(SqliteDataReader reader)
        {
            return new ContractRecord
            {
                Id = reader.GetInt64(0),
                GroupId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                StartDate = reader.IsDBNull(3) ? "" : reader.GetString(3),
                EndDate = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ReminderDays = reader.IsDBNull(5) ? 90 : reader.GetInt32(5),
                ReminderDate = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsCurrent = reader.IsDBNull(7) || reader.GetInt32(7) == 1,
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
                StoragePath = reader.IsDBNull(9) ? null : reader.GetString(9),
                CreatedAt = reader.IsDBNull(10) ? "" : reader.GetString(10),
                TotalAmount = reader.IsDBNull(11) ? 0 : reader.GetDecimal(11),
                PaidAmount = reader.IsDBNull(12) ? 0 : reader.GetDecimal(12),
            };
        }

        public static string? CalcReminderDate(string endDateStr, int reminderDays)
        {
            if (DateTime.TryParseExact(endDateStr, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                return end.AddDays(-reminderDays).ToString("yyyy-MM-dd");
            }
            return null;
        }


        public long AddContract(string name, string startDate, string endDate,
            int reminderDays = 90, string? notes = null, string? storagePath = null,
            decimal totalAmount = 0, decimal paidAmount = 0)
        {
            var reminderDate = CalcReminderDate(endDate, reminderDays);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT INTO contracts (name, start_date, end_date, reminder_days, reminder_date, notes, storage_path, is_current, total_amount, paid_amount) VALUES (@name, @start, @end, @days, @rd, @notes, @sp, 1, @ta, @pa)";
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@start", startDate);
            cmd.Parameters.AddWithValue("@end", endDate);
            cmd.Parameters.AddWithValue("@days", reminderDays);
            cmd.Parameters.AddWithValue("@rd", reminderDate ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("@notes", notes ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("@sp", storagePath ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("@ta", totalAmount);
            cmd.Parameters.AddWithValue("@pa", paidAmount);
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT last_insert_rowid()";
            var newId = (long)cmd.ExecuteScalar()!;
            cmd.CommandText = "UPDATE contracts SET group_id = @gid WHERE id = @id";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@gid", newId);
            cmd.Parameters.AddWithValue("@id", newId);
            cmd.ExecuteNonQuery();
            return newId;
        }

        public long? RenewContract(long contractId, string newName, string newStart, string newEnd,
            string? notes = null, string? storagePath = null,
            decimal totalAmount = 0, decimal paidAmount = 0)
        {
            using var getCmd = _connection.CreateCommand();
            getCmd.CommandText = "SELECT group_id, reminder_days FROM contracts WHERE id = @id";
            getCmd.Parameters.AddWithValue("@id", contractId);
            long? groupId = null;
            int reminderDays = 90;
            using (var reader = getCmd.ExecuteReader())
            {
                if (!reader.Read()) return null;
                groupId = reader.IsDBNull(0) ? contractId : reader.GetInt64(0);
                reminderDays = reader.IsDBNull(1) ? 90 : reader.GetInt32(1);
            }
            var reminderDate = CalcReminderDate(newEnd, reminderDays);
            using var upCmd = _connection.CreateCommand();
            upCmd.CommandText = "UPDATE contracts SET is_current = 0 WHERE id = @id";
            upCmd.Parameters.AddWithValue("@id", contractId);
            upCmd.ExecuteNonQuery();
            using var insCmd = _connection.CreateCommand();
            insCmd.CommandText = "INSERT INTO contracts (group_id, name, start_date, end_date, reminder_days, reminder_date, is_current, notes, storage_path, total_amount, paid_amount) VALUES (@gid, @name, @start, @end, @days, @rd, 1, @notes, @sp, @ta, @pa)";
            insCmd.Parameters.AddWithValue("@gid", groupId);
            insCmd.Parameters.AddWithValue("@name", newName);
            insCmd.Parameters.AddWithValue("@start", newStart);
            insCmd.Parameters.AddWithValue("@end", newEnd);
            insCmd.Parameters.AddWithValue("@days", reminderDays);
            insCmd.Parameters.AddWithValue("@rd", reminderDate ?? (object?)DBNull.Value);
            insCmd.Parameters.AddWithValue("@notes", notes ?? (object?)DBNull.Value);
            insCmd.Parameters.AddWithValue("@sp", storagePath ?? (object?)DBNull.Value);
            insCmd.Parameters.AddWithValue("@ta", totalAmount);
            insCmd.Parameters.AddWithValue("@pa", paidAmount);
            insCmd.ExecuteNonQuery();
            insCmd.CommandText = "SELECT last_insert_rowid()";
            var newId = (long)insCmd.ExecuteScalar()!;
            return newId;
        }


        public List<ContractRecord> GetCurrentContracts()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, group_id, name, start_date, end_date, reminder_days, reminder_date, is_current, notes, storage_path, created_at, total_amount, paid_amount FROM contracts WHERE is_current = 1 ORDER BY end_date ASC";
            using var reader = cmd.ExecuteReader();
            var result = new List<ContractRecord>();
            while (reader.Read()) result.Add(ReadContract(reader));
            return result;
        }

        public ContractRecord? GetContract(long id)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, group_id, name, start_date, end_date, reminder_days, reminder_date, is_current, notes, storage_path, created_at, total_amount, paid_amount FROM contracts WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read()) return ReadContract(reader);
            return null;
        }

        public List<ContractRecord> GetContractHistory(long contractId)
        {
            using var getCmd = _connection.CreateCommand();
            getCmd.CommandText = "SELECT group_id FROM contracts WHERE id = @id";
            getCmd.Parameters.AddWithValue("@id", contractId);
            long? groupId = null;
            using (var reader = getCmd.ExecuteReader())
            {
                if (!reader.Read()) return new List<ContractRecord>();
                groupId = reader.IsDBNull(0) ? contractId : reader.GetInt64(0);
            }
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, group_id, name, start_date, end_date, reminder_days, reminder_date, is_current, notes, storage_path, created_at, total_amount, paid_amount FROM contracts WHERE id = @gid OR group_id = @gid2 ORDER BY created_at ASC";
            cmd.Parameters.AddWithValue("@gid", groupId);
            cmd.Parameters.AddWithValue("@gid2", groupId);
            using var reader2 = cmd.ExecuteReader();
            var result = new List<ContractRecord>();
            while (reader2.Read()) result.Add(ReadContract(reader2));
            return result;
        }

        public void UpdateContract(long id, string name, string startDate, string endDate,
            int reminderDays, string? notes = null, string? storagePath = null,
            decimal totalAmount = 0, decimal paidAmount = 0)
        {
            var reminderDate = CalcReminderDate(endDate, reminderDays);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE contracts SET name = @name, start_date = @start, end_date = @end, reminder_days = @days, reminder_date = @rd, notes = @notes, storage_path = @sp, total_amount = @ta, paid_amount = @pa WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@start", startDate);
            cmd.Parameters.AddWithValue("@end", endDate);
            cmd.Parameters.AddWithValue("@days", reminderDays);
            cmd.Parameters.AddWithValue("@rd", reminderDate ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("@notes", notes ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("@sp", storagePath ?? (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("@ta", totalAmount);
            cmd.Parameters.AddWithValue("@pa", paidAmount);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void UpdatePaidAmount(long id, decimal paidAmount)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE contracts SET paid_amount = @pa WHERE id = @id";
            cmd.Parameters.AddWithValue("@pa", paidAmount);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteContract(long contractId)
        {
            using var getCmd = _connection.CreateCommand();
            getCmd.CommandText = "SELECT group_id FROM contracts WHERE id = @id";
            getCmd.Parameters.AddWithValue("@id", contractId);
            long? groupId = null;
            using (var reader = getCmd.ExecuteReader())
            {
                if (reader.Read())
                    groupId = reader.IsDBNull(0) ? contractId : reader.GetInt64(0);
            }
            if (groupId == null) { DeleteSingleContract(contractId); return; }
            using var idsCmd = _connection.CreateCommand();
            idsCmd.CommandText = "SELECT id FROM contracts WHERE id = @gid OR group_id = @gid2";
            idsCmd.Parameters.AddWithValue("@gid", groupId.Value);
            idsCmd.Parameters.AddWithValue("@gid2", groupId.Value);
            var allIds = new List<long>();
            using (var reader = idsCmd.ExecuteReader())
            {
                while (reader.Read()) allIds.Add(reader.GetInt64(0));
            }
            foreach (var cid in allIds)
            {
                DeleteAttachmentFiles(cid);
                using var delAtt = _connection.CreateCommand();
                delAtt.CommandText = "DELETE FROM attachments WHERE contract_id = @cid";
                delAtt.Parameters.AddWithValue("@cid", cid);
                delAtt.ExecuteNonQuery();
            }
            using var delCmd = _connection.CreateCommand();
            delCmd.CommandText = "DELETE FROM contracts WHERE id = @gid3 OR group_id = @gid4";
            delCmd.Parameters.AddWithValue("@gid3", groupId.Value);
            delCmd.Parameters.AddWithValue("@gid4", groupId.Value);
            delCmd.ExecuteNonQuery();
        }


        private void DeleteSingleContract(long contractId)
        {
            DeleteAttachmentFiles(contractId);
            using var delAtt = _connection.CreateCommand();
            delAtt.CommandText = "DELETE FROM attachments WHERE contract_id = @id";
            delAtt.Parameters.AddWithValue("@id", contractId);
            delAtt.ExecuteNonQuery();
            using var delCmd = _connection.CreateCommand();
            delCmd.CommandText = "DELETE FROM contracts WHERE id = @id";
            delCmd.Parameters.AddWithValue("@id", contractId);
            delCmd.ExecuteNonQuery();
        }

        private void DeleteAttachmentFiles(long contractId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT file_path FROM attachments WHERE contract_id = @id";
            cmd.Parameters.AddWithValue("@id", contractId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        public long? AddAttachment(long contractId, string sourcePath, string storageDir)
        {
            if (!File.Exists(sourcePath)) return null;
            var originalName = Path.GetFileName(sourcePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var storedName = timestamp + "_" + originalName;
            var storedPath = Path.Combine(storageDir, storedName);
            Directory.CreateDirectory(storageDir);
            File.Copy(sourcePath, storedPath, overwrite: false);
            var fileSize = new FileInfo(storedPath).Length;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT INTO attachments (contract_id, file_name, file_path, original_name, file_size) VALUES (@cid, @fn, @fp, @on, @fs)";
            cmd.Parameters.AddWithValue("@cid", contractId);
            cmd.Parameters.AddWithValue("@fn", storedName);
            cmd.Parameters.AddWithValue("@fp", storedPath);
            cmd.Parameters.AddWithValue("@on", originalName);
            cmd.Parameters.AddWithValue("@fs", fileSize);
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT last_insert_rowid()";
            return (long)cmd.ExecuteScalar()!;
        }

        public List<AttachmentRecord> GetAttachments(long contractId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM attachments WHERE contract_id = @id ORDER BY created_at DESC";
            cmd.Parameters.AddWithValue("@id", contractId);
            using var reader = cmd.ExecuteReader();
            var result = new List<AttachmentRecord>();
            while (reader.Read())
            {
                result.Add(new AttachmentRecord {
                    Id = reader.GetInt64(0),
                    ContractId = reader.GetInt64(1),
                    FileName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    FilePath = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    OriginalName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    FileSize = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    CreatedAt = reader.IsDBNull(6) ? "" : reader.GetString(6),
                });
            }
            return result;
        }


        public List<ContractStatusInfo> GetAllCurrentWithStatus()
        {
            var today = DateTime.Today;
            var rows = GetCurrentContracts();
            var result = new List<ContractStatusInfo>();
            foreach (var c in rows)
            {
                if (!DateTime.TryParseExact(c.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate)) continue;
                if (!DateTime.TryParseExact(c.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
                var remaining = (int)(endDate - today).TotalDays;
                string status, statusText; int sortKey;
                if (remaining < 0) { status = "expired"; statusText = "\u5df2\u8fc7\u671f"; sortKey = -3; }
                else if (remaining <= c.ReminderDays) { status = "expiring"; statusText = "\u5373\u5c06\u5230\u671f(" + remaining + "\u5929)"; sortKey = -2; }
                else if (remaining <= c.ReminderDays * 2) { status = "warning"; statusText = "\u6b63\u5e38(" + remaining + "\u5929)"; sortKey = -1; }
                else { status = "normal"; statusText = "\u6b63\u5e38(" + remaining + "\u5929)"; sortKey = remaining; }
                result.Add(new ContractStatusInfo { Id = c.Id, GroupId = c.GroupId, Name = c.Name, StartDate = c.StartDate, EndDate = c.EndDate, ReminderDays = c.ReminderDays, ReminderDate = c.ReminderDate, Notes = c.Notes, StoragePath = c.StoragePath, TotalAmount = c.TotalAmount, PaidAmount = c.PaidAmount, Remaining = remaining, Status = status, StatusText = statusText, SortKey = sortKey });
            }
            result.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
            return result;
        }

        public List<ContractRecord> GetExpiringContracts()
        {
            var today = DateTime.Today;
            var rows = GetCurrentContracts();
            var result = new List<ContractRecord>();
            foreach (var row in rows)
            {
                if (!DateTime.TryParseExact(row.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) continue;
                if ((int)(end - today).TotalDays <= row.ReminderDays)
                    result.Add(row);
            }
            return result;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _connection?.Close();
                _connection?.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// Export the database to a backup file using VACUUM INTO.
        /// Does not require closing the current connection.
        /// </summary>
        public void ExportBackup(string targetPath)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{targetPath.Replace("'", "''")}'";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Import a backup file, replacing the current database.
        /// Closes the connection, replaces the file, then reopens.
        /// </summary>
        public void ImportBackup(string sourcePath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DatabaseService));

            // Close current connection
            _connection.Close();
            _connection.Dispose();

            try
            {
                // Replace current db with backup
                File.Copy(sourcePath, _dbPath, overwrite: true);
            }
            catch
            {
                // Restore attempt: reopen original if copy failed
                try
                {
                    _connection = new SqliteConnection("Data Source=" + _dbPath);
                    _connection.Open();
                }
                catch { }
                throw;
            }

            // Reopen connection
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            _connection.Open();

            using var init = _connection.CreateCommand();
            init.CommandText = "PRAGMA foreign_keys = ON";
            init.ExecuteNonQuery();
            init.CommandText = "PRAGMA journal_mode = WAL";
            init.ExecuteNonQuery();
        }
    }
}


