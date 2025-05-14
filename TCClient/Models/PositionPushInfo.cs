using System;

namespace TCClient.Models
{
    public class PositionPushInfo
    {
        public long Id { get; set; }
        public string Contract { get; set; }
        public long AccountId { get; set; }
        public string Status { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? CloseTime { get; set; }
    }
} 