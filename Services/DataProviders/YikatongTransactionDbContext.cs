using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Storage;
using wish_drom.Data.Entities;

namespace wish_drom.Services.DataProviders
{
    public class YikatongTransactionDbContext : DbContext
    {
        public DbSet<YikatongTransaction> Transactions { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "yikatong_transaction.db");
            options.UseSqlite($"Data Source={dbPath}");
        }
    }
}