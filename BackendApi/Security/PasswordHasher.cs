using System.Security.Cryptography;
using System.Text;

namespace BackendApi.Security;

/// <summary>
/// Password hashing utility — ใช้ PBKDF2 (RFC 2898) สำหรับ hash password อย่างปลอดภัย
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;     // 128-bit salt
    private const int HashSize = 32;     // 256-bit hash
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// สร้าง hash จาก password พร้อม salt
    /// </summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            Algorithm,
            HashSize);

        // Format: iterations.algorithm.salt.hash (Base64)
        return $"{Iterations}.{Algorithm.Name}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// ตรวจสอบ password กับ hash ที่เก็บไว้
    /// </summary>
    public static bool VerifyPassword(string password, string passwordHash)
    {
        var parts = passwordHash.Split('.');
        if (parts.Length != 4) return false;

        if (!int.TryParse(parts[0], out var iterations)) return false;
        var algorithmName = parts[1];
        var salt = Convert.FromBase64String(parts[2]);
        var storedHash = Convert.FromBase64String(parts[3]);

        var algorithm = new HashAlgorithmName(algorithmName);
        var computedHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            algorithm,
            storedHash.Length);

        return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
    }
}
