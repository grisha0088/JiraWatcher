using System.Data.Entity;

namespace DutyBot
{
    public class DbContext : System.Data.Entity.DbContext
    {
        public DbContext(): base("DbConnection"){ }
        public DbSet<Log> Logs { get; set; }
        public DbSet<Parametr> Parametrs { get; set; }
    }
}
