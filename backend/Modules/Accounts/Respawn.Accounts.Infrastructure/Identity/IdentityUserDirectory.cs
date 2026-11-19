using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn.Accounts;
using Respawn.Accounts.Ports;
using Respawn.Accounts.Infrastructure.Persistence;

namespace Respawn.Accounts.Infrastructure.Identity;

/// <summary>Accounts through ASP.NET Core Identity: its UserManager hashes and verifies passwords,
/// counts failed sign-ins and locks the account. This class only decides in which order to ask.</summary>
internal sealed class IdentityUserDirectory(
    UserManager<AppUser> users,
    IPasswordHasher<AppUser> hasher,
    AccountsDbContext db,
    TimeProvider clock) : IUserDirectory
{
    public Task<bool> AnyAsync(CancellationToken ct) => db.Users.AnyAsync(ct);

    public async Task<Account?> FindByEmailAsync(string email, CancellationToken ct) =>
        ToAccount(await users.FindByEmailAsync(email));

    public async Task<Account?> FindByExternalIdentityAsync(string issuer, string subject, CancellationToken ct) =>
        ToAccount(await users.FindByLoginAsync(issuer, subject));

    public async Task<Account> CreateAsync(
        string? email, string displayName, string? password, bool isAdmin, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        var user = new AppUser
        {
            Id = id,
            UserName = id.ToString("N"), // Identity wants a unique user name; accounts are known by e-mail
            Email = email,
            DisplayName = displayName,
            IsAdmin = isAdmin,
            CreatedAt = clock.GetUtcNow(),
        };

        IdentityResult result;
        try
        {
            result = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new AccountException(AccountError.AccountExists);
        }

        if (!result.Succeeded)
        {
            var refusedPassword = result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal));
            throw refusedPassword
                ? new AccountException(AccountError.WeakPassword)
                : new InvalidOperationException("the account was refused: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }

        return ToAccount(user)!;
    }

    public async Task<Account?> AuthenticateAsync(string email, string password, CancellationToken ct)
    {
        // The row is locked until the transaction ends: sign-ins to one account are counted one by one,
        // so parallel attempts cannot all be judged against the same failed-attempt count. Locked as an
        // update that leaves the key alone, which is what follows: a row that only refers to the account
        // (a group membership) can still be written meanwhile, where FOR UPDATE would make two sign-ins
        // wait for each other.
        var normalized = users.NormalizeEmail(email);
        var matches = await db.Users
            .FromSql($"SELECT * FROM accounts.users WHERE normalized_email = {normalized} FOR NO KEY UPDATE")
            .ToListAsync(ct);
        var user = matches.FirstOrDefault();

        if (user?.PasswordHash is null)
        {
            hasher.VerifyHashedPassword(user!, Argon2PasswordHasher.DummyHash, password);
            return null;
        }

        // Verified even when the account is locked, so that a locked account is refused no faster than
        // a wrong password.
        var valid = await users.CheckPasswordAsync(user, password);
        if (await users.IsLockedOutAsync(user))
        {
            return null;
        }

        if (!valid)
        {
            await users.AccessFailedAsync(user);
            return null;
        }

        if (user.AccessFailedCount != 0)
        {
            await users.ResetAccessFailedCountAsync(user);
        }

        return ToAccount(user);
    }

    public async Task LinkExternalIdentityAsync(Guid accountId, string issuer, string subject, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(accountId.ToString())
            ?? throw new InvalidOperationException("the account to link no longer exists");
        var result = await users.AddLoginAsync(user, new UserLoginInfo(issuer, subject, issuer));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("the provider could not be linked: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }

    public async Task<Account?> SaveSettingsAsync(Guid accountId, InterfaceSettings settings, CancellationToken ct)
    {
        // One UPDATE of the three columns: Identity's concurrency stamp guards what Identity changes
        // (the password, the lockout), not these, so a sign-in counted at the same time never refuses it.
        var updated = await db.Users
            .Where(u => u.Id == accountId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(u => u.InterfaceLanguage, settings.Language)
                    .SetProperty(u => u.InterfaceTheme, settings.Theme)
                    .SetProperty(u => u.InterfaceCharacter, settings.Character),
                ct);
        if (updated == 0)
        {
            return null;
        }

        return (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == accountId, ct))?.ToAccount();
    }

    private static Account? ToAccount(AppUser? user) => user?.ToAccount();
}
