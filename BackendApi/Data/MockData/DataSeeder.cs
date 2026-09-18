using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Core.StateMachines;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System.Security.Cryptography;

namespace BackendApi.Data;

public static class DataSeeder
{
    /// <summary>
    /// ทำการ Seed ข้อมูลเริ่มต้นและข้อมูล Mock (หากระบุ) เพื่อให้ทุก Platform ใช้ทดสอบได้
    /// </summary>
    public static async Task SeedAsync(
        ApplicationDbContext context,
        string seedPassword,
        bool seedMockData = false)
    {
        if (string.IsNullOrWhiteSpace(seedPassword) || seedPassword.Length is < 12 or > 128)
        {
            throw new InvalidOperationException(
                "SeedAdminPassword must be configured with 12 to 128 characters.");
        }

        // ใช้ Transaction เพื่อให้แน่ใจว่าข้อมูลบันทึกได้อย่างปลอดภัยและเป็น Atomicity
        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var hashedPw = PasswordHasher.HashPassword(seedPassword);

            // 1. Seed ข้อมูลระบบที่จำเป็นเสมอ (Admin & Baseline Shops)
            await SeedSystemAdminAsync(context, seedPassword, hashedPw);
            await SeedInitialShopsAsync(context);

            // 2. Seed ข้อมูลจำลอง (Mock Data) เฉพาะเมื่อเปิดใช้งาน
            if (seedMockData)
            {
                await SeedMockDataAsync(context, seedPassword, hashedPw);
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task SeedSystemAdminAsync(
        ApplicationDbContext context,
        string seedPassword,
        string hashedPw)
    {
        var systemUsers = new List<User>
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
            }
        };

        foreach (var user in systemUsers)
        {
            var existingUser = await context.Users.FirstOrDefaultAsync(u => u.Email == user.Email);
            if (existingUser is null)
            {
                await context.Users.AddAsync(user);
                continue;
            }

            if (!PasswordMatchesSeed(seedPassword, existingUser.PasswordHash))
            {
                existingUser.PasswordHash = hashedPw;
                existingUser.RefreshToken = null;
                existingUser.RefreshTokenExpiresAt = null;
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedInitialShopsAsync(ApplicationDbContext context)
    {
        var initialShops = new List<Shop>
        {
            new Shop
            {
                Id = "e0000000-0000-0000-0000-000000000001",
                Name = "สวนสาธารณะหนองประจักษ์",
                MenuName = "ข้าวผัดหนองประจักษ์",
                MenuPrice = 55.00m,
                Location = new Point(102.780810, 17.418986) { SRID = 4326 },
                IsOpen = true,
                PrepTimeMinutes = 15,
                OpeningHours = "08:00-20:00",
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new Shop
            {
                Id = "e0000000-0000-0000-0000-000000000002",
                Name = "ทุ่งศรีเมืองอุดรธานี",
                MenuName = "เส้นเล็กน้ำตก",
                MenuPrice = 50.00m,
                Location = new Point(102.787000, 17.412000) { SRID = 4326 },
                IsOpen = true,
                PrepTimeMinutes = 10,
                OpeningHours = "09:00-18:00",
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new Shop
            {
                Id = "e0000000-0000-0000-0000-000000000003",
                Name = "เซ็นทรัล อุดรธานี",
                MenuName = "ชาไทยเย็นสุดเข้มข้น",
                MenuPrice = 60.00m,
                Location = new Point(102.799790, 17.405827) { SRID = 4326 },
                IsOpen = true,
                PrepTimeMinutes = 12,
                OpeningHours = "10:00-21:00",
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new Shop
            {
                Id = "e0000000-0000-0000-0000-000000000004",
                Name = "ยูดี ทาวน์",
                MenuName = "กาแฟดริปอุดรธานี",
                MenuPrice = 75.00m,
                Location = new Point(102.802100, 17.408500) { SRID = 4326 },
                IsOpen = true,
                PrepTimeMinutes = 15,
                OpeningHours = "07:00-22:00",
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new Shop
            {
                Id = "e0000000-0000-0000-0000-000000000005",
                Name = "ตลาดรถไฟอุดรธานี",
                MenuName = "หมูปิ้งสูตรโบราณ",
                MenuPrice = 40.00m,
                Location = new Point(102.805500, 17.404200) { SRID = 4326 },
                IsOpen = true,
                PrepTimeMinutes = 8,
                OpeningHours = "16:00-23:00",
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        };

        foreach (var shop in initialShops)
        {
            if (!await context.Shops.AnyAsync(s => s.Id == shop.Id))
            {
                await context.Shops.AddAsync(shop);
            }
        }
        await context.SaveChangesAsync();
    }

    private static Task SeedMockDataAsync(
        ApplicationDbContext context,
        string seedPassword,
        string hashedPw)
    {
        return Task.CompletedTask;
    }

    private static bool PasswordMatchesSeed(string seedPassword, string passwordHash)
    {
        try
        {
            return PasswordHasher.VerifyPassword(seedPassword, passwordHash);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}


