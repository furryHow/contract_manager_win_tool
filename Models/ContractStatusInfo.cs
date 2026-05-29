namespace ContractManager.Models
{
    public class ContractStatusInfo
    {
        public long Id { get; set; }
        public long? GroupId { get; set; }
        public string Name { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public int ReminderDays { get; set; }
        public string? ReminderDate { get; set; }
        public string? Notes { get; set; }
        public string? StoragePath { get; set; }
        public int Remaining { get; set; }
        public string Status { get; set; } = "";
        public string StatusText { get; set; } = "";
        public int SortKey { get; set; }
    }
}
