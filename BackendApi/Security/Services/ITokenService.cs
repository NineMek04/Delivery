using BackendApi.Security.Models;

namespace BackendApi.Security.Services;

public interface ITokenService
{
    string CreateAccessToken(TokenSubject subject, DateTime expiresAtUtc);
}

