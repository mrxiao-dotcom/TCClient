namespace TCClient.Helpers
{
    public static class DatabaseConfig
    {
        public const string Server = "154.23.181.75";
        public const string Database = "ltz";
        public const string Username = "root";
        public const string Password = "Xj774913@";
        public const int Port = 3306;

        public static string GetConnectionString()
        {
            return $"Server={Server};Port={Port};Database={Database};Uid={Username};Pwd={Password};";
        }
    }
} 