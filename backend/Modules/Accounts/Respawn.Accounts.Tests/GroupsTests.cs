using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn.Accounts.Infrastructure.Persistence;
using Respawn.Accounts.Ports;
using Respawn.Accounts.Tests.Support;
using Respawn.Testing;

namespace Respawn.Accounts.Tests;

/// <summary>Groups, as stored: the default group every account belongs to. No route reads them yet.</summary>
public sealed class GroupsTests
{
    [Fact]
    public async Task The_first_account_creates_the_default_group_and_owns_it()
    {
        await using var app = await TestApp.StartAsync();
        Assert.Empty(await GroupsAsync(app));

        using var browser = app.Browser();
        var account = (await browser.RegisterAsync().JsonAsync()).GetProperty("id").GetGuid();

        var group = Assert.Single(await GroupsAsync(app));
        Assert.True(group.IsDefault);
        Assert.Equal("Everyone", group.Name);
        Assert.Equal(account, group.OwnerId);
        Assert.Equal([account], await MembersAsync(app, group.Id));
    }

    [Fact]
    public async Task A_new_account_joins_the_default_group()
    {
        await using var app = await TestApp.StartAsync();
        using var first = app.Browser();
        var owner = (await first.RegisterAsync().JsonAsync()).GetProperty("id").GetGuid();
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true')");

        using var second = app.Browser();
        var member = (await second.RegisterAsync("second@example.org", "Second").JsonAsync()).GetProperty("id").GetGuid();

        var group = Assert.Single(await GroupsAsync(app));
        Assert.Equal(owner, group.OwnerId);
        Assert.Equal(new[] { owner, member }.Order(), await MembersAsync(app, group.Id));
    }

    [Fact]
    public async Task An_account_created_from_a_provider_joins_it_too()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var response = await browser.ExchangeAsync(issuer.Token());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = (await response.JsonAsync()).GetProperty("id").GetGuid();

        var group = Assert.Single(await GroupsAsync(app));
        Assert.Equal(account, group.OwnerId);
        Assert.Equal([account], await MembersAsync(app, group.Id));
    }

    [Fact]
    public async Task Signing_in_again_changes_no_membership()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, await browser.LoginAsync("first@example.org", TestApp.Password).StatusAsync());

        var group = Assert.Single(await GroupsAsync(app));
        Assert.Single(await MembersAsync(app, group.Id));
    }

    [Fact]
    public async Task A_registration_that_fails_leaves_no_group_behind()
    {
        // The account, its group and its membership are one transaction.
        await using var app = await TestApp.StartAsync(services: services => services.AddScoped<ISessionStore, FailingSessions>());
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.InternalServerError, await browser.RegisterAsync().StatusAsync());

        Assert.Empty(await GroupsAsync(app));
        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Users.AnyAsync());
    }

    [Fact]
    public async Task The_database_holds_one_default_group_only()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var owner = (await browser.RegisterAsync().JsonAsync()).GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        db.Groups.Add(new UserGroup { Id = Guid.CreateVersion7(), Name = "Another", OwnerId = owner, IsDefault = true, CreatedAt = now });
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(refused.InnerException).SqlState);

        // Any number of other groups, and an account is in a group once.
        db.ChangeTracker.Clear();
        var other = new UserGroup { Id = Guid.CreateVersion7(), Name = "Another", OwnerId = owner, CreatedAt = now };
        db.Groups.Add(other);
        db.GroupMemberships.Add(new GroupMembership { GroupId = other.Id, UserId = owner, JoinedAt = now });
        await db.SaveChangesAsync();
        Assert.Equal(2, (await GroupsAsync(app)).Count);

        var twice = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO accounts.group_memberships (group_id, user_id, joined_at) VALUES ({other.Id}, {owner}, now())"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, twice.SqlState);
    }

    [Fact]
    public async Task The_migration_puts_the_existing_accounts_in_a_default_group()
    {
        // A database as it was before groups: migrated up to the first migration, with two accounts.
        TestDatabase.EnsureCreated();
        var name = TestDatabase.Name[..^"_test".Length] + "_upgrade_test";
        var url = TestDatabase.UrlFor(name);
        TestDatabase.Admin($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        TestDatabase.Create(url);

        await using var dataSource = TestDatabase.DataSource(url);
        var options = new DbContextOptionsBuilder<AccountsDbContext>();
        AccountsDbContext.Configure(options, dataSource);
        await using var db = new AccountsDbContext(options.Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("Accounts");

        var member = Guid.CreateVersion7();
        var administrator = Guid.CreateVersion7();
        foreach (var (id, isAdmin, daysAgo) in new[] { (member, false, 2), (administrator, true, 1) })
        {
            db.Users.Add(new AppUser
            {
                Id = id,
                UserName = id.ToString("N"),
                NormalizedUserName = id.ToString("N").ToUpperInvariant(),
                DisplayName = "Before groups",
                IsAdmin = isAdmin,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
            });
        }

        await db.SaveChangesAsync();

        await migrator.MigrateAsync();

        var group = await db.Groups.SingleAsync();
        Assert.True(group.IsDefault);
        Assert.Equal(administrator, group.OwnerId); // the administrator, not the oldest account
        Assert.Equal(
            new[] { member, administrator }.Order(),
            await db.GroupMemberships.Where(m => m.GroupId == group.Id).Select(m => m.UserId).OrderBy(id => id).ToListAsync());
    }

    private static async Task<List<UserGroup>> GroupsAsync(TestApp app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Groups.AsNoTracking().ToListAsync();
    }

    private static async Task<List<Guid>> MembersAsync(TestApp app, Guid group)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().GroupMemberships
            .Where(m => m.GroupId == group).Select(m => m.UserId).OrderBy(id => id).ToListAsync();
    }

    /// <summary>The last step of a registration, failing.</summary>
    private sealed class FailingSessions : ISessionStore
    {
        public Task<string> OpenAsync(Guid accountId, DateTimeOffset expiresAt, CancellationToken ct) =>
            throw new InvalidOperationException("no session can be opened");

        public Task<Account?> FindAccountAsync(string token, CancellationToken ct) => Task.FromResult<Account?>(null);

        public Task CloseAsync(string token, CancellationToken ct) => Task.CompletedTask;
    }
}
