using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Accounts.Infrastructure.Identity;
using Respawn.Accounts.Infrastructure.Oidc;
using Respawn.Accounts.Infrastructure.Persistence;
using Respawn.Accounts.Ports;
using Respawn.SharedKernel;

namespace Respawn.Accounts.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The adapters behind the ports of the Accounts module. The host registers
    /// <see cref="AccountOptions"/>, a <see cref="TimeProvider"/> and the <see cref="IConnectionSource"/>.</summary>
    public static IServiceCollection AddAccountsInfrastructure(this IServiceCollection services)
    {
        // The module's schema. Nothing connects here: the context's options are built when a request
        // first needs the context, which is when the host opens the connection and migrates.
        services.AddSingleton<ISchemaMigrator, AccountsSchemaMigrator>();
        services.AddDbContext<AccountsDbContext>((provider, options) =>
            AccountsDbContext.Configure(options, provider.GetRequiredService<IConnectionSource>().DataSource()));

        // Accounts: ASP.NET Core Identity, with Argon2id in place of its default password hasher.
        services.AddScoped<IPasswordHasher<AppUser>, Argon2PasswordHasher>();
        services.AddIdentityCore<AppUser>().AddEntityFrameworkStores<AccountsDbContext>();
        services.AddOptions<IdentityOptions>().Configure<AccountOptions>((identity, accounts) =>
        {
            // Length is the one password rule; composition rules only make passwords harder to remember.
            identity.Password.RequiredLength = accounts.PasswordMinLength;
            identity.Password.RequiredUniqueChars = 1;
            identity.Password.RequireDigit = false;
            identity.Password.RequireLowercase = false;
            identity.Password.RequireUppercase = false;
            identity.Password.RequireNonAlphanumeric = false;
            identity.Lockout.AllowedForNewUsers = true;
            identity.Lockout.MaxFailedAccessAttempts = accounts.LockoutThreshold;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(accounts.LockoutMinutes);
            // Uniqueness is the database's: an account from a provider that sends no e-mail has none.
            identity.User.RequireUniqueEmail = false;
        });

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IUserDirectory, IdentityUserDirectory>();
        services.AddScoped<IGroupDirectory, EfGroupDirectory>();
        services.AddScoped<IInstanceSettings, EfInstanceSettings>();
        services.AddScoped<ISessionStore, EfSessionStore>();
        services.AddScoped<IExchangedTokens, EfExchangedTokens>();

        // Sign-in through a provider.
        services.AddSingleton<OidcValidationSettings>();
        services.AddHttpClient(OidcTokenValidator.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddSingleton<IExternalTokenValidator, OidcTokenValidator>();

        return services;
    }
}
