using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Storage;
using wish_drom.Data.Entities;

namespace wish_drom.Services.DataProviders
{
    public class YikatongBalanceDbContext : DbContext
    {
        public DbSet<YikatongBalance> Balances { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "yikatong_balance.db");
            options.UseSqlite($"Data Source={dbPath}");
        }
    }
}