namespace ContractManager.Models
{
    public class AttachmentRecord
    {
        public long Id { get; set; }
        public long ContractId { get; set; }
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string? OriginalName { get; set; }
        public long? FileSize { get; set; }
        public string CreatedAt { get; set; } = "";
    }
}
