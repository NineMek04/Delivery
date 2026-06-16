namespace BackendApi.Security.Models;

public sealed record TokenSubject(
    string UserId,
    string Email,
    string DisplayName,
    string Role,
    string? ShopId = null);

