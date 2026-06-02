using BackendApi.Models;
using BackendApi.Core.StateMachines;
using BackendApi.Security;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace BackendApi.Data;

public static class DataSeeder
{
    /// <summary>
    /// ทำการ Seed ข้อมูล Mock เริ่มต้นเพื่อให้ทุก Platform (Admin Dashboard, Rider App, AI Engine) ใช้ทดสอบฟีเจอร์ได้
    /// </summary>
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        // ใช้ Transaction เพื่อให้แน่ใจว่าข้อมูลบันทึกได้อย่างปลอดภัยและเป็น Atomicity
        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var hashedPw = PasswordHasher.HashPassword("Password123!");

            // 1. Seed Riders (ข้อมูลผู้จัดส่ง)
            // ต้องบันทึก Rider ก่อนเพื่อเชื่อมต่อ RiderId ใน User
            var rider1Id = "11111111-1111-1111-1111-111111111111";
            var rider2Id = "22222222-2222-2222-2222-222222222222";
            var rider3Id = "33333333-3333-3333-3333-333333333333";

            var riders = new List<Rider>
            {
                new Rider
                {
                    Id = rider1Id,
                    Name = "Somchai Rider One",
                    State = RiderState.IDLE,
                    CurrentLocation = new Point(102.7872, 17.4138) { SRID = 4326 }, // Udon Thani Center
                    LastGpsUpdate = DateTime.UtcNow,
                    LastHeartbeat = DateTime.UtcNow
                },
                new Rider
                {
                    Id = rider2Id,
                    Name = "Somsri Rider Two",
                    State = RiderState.IDLE,
                    CurrentLocation = new Point(102.8072, 17.4038) { SRID = 4326 }, // UD Town Area
                    LastGpsUpdate = DateTime.UtcNow,
                    LastHeartbeat = DateTime.UtcNow
                },
                new Rider
                {
                    Id = rider3Id,
                    Name = "Anan Rider Three",
                    State = RiderState.OFFLINE,
                    CurrentLocation = new Point(102.7672, 17.4238) { SRID = 4326 }, // Nong Prajak West
                    LastGpsUpdate = DateTime.UtcNow.AddHours(-2),
                    LastHeartbeat = DateTime.UtcNow.AddHours(-2)
                }
            };

            foreach (var rider in riders)
            {
                if (!await context.Riders.AnyAsync(r => r.Id == rider.Id))
                {
                    await context.Riders.AddAsync(rider);
                }
            }
            await context.SaveChangesAsync(); // เซฟให้ ID ของ Rider เข้าสู่ระบบก่อน

            // 2. Seed Users (ใช้สำหรับเข้าสู่ระบบของทุกฝั่ง)
            var users = new List<User>
            {
                new User
                {
                    Id = "00000000-0000-0000-0000-000000000001",
                    Email = "admin@delivery.com",
                    FullName = "System Admin",
                    Role = AuthConstants.AdminRole,
                    PasswordHash = hashedPw,
                    IsActive = true,
                    IsDeleted = false
                },
                new User
                {
                    Id = "00000000-0000-0000-0000-000000000002",
                    Email = "ops@delivery.com",
                    FullName = "Operations Manager",
                    Role = AuthConstants.DispatcherRole,
                    PasswordHash = hashedPw,
                    IsActive = true,
                    IsDeleted = false
                },
                new User
                {
                    Id = "00000000-0000-0000-0000-000000000003",
                    Email = "customer1@delivery.com",
                    FullName = "Somchai Customer",
                    Role = "Customer",
                    PasswordHash = hashedPw,
                    IsActive = true,
                    IsDeleted = false
                },
                new User
                {
                    Id = "00000000-0000-0000-0000-000000000004",
                    Email = "customer2@delivery.com",
                    FullName = "Somsri Customer",
                    Role = "Customer",
                    PasswordHash = hashedPw,
                    IsActive = true,
                    IsDeleted = false
                },
                new User
                {
                    Id = "00000000-0000-0000-0000-000000000005",
                    Email = "rider1@delivery.com",
                    FullName = "Somchai Rider One",
                    Role = AuthConstants.RiderRole,
                    RiderId = rider1Id,
                    PasswordHash = hashedPw,
                    IsActive = true,
                    IsDeleted = false
                },
                new User
                {
                    Id = "00000000-0000-0000-0000-000000000006",
                    Email = "rider2@delivery.com",
                    FullName = "Somsri Rider Two",
                    Role = AuthConstants.RiderRole,
                    RiderId = rider2Id,
                    PasswordHash = hashedPw,
                    IsActive = true,
                    IsDeleted = false
                },
                new User
                {
                    Id = "00000000-0000-0000-0000-000000000007",
                    Email = "rider3@delivery.com",
                    FullName = "Anan Rider Three",
                    Role = AuthConstants.RiderRole,
                    RiderId = rider3Id,
                    PasswordHash = hashedPw,
                    IsActive = true,
                    IsDeleted = false
                }
            };

            foreach (var user in users)
            {
                if (!await context.Users.AnyAsync(u => u.Email == user.Email))
                {
                    await context.Users.AddAsync(user);
                }
            }
            await context.SaveChangesAsync();

            // 3. Seed Orders (ออเดอร์ในสถานะที่หลากหลายสำหรับใช้ทดสอบ flow)
            var orders = new List<Order>
            {
                // 1. Order ใหม่ที่กำลังรอประมวลผล (CREATED)
                new Order
                {
                    Id = "99999999-9999-9999-9999-000000000001",
                    State = OrderState.CREATED,
                    PickupLocation = new Point(102.7872, 17.4138) { SRID = 4326 }, // Udon Center
                    DropoffLocation = new Point(102.8072, 17.4038) { SRID = 4326 }, // UD Town
                    DistanceKm = 4.5,
                    DeliveryFee = 75.00m,
                    ExpectedDeliveryTime = DateTime.UtcNow.AddHours(1),
                    IsDeleted = false
                },
                // 2. Order ที่ระบบส่งเข้า AI เพื่อคำนวณและจับคู่ไรเดอร์ (MATCHING)
                new Order
                {
                    Id = "99999999-9999-9999-9999-000000000002",
                    State = OrderState.MATCHING,
                    PickupLocation = new Point(102.7918, 17.3938) { SRID = 4326 }, // Rajabhat University area
                    DropoffLocation = new Point(102.7718, 17.4238) { SRID = 4326 }, // Nong Prajak
                    DistanceKm = 3.8,
                    DeliveryFee = 68.00m,
                    ExpectedDeliveryTime = DateTime.UtcNow.AddHours(2),
                    IsDeleted = false
                },
                // 3. Order ที่ AI เลือกไรเดอร์แล้ว และระบบกำลังส่ง Offer ไปหา Rider 1 (OFFERING)
                new Order
                {
                    Id = "99999999-9999-9999-9999-000000000003",
                    State = OrderState.OFFERING,
                    PickupLocation = new Point(102.7850, 17.4100) { SRID = 4326 },
                    DropoffLocation = new Point(102.8050, 17.4200) { SRID = 4326 },
                    DistanceKm = 2.5,
                    DeliveryFee = 55.00m,
                    ExpectedDeliveryTime = DateTime.UtcNow.AddHours(1.5),
                    CurrentOfferId = "mock-offer-1001",
                    OfferVersion = 1,
                    OfferExpiresAt = DateTime.UtcNow.AddMinutes(5),
                    IsDeleted = false
                },
                // 4. Order ที่ถูกกดรับและมอบหมายงานให้ Rider 2 เรียบร้อย (ASSIGNED)
                new Order
                {
                    Id = "99999999-9999-9999-9999-000000000004",
                    State = OrderState.ASSIGNED,
                    PickupLocation = new Point(102.8100, 17.4000) { SRID = 4326 },
                    DropoffLocation = new Point(102.8200, 17.3800) { SRID = 4326 },
                    DistanceKm = 3.1,
                    DeliveryFee = 61.00m,
                    ExpectedDeliveryTime = DateTime.UtcNow.AddMinutes(45),
                    AssignedRiderId = rider2Id,
                    AssignedAt = DateTime.UtcNow.AddMinutes(-10),
                    IsDeleted = false
                },
                // 5. Order ที่ถูกจัดส่งสำเร็จเรียบร้อย (COMPLETED)
                new Order
                {
                    Id = "99999999-9999-9999-9999-000000000005",
                    State = OrderState.COMPLETED,
                    PickupLocation = new Point(102.7800, 17.4150) { SRID = 4326 },
                    DropoffLocation = new Point(102.7900, 17.4250) { SRID = 4326 },
                    DistanceKm = 1.8,
                    DeliveryFee = 48.00m,
                    ExpectedDeliveryTime = DateTime.UtcNow.AddHours(-1),
                    AssignedRiderId = rider1Id,
                    AssignedAt = DateTime.UtcNow.AddMinutes(-30),
                    CompletedAt = DateTime.UtcNow.AddMinutes(-15),
                    IsDeleted = false
                }
            };

            foreach (var order in orders)
            {
                if (!await context.Orders.AnyAsync(o => o.Id == order.Id))
                {
                    await context.Orders.AddAsync(order);
                }
            }

            // 4. Seed Rider Location Histories (ประวัติพิกัดสำหรับวิเคราะห์หรือจำลองการวิ่ง)
            if (!await context.RiderLocationHistories.AnyAsync())
            {
                var histories = new List<RiderLocationHistory>
                {
                    new RiderLocationHistory
                    {
                        Id = Guid.NewGuid().ToString(),
                        RiderId = rider1Id,
                        Location = new Point(102.7872, 17.4138) { SRID = 4326 },
                        RecordedAt = DateTime.UtcNow.AddMinutes(-10),
                        RecordedFromIp = "127.0.0.1"
                    },
                    new RiderLocationHistory
                    {
                        Id = Guid.NewGuid().ToString(),
                        RiderId = rider1Id,
                        Location = new Point(102.7890, 17.4125) { SRID = 4326 },
                        RecordedAt = DateTime.UtcNow.AddMinutes(-5),
                        RecordedFromIp = "127.0.0.1"
                    },
                    new RiderLocationHistory
                    {
                        Id = Guid.NewGuid().ToString(),
                        RiderId = rider2Id,
                        Location = new Point(102.8072, 17.4038) { SRID = 4326 },
                        RecordedAt = DateTime.UtcNow.AddMinutes(-8),
                        RecordedFromIp = "127.0.0.1"
                    }
                };

                await context.RiderLocationHistories.AddRangeAsync(histories);
            }

            // 5. Seed Shops (ข้อมูลร้านค้าสำหรับทดสอบ)
            if (await context.Shops.CountAsync() < 3)
            {
                var shops = new List<Shop>
                {
                    new Shop
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "ร้านกาแฟ ป้านงค์ (UD)",
                        MenuName = "ลาเต้เย็น",
                        MenuPrice = 65.00m,
                        Location = new Point(102.8050, 17.4020) { SRID = 4326 }, // แถว UD Town
                        CreatedAt = DateTime.UtcNow,
                        IsDeleted = false
                    },
                    new Shop
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "ก๋วยเตี๋ยวเนื้อ ตุ๋นยาจีน",
                        MenuName = "ก๋วยเตี๋ยวเนื้อเปื่อย",
                        MenuPrice = 80.00m,
                        Location = new Point(102.7910, 17.4100) { SRID = 4326 }, // กลางเมืองอุดร
                        CreatedAt = DateTime.UtcNow,
                        IsDeleted = false
                    },
                    new Shop
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "VT แหนมเนือง (สาขาใหญ่)",
                        MenuName = "ชุดแหนมเนือง ชุดใหญ่",
                        MenuPrice = 350.00m,
                        Location = new Point(102.7750, 17.4200) { SRID = 4326 }, // ใกล้หนองประจักษ์
                        CreatedAt = DateTime.UtcNow,
                        IsDeleted = false
                    }
                };
                
                await context.Shops.AddRangeAsync(shops);
            }

            // บันทึกข้อมูลและยืนยัน Transaction
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}