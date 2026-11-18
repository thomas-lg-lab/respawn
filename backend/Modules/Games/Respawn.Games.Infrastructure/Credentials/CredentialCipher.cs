using System.Security.Cryptography;
using System.Text;
using Sodium;

namespace Respawn.Games.Infrastructure.Credentials;

/// <summary>
/// Seals an owner's access to a store before it is stored, and opens it for the sync: libsodium's
/// XChaCha20-Poly1305 (an authenticated encryption, through Sodium.Core) under the instance's key, with
/// a new random nonce each time. The linked account's id is bound to it as associated data: a sealed
/// value copied onto another account does not open there.
///
/// What is stored: a version byte, the nonce, then the sealed text. The version leaves room for another
/// key or another construction later; there is one today.
/// </summary>
internal sealed class CredentialCipher(Func<EncryptionOptions> options)
{
    private const byte Version = 1;

    private const int NonceBytes = 24;

    private static readonly byte[] Purpose = "respawn:games:linked-account-credential:v1:"u8.ToArray();

    /// <exception cref="CredentialException">The instance has no usable encryption key.</exception>
    public byte[] Seal(Guid linkedAccountId, string credential)
    {
        var key = options().Resolve();
        var clear = Encoding.UTF8.GetBytes(credential);
        try
        {
            var nonce = SecretAeadXChaCha20Poly1305.GenerateNonce();
            var text = SecretAeadXChaCha20Poly1305.Encrypt(clear, nonce, key, Bound(linkedAccountId));
            return [Version, .. nonce, .. text];
        }
        finally
        {
            // Neither the instance's key nor the owner's is left in a buffer of ours.
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    /// <exception cref="CredentialException">The instance has no usable encryption key, not the one it
    /// was sealed with, or what is stored was not sealed for this account.</exception>
    public string Open(Guid linkedAccountId, byte[] kept)
    {
        if (kept.Length <= 1 + NonceBytes || kept[0] != Version)
        {
            throw new CredentialException("it was not kept in a form this version of the app reads");
        }

        var key = options().Resolve();
        byte[]? clear = null;
        try
        {
            clear = SecretAeadXChaCha20Poly1305.Decrypt(kept[(1 + NonceBytes)..], kept[1..(1 + NonceBytes)], key, Bound(linkedAccountId));
            // The string handed to the sync is the one copy left: a string cannot be wiped.
            return Encoding.UTF8.GetString(clear);
        }
        catch (CryptographicException ex)
        {
            throw new CredentialException("the instance's encryption key is not the one it was kept with", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (clear is not null)
            {
                CryptographicOperations.ZeroMemory(clear);
            }
        }
    }

    private static byte[] Bound(Guid linkedAccountId) => [.. Purpose, .. linkedAccountId.ToByteArray(bigEndian: true)];
}
