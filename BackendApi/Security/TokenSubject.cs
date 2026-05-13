namespace BackendApi.Security;

public sealed record TokenSubject(
    string UserId,
    string Email,
    string DisplayName,
    string Role);
