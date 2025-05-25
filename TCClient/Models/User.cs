using System;

namespace TCClient.Models
{
    public class User
    {
        public long Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string Email { get; set; }
        public DateTime? LastLoginTime { get; set; }
        public int Status { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
        
        // 为兼容性添加的别名属性
        public DateTime CreatedAt 
        { 
            get => CreateTime; 
            set => CreateTime = value; 
        }
    }
} 