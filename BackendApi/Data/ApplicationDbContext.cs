using BackendApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Rider> Riders { get; set; }
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // บังคับให้สร้าง Extension PostGIS ในฐานข้อมูล
            modelBuilder.HasPostgresExtension("postgis");
            
            base.OnModelCreating(modelBuilder);
        }
    }
}