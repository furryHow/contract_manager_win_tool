namespace ContractManager.Models
{
    public class ContractRecord
    {
        public long Id { get; set; }
        public long? GroupId { get; set; }
        public string Name { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public int ReminderDays { get; set; } = 90;
        public string? ReminderDate { get; set; }
        public bool IsCurrent { get; set; } = true;
        public string? Notes { get; set; }
        public string? StoragePath { get; set; }
        public string CreatedAt { get; set; } = "";
        public decimal TotalAmount { get; set; } = 0;
        public decimal PaidAmount { get; set; } = 0;
    }
}
