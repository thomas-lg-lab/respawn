using System.ComponentModel.DataAnnotations;
using Respawn.Accounts;

namespace Respawn.Accounts.Api;

// The bodies of the public API, version 1. Property names are written in snake_case on the wire.

public sealed record UserResponse(Guid Id, string? Email, string DisplayName, bool IsAdmin)
{
    public static UserResponse From(Account account) => new(account.Id, account.Email, account.DisplayName, account.IsAdmin);
}

public sealed record ProviderResponse(string Name, string Issuer, string ClientId);

public sealed record ConfigResponse(bool LocalSignIn, bool RegistrationOpen, IReadOnlyList<ProviderResponse> OidcProviders);

public sealed class RegisterRequest
{
    [Required, StringLength(320)]
    public string Email { get; init; } = "";

    [Required, StringLength(100)]
    public string DisplayName { get; init; } = "";

    [Required, StringLength(256)]
    public string Password { get; init; } = "";
}

public sealed class LoginRequest
{
    [Required, StringLength(320)]
    public string Email { get; init; } = "";

    [Required, StringLength(256)]
    public string Password { get; init; } = "";
}

public sealed class OidcRequest
{
    [Required, StringLength(64)]
    public string Provider { get; init; } = "";

    [Required, StringLength(16384)]
    public string IdToken { get; init; } = "";

    [Required, StringLength(256, MinimumLength = 8)]
    public string Nonce { get; init; } = "";
}
