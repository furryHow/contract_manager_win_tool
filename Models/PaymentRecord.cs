using System;

namespace ContractManager.Models
{
    public class PaymentRecord
    {
        public long Id { get; set; }
        public long ContractId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentDate { get; set; } = "";
        public string? Notes { get; set; }
        public string CreatedAt { get; set; } = "";
    }
}
