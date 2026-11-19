using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using Respawn.Accounts;
using Respawn.SharedKernel;

namespace Respawn.Accounts.Infrastructure.Persistence;

/// <summary>The tables of the Accounts module, in its own schema: accounts and what signing in needs.</summary>
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : IdentityUserContext<AppUser, Guid>(options)
{
    /// <summary>The PostgreSQL schema this module owns. No other module reads or writes it.</summary>
    public const string Schema = "accounts";

    public DbSet<UserGroup> Groups => Set<UserGroup>();

    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<ExchangedToken> ExchangedTokens => Set<ExchangedToken>();

    public DbSet<Setting> Settings => Set<Setting>();

    /// <summary>The one place that says how the context talks to PostgreSQL, shared by the app, the
    /// migration at first connection and the design-time tools.</summary>
    public static void Configure(DbContextOptionsBuilder options, DbDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql
                // The module's migration history lives with its tables, so each module migrates alone.
                .MigrationsHistoryTable("__EFMigrationsHistory", Schema)
                // A pooled connection the server closed meanwhile (a restart, a rotated password) is
                // only found broken when used: the query is run again on a new one. Short and few, so
                // a database that is really gone still answers 503 quickly.
                .EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(200), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<AppUser>(user =>
        {
            user.ToTable("users", table =>
            {
                // The settings are chosen together: all three or none.
                table.HasCheckConstraint(
                    "ck_users_interface_settings_together",
                    "(interface_language IS NULL) = (interface_theme IS NULL) AND (interface_theme IS NULL) = (interface_character IS NULL)");
                // Each one is one of its words: the ones the converters below write.
                table.HasCheckConstraint(
                    "ck_users_interface_language",
                    OneOf("interface_language", Enum.GetValues<Language>().Select(v => SettingWords.Of(v))));
                table.HasCheckConstraint(
                    "ck_users_interface_theme",
                    OneOf("interface_theme", Enum.GetValues<Theme>().Select(v => SettingWords.Of(v))));
                table.HasCheckConstraint(
                    "ck_users_interface_character",
                    OneOf("interface_character", Enum.GetValues<Character>().Select(v => SettingWords.Of(v))));
            });
            user.Property(u => u.DisplayName).HasMaxLength(100);
            // Stored as the words the API uses, so that a row reads as what the account chose.
            user.Property(u => u.InterfaceLanguage).HasMaxLength(8).HasConversion(
                new ValueConverter<Language, string>(v => SettingWords.Of(v), v => SettingWords.ToLanguage(v)));
            user.Property(u => u.InterfaceTheme).HasMaxLength(8).HasConversion(
                new ValueConverter<Theme, string>(v => SettingWords.Of(v), v => SettingWords.ToTheme(v)));
            user.Property(u => u.InterfaceCharacter).HasMaxLength(8).HasConversion(
                new ValueConverter<Character, string>(v => SettingWords.Of(v), v => SettingWords.ToCharacter(v)));
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.NormalizedEmail).HasMaxLength(320);
            // One account per e-mail; accounts without one are not concerned (NULL is never equal to NULL).
            user.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        // An external identity is its issuer and subject: Identity's "login", under the name the rest
        // of the app uses. An issuer is a URL and a subject may be 255 characters long, both more than
        // Identity's default of 128.
        builder.Entity<IdentityUserLogin<Guid>>(login =>
        {
            login.ToTable("external_identities");
            login.Property(l => l.LoginProvider).HasMaxLength(512);
            login.Property(l => l.ProviderKey).HasMaxLength(255);
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<UserGroup>(group =>
        {
            group.ToTable("groups");
            group.Property(g => g.Name).HasMaxLength(100);
            // One default group at most: only the rows that are the default one are in the index.
            group.HasIndex(g => g.IsDefault).IsUnique().HasFilter("is_default");
            // A group does not vanish with its owner's account: it is handed over or deleted first.
            group.HasOne<AppUser>().WithMany().HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<GroupMembership>(membership =>
        {
            membership.ToTable("group_memberships");
            membership.HasKey(m => new { m.GroupId, m.UserId });
            membership.HasOne<UserGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
            membership.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Session>(session =>
        {
            session.ToTable("sessions");
            session.Property(s => s.TokenHash).HasMaxLength(64);
            session.HasIndex(s => s.TokenHash).IsUnique();
            session.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ExchangedToken>(token =>
        {
            token.ToTable("exchanged_tokens");
            token.HasKey(t => t.TokenHash);
            token.Property(t => t.TokenHash).HasMaxLength(64);
        });

        builder.Entity<Setting>(setting =>
        {
            setting.ToTable("settings");
            setting.HasKey(s => s.Key);
            setting.Property(s => s.Key).HasMaxLength(64);
        });
    }

    /// <summary>The SQL of a check that a column holds one of these words (or nothing).</summary>
    private static string OneOf(string column, IEnumerable<string> words) =>
        $"{column} IN ({string.Join(", ", words.Select(word => $"'{word}'"))})";
}

internal sealed class AccountsSchemaMigrator : ISchemaMigrator
{
    public void Migrate(DbDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>();
        AccountsDbContext.Configure(options, dataSource);
        using var context = new AccountsDbContext(options.Options);
        context.Database.Migrate();
    }
}

/// <summary>For `dotnet ef migrations add` only: the tools need a context without starting the app, and
/// never connect with it.</summary>
internal sealed class AccountsDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>();
        AccountsDbContext.Configure(options, NpgsqlDataSource.Create("Host=localhost;Database=respawn"));
        return new AccountsDbContext(options.Options);
    }
}
