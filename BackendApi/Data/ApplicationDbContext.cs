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
        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // บังคับให้สร้าง Extension PostGIS ในฐานข้อมูล
            modelBuilder.HasPostgresExtension("postgis");

            // Unique index on Email — ป้องกันอีเมลซ้ำที่ระดับ DB
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Email).IsUnique();
                entity.Property(u => u.Email).HasMaxLength(100);
                entity.Property(u => u.FullName).HasMaxLength(100);
                entity.Property(u => u.Role).HasMaxLength(20);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}