using BackendApi.Data;
using BackendApi.Services;
using BackendApi.Security;
using BackendApi.Models;
using BackendApi.Core.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace BackendApi.UnitTests.Security;

public class DataSeederSecurityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("           ")]
    public async Task SeedAsync_WhenBootstrapPasswordIsInvalid_FailsClosed(string password)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUserService>();
        await using var context = new ApplicationDbContext(options, currentUser.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DataSeeder.SeedAsync(context, password));

        Assert.Contains("SeedAdminPassword", exception.Message);
        Assert.Empty(context.Users);
    }

    [Fact]
    public async Task SeedAsync_WhenSeededAdminAlreadyExists_RotatesPasswordAndRevokesRefreshToken()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(
                Guid.NewGuid().ToString(),
                database => database.EnableNullChecks(false))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUserService>();
        await using var context = new ApplicationDbContext(options, currentUser.Object);
        var admin = new User
        {
            Id = "00000000-0000-0000-0000-000000000001",
            Email = "admin@delivery.com",
            FullName = "System Admin",
            Role = AuthConstants.AdminRole,
            PasswordHash = PasswordHasher.HashPassword("previous-secure-password"),
            RefreshToken = "stale-refresh-token",
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(1),
            RowVersion = new byte[8]
        };
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        const string newPassword = "new-bootstrap-password";
        await DataSeeder.SeedAsync(context, newPassword);

        Assert.True(PasswordHasher.VerifyPassword(newPassword, admin.PasswordHash));
        Assert.Null(admin.RefreshToken);
        Assert.Null(admin.RefreshTokenExpiresAt);
    }
}
