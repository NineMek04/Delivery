namespace BackendApi.Security;

public interface ITokenService
{
    string CreateAccessToken(TokenSubject subject, DateTime expiresAtUtc);
}
