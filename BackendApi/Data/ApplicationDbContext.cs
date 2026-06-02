using BackendApi.Models;
using BackendApi.Core.Models;
using BackendApi.Services;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace BackendApi.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly ICurrentUserService _currentUserService;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentUserService currentUserService)
            : base(options)
        {
            _currentUserService = currentUserService;
        }

        public DbSet<Rider> Riders { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Shop> Shops { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<MenuItemOption> MenuItemOptions { get; set; }
        public DbSet<MenuItemOptionItem> MenuItemOptionItems { get; set; }

        public DbSet<RiderLocationHistory> RiderLocationHistories { get; set; }
        
        public DbSet<CustomerAddress> CustomerAddresses { get; set; }
        public DbSet<MenuCategory> MenuCategories { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<FcmToken> FcmTokens { get; set; }
        public DbSet<ProcessedEvent> ProcessedEvents { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }

        public override int SaveChanges()
        {
            ApplyAuditFields();
            ApplySoftDelete();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditFields();
            ApplySoftDelete();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyAuditFields()
        {
            var userId = _currentUserService.UserId;
            var userName = _currentUserService.UserName;
            var ipAddress = _currentUserService.IpAddress;
            var now = DateTime.UtcNow;

            foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAt = now;
                        entry.Entity.CreatedByUserId = userId;
                        entry.Entity.CreatedByName = userName;
                        entry.Entity.CreatedFromIp = ipAddress;
                        break;
                    case EntityState.Modified:
                        entry.Entity.UpdatedAt = now;
                        entry.Entity.UpdatedByUserId = userId;
                        entry.Entity.UpdatedByName = userName;
                        entry.Entity.UpdatedFromIp = ipAddress;
                        break;
                }
            }
        }

        private void ApplySoftDelete()
        {
            var userId = _currentUserService.UserId;
            var userName = _currentUserService.UserName;
            var ipAddress = _currentUserService.IpAddress;
            var now = DateTime.UtcNow;

            foreach (var entry in ChangeTracker.Entries<ISoftDeletableEntity>())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = now;
                    entry.Entity.DeletedByUserId = userId;
                    entry.Entity.DeletedByName = userName;
                    entry.Entity.DeletedFromIp = ipAddress;
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // บังคับให้สร้าง Extension PostGIS ในฐานข้อมูล
            modelBuilder.HasPostgresExtension("postgis");

            // ProcessedEvents — composite primary key for Idempotency
            modelBuilder.Entity<ProcessedEvent>()
                .HasKey(pe => new { pe.EventId, pe.HandlerName });

            // RiderLocationHistories — ไม่ลงทะเบียน Index ผ่าน EF Core Fluent API
            // เพราะตารางนี้เป็น Partitioned Table หลัง Migration Phase3EnterpriseSpatialScaling
            // Index ทั้งหมด (GiST + Composite B-tree) ถูกสร้างผ่าน Raw SQL ใน Migration แล้ว
            // การลงทะเบียนซ้ำที่นี่จะทำให้ EF Core พยายาม Drop/Recreate Index ในการ migrate ครั้งถัดไป

            // Riders — ใช้สำหรับ ST_DWithin (หา Rider ใกล้ Pickup)
            modelBuilder.Entity<Rider>()
                .HasIndex(r => r.CurrentLocation)
                .HasMethod("gist")
                .HasDatabaseName("IX_Riders_CurrentLocation_Gist");

            // Orders — ใช้สำหรับ Analytics และ Spatial Queries
            modelBuilder.Entity<Order>()
                .HasIndex(o => o.PickupLocation)
                .HasMethod("gist")
                .HasDatabaseName("IX_Orders_PickupLocation_Gist");

            modelBuilder.Entity<Order>()
                .HasIndex(o => o.DropoffLocation)
                .HasMethod("gist")
                .HasDatabaseName("IX_Orders_DropoffLocation_Gist");

            // Shops — ใช้สำหรับสืบค้นพิกัดร้านค้าเชิงพื้นที่ (GiST Index)
            modelBuilder.Entity<Shop>()
                .HasIndex(s => s.Location)
                .HasMethod("gist")
                .HasDatabaseName("IX_Shops_Location_Gist");

            // MenuItems — ใช้สำหรับค้นหาเมนูตามร้านค้า
            modelBuilder.Entity<MenuItem>()
                .HasIndex(m => m.ShopId)
                .HasDatabaseName("IX_MenuItems_ShopId");



            // Orders — B-tree สำหรับ GetMyOrders query (WHERE AssignedRiderId = ?)
            modelBuilder.Entity<Order>()
                .HasIndex(o => o.AssignedRiderId)
                .HasDatabaseName("IX_Orders_AssignedRiderId");

            // CustomerAddresses — spatial index for address locations
            modelBuilder.Entity<CustomerAddress>()
                .HasIndex(ca => ca.Location)
                .HasMethod("gist")
                .HasDatabaseName("IX_CustomerAddresses_Location_Gist");

            // CustomerAddresses — B-tree index for UserId
            modelBuilder.Entity<CustomerAddress>()
                .HasIndex(ca => ca.UserId)
                .HasDatabaseName("IX_CustomerAddresses_UserId");

            // MenuCategories — B-tree index for ShopId
            modelBuilder.Entity<MenuCategory>()
                .HasIndex(mc => mc.ShopId)
                .HasDatabaseName("IX_MenuCategories_ShopId");

            // MenuItem — B-tree index for MenuCategoryId
            modelBuilder.Entity<MenuItem>()
                .HasIndex(mi => mi.MenuCategoryId)
                .HasDatabaseName("IX_MenuItems_MenuCategoryId");

            // OrderItems — B-tree index for OrderId and MenuItemId
            modelBuilder.Entity<OrderItem>()
                .HasIndex(oi => oi.OrderId)
                .HasDatabaseName("IX_OrderItems_OrderId");

            modelBuilder.Entity<OrderItem>()
                .HasIndex(oi => oi.MenuItemId)
                .HasDatabaseName("IX_OrderItems_MenuItemId");

            // FcmTokens — B-tree indexes for UserId and Token
            modelBuilder.Entity<FcmToken>()
                .HasIndex(ft => ft.UserId)
                .HasDatabaseName("IX_FcmTokens_UserId");

            modelBuilder.Entity<FcmToken>()
                .HasIndex(ft => ft.Token)
                .HasDatabaseName("IX_FcmTokens_Token");

            // ChatMessages — B-tree index for OrderId
            modelBuilder.Entity<ChatMessage>()
                .HasIndex(cm => cm.OrderId)
                .HasDatabaseName("IX_ChatMessages_OrderId");

            // Unique filtered index on User.ShopId for StorePartners (soft delete aware)
            modelBuilder.Entity<User>()
                .HasIndex(u => u.ShopId)
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false AND \"ShopId\" IS NOT NULL")
                .HasDatabaseName("IX_Users_ShopId");

            // --- Universal Tracking & Reference Numbers (RefNumber) ---
            modelBuilder.Entity<Order>().Property(o => o.RefNumber).UseIdentityByDefaultColumn();
            modelBuilder.Entity<Order>().HasIndex(o => o.RefNumber).IsUnique().HasDatabaseName("IX_Orders_RefNumber");

            modelBuilder.Entity<Rider>().Property(r => r.RefNumber).UseIdentityByDefaultColumn();
            modelBuilder.Entity<Rider>().HasIndex(r => r.RefNumber).IsUnique().HasDatabaseName("IX_Riders_RefNumber");

            modelBuilder.Entity<Shop>().Property(s => s.RefNumber).UseIdentityByDefaultColumn();
            modelBuilder.Entity<Shop>().HasIndex(s => s.RefNumber).IsUnique().HasDatabaseName("IX_Shops_RefNumber");

            modelBuilder.Entity<MenuItem>().Property(m => m.RefNumber).UseIdentityByDefaultColumn();
            modelBuilder.Entity<MenuItem>().HasIndex(m => m.RefNumber).IsUnique().HasDatabaseName("IX_MenuItems_RefNumber");

            modelBuilder.Entity<User>().Property(u => u.RefNumber).UseIdentityByDefaultColumn();
            modelBuilder.Entity<User>().HasIndex(u => u.RefNumber).IsUnique().HasDatabaseName("IX_Users_RefNumber");

            modelBuilder.Entity<CustomerAddress>().Property(ca => ca.RefNumber).UseIdentityByDefaultColumn();
            modelBuilder.Entity<CustomerAddress>().HasIndex(ca => ca.RefNumber).IsUnique().HasDatabaseName("IX_CustomerAddresses_RefNumber");

            modelBuilder.Entity<MenuCategory>().Property(mc => mc.RefNumber).UseIdentityByDefaultColumn();
            modelBuilder.Entity<MenuCategory>().HasIndex(mc => mc.RefNumber).IsUnique().HasDatabaseName("IX_MenuCategories_RefNumber");

            var dateTimeConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                v => v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            var nullableDateTimeConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
                v => v.HasValue ? (v.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v.Value.ToUniversalTime()) : null,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (InheritsFromGenericBase(entityType.ClrType, typeof(BaseEntity<>)) &&
                    entityType.FindProperty(nameof(BaseEntity<string>.RowVersion)) is not null)
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property<byte[]>(nameof(BaseEntity<string>.RowVersion))
                        .HasDefaultValue(Array.Empty<byte>())
                        .IsRowVersion();
                }

                if (typeof(ISoftDeletableEntity).IsAssignableFrom(entityType.ClrType))
                {
                    modelBuilder.Entity(entityType.ClrType).HasQueryFilter(CreateSoftDeleteFilter(entityType.ClrType));
                }

                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(dateTimeConverter);
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(nullableDateTimeConverter);
                    }
                }
            }

            // Unique index on Email — ป้องกันอีเมลซ้ำที่ระดับ DB (เฉพาะที่ยังไม่ลบ)
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Email)
                    .IsUnique()
                    .HasFilter("\"IsDeleted\" = false");
                
                entity.Property(u => u.Email).HasMaxLength(100);
                entity.Property(u => u.FullName).HasMaxLength(100);
                entity.Property(u => u.Role).HasMaxLength(20);
            });

            base.OnModelCreating(modelBuilder);
        }

        private static bool InheritsFromGenericBase(Type candidateType, Type genericTypeDefinition)
        {
            var currentType = candidateType;

            while (currentType is not null && currentType != typeof(object))
            {
                if (currentType.IsGenericType &&
                    currentType.GetGenericTypeDefinition() == genericTypeDefinition)
                {
                    return true;
                }

                currentType = currentType.BaseType;
            }

            return false;
        }

        private static System.Linq.Expressions.LambdaExpression CreateSoftDeleteFilter(Type entityType)
        {
            var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
            var propertyMethod = typeof(EF).GetMethod(nameof(EF.Property), BindingFlags.Static | BindingFlags.Public)
                ?.MakeGenericMethod(typeof(bool));
            var isDeletedProperty = System.Linq.Expressions.Expression.Call(propertyMethod!, parameter, System.Linq.Expressions.Expression.Constant("IsDeleted"));
            var compareExpression = System.Linq.Expressions.Expression.Equal(isDeletedProperty, System.Linq.Expressions.Expression.Constant(false));
            return System.Linq.Expressions.Expression.Lambda(compareExpression, parameter);
        }
    }
}
