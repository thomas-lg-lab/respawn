namespace Respawn.Accounts;

/// <summary>Why an account request was refused. The API turns each one into its status and code.</summary>
public enum AccountError
{
    InvalidEmail,
    WeakPassword,
    RegistrationClosed,
    AccountExists,

    /// <summary>The provider's e-mail belongs to an account: its owner signs in first, then links.</summary>
    LinkProviderFirst,
    LocalSignInDisabled,
    InvalidCredentials,
    UnknownProvider,
    InvalidToken,
    ProviderUnavailable,
}

public sealed class AccountException(AccountError error) : Exception(error.ToString())
{
    public AccountError Error { get; } = error;
}
