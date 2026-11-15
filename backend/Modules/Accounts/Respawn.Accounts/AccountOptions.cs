namespace Respawn.Accounts;

/// <summary>The account rules an operator sets when the app starts.</summary>
public sealed class AccountOptions
{
    public int SessionDays { get; init; } = 30;

    public int PasswordMinLength { get; init; } = 12;

    /// <summary>Failed sign-ins before an account is locked.</summary>
    public int LockoutThreshold { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 15;

    public IReadOnlyList<OidcProvider> OidcProviders { get; init; } = [];
}

/// <summary>Instance settings an administrator changes at run time, by their stored key.</summary>
public static class InstanceFlags
{
    public const string LocalSignIn = "local_sign_in_enabled";

    public const string RegistrationOpen = "registration_open";
}
