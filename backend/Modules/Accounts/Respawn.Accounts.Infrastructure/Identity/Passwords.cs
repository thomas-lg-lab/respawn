using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Respawn.Accounts.Infrastructure.Persistence;
using Sodium;

namespace Respawn.Accounts.Infrastructure.Identity;

/// <summary>Argon2id through libsodium, plugged into ASP.NET Core Identity in place of its PBKDF2
/// default. libsodium picks the salt, encodes the parameters in the hash and compares in constant
/// time: nothing of that is written here.</summary>
internal sealed class Argon2PasswordHasher : IPasswordHasher<AppUser>
{
    private const PasswordHash.StrengthArgon Strength = PasswordHash.StrengthArgon.Interactive;

    private static readonly Lazy<string> Dummy = new(() => Hash(Tokens.New().Token));

    /// <summary>A hash of a password nobody knows. Verified against when the account does not exist or
    /// has no password, so a wrong e-mail costs as much time as a wrong password.</summary>
    public static string DummyHash => Dummy.Value;

    public string HashPassword(AppUser user, string password) => Hash(password);

    public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword)
    {
        if (!PasswordHash.ArgonHashStringVerify(hashedPassword, providedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        return PasswordHash.ArgonPasswordNeedsRehash(hashedPassword, Strength)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    // libsodium returns a fixed-size buffer: the padding after the hash is not part of it.
    private static string Hash(string password) => PasswordHash.ArgonHashString(password, Strength).TrimEnd('\0');
}

/// <summary>Opaque tokens: random bytes for the browser, their SHA-256 for the database.</summary>
internal static class Tokens
{
    public static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>A random token, and the digest that is stored.</summary>
    public static (string Token, string Hash) New()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, Digest(token));
    }
}
