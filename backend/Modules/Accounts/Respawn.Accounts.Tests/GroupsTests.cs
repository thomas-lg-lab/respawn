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
        TestDatabase.OpenRegistration();

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
        Assert.Equal("/", (await browser.SignInAsync(issuer)).Landing());
        var account = (await browser.GetAsync("/api/v1/me").JsonAsync()).GetProperty("id").GetGuid();

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
    public async Task An_account_created_without_a_group_joins_at_the_next_sign_in()
    {
        // An instance still running the version before groups creates an account after the migration:
        // nothing put it in the default group.
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var owner = (await browser.RegisterAsync().JsonAsync()).GetProperty("id").GetGuid();
        var stranger = Guid.CreateVersion7();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            db.Users.Add(new AppUser
            {
                Id = stranger,
                UserName = stranger.ToString("N"),
                NormalizedUserName = stranger.ToString("N").ToUpperInvariant(),
                DisplayName = "Created elsewhere",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Anyone signing in is enough: the rule is about every account, not the one signing in.
        Assert.Equal(HttpStatusCode.OK, await browser.LoginAsync("first@example.org", TestApp.Password).StatusAsync());

        var group = Assert.Single(await GroupsAsync(app));
        Assert.Equal(owner, group.OwnerId);
        Assert.Equal(new[] { owner, stranger }.Order(), await MembersAsync(app, group.Id));
    }

    [Fact]
    public async Task A_missing_default_group_comes_back_to_the_administrator_with_everyone_in_it()
    {
        await using var app = await TestApp.StartAsync();
        using var first = app.Browser();
        var administrator = (await first.RegisterAsync().JsonAsync()).GetProperty("id").GetGuid();
        TestDatabase.OpenRegistration();
        using var second = app.Browser();
        var member = (await second.RegisterAsync("second@example.org", "Second").JsonAsync()).GetProperty("id").GetGuid();

        // The default group is gone while accounts exist; its memberships went with it.
        TestDatabase.Run("DELETE FROM accounts.groups");

        using var third = app.Browser();
        var newcomer = (await third.RegisterAsync("third@example.org", "Third").JsonAsync()).GetProperty("id").GetGuid();

        var group = Assert.Single(await GroupsAsync(app));
        Assert.True(group.IsDefault);
        Assert.Equal(administrator, group.OwnerId); // not the account that happened to register next
        Assert.Equal(new[] { administrator, member, newcomer }.Order(), await MembersAsync(app, group.Id));
    }

    [Fact]
    public async Task Sign_ins_at_the_same_time_restore_one_default_group()
    {
        await using var app = await TestApp.StartAsync();
        using var first = app.Browser();
        await first.RegisterAsync();
        TestDatabase.OpenRegistration();
        string[] emails = ["first@example.org", .. Enumerable.Range(2, 5).Select(number => $"player{number}@example.org")];
        foreach (var email in emails.Skip(1))
        {
            using var browser = app.Browser();
            Assert.Equal(HttpStatusCode.Created, await browser.RegisterAsync(email, "Player").StatusAsync());
        }

        TestDatabase.Run("DELETE FROM accounts.groups");

        // Each sign-in finds the group missing; the database lets one create it and the others wait.
        var codes = await Task.WhenAll(emails.Select(async email =>
        {
            using var browser = app.Browser();
            return await browser.LoginAsync(email, TestApp.Password).StatusAsync();
        }));

        Assert.All(codes, code => Assert.Equal(HttpStatusCode.OK, code));
        var group = Assert.Single(await GroupsAsync(app));
        Assert.Equal(emails.Length, (await MembersAsync(app, group.Id)).Count);
    }

    [Fact]
    public async Task A_registration_that_fails_leaves_no_group_behind()
    {
        // The account, its group and its membership are one transaction.
        await using var app = await TestApp.StartAsync(services: services => services.AddScoped<ISessionStore, FailingSessions>());
        using var browser = app.Browser();
        Assert.Equal((HttpStatusCode.InternalServerError, "internal_error"), await browser.RegisterAsync().RefusalAsync());

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
    public async Task A_group_keeps_its_owner_and_a_deleted_account_leaves_its_groups()
    {
        await using var app = await TestApp.StartAsync();
        using var first = app.Browser();
        var owner = (await first.RegisterAsync().JsonAsync()).GetProperty("id").GetGuid();
        TestDatabase.OpenRegistration();
        using var second = app.Browser();
        var member = (await second.RegisterAsync("second@example.org", "Second").JsonAsync()).GetProperty("id").GetGuid();
        var group = Assert.Single(await GroupsAsync(app));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        // A group does not vanish with its owner's account: the database refuses, until it is handed over.
        var refused = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"DELETE FROM accounts.users WHERE id = {owner}"));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, refused.SqlState);

        // Any other account goes, and its memberships with it.
        await db.Database.ExecuteSqlAsync($"DELETE FROM accounts.users WHERE id = {member}");
        Assert.Equal([owner], await MembersAsync(app, group.Id));
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

        // Written as that schema knows them, not through today's model: a column added to the accounts
        // later must not reach back into this test.
        var member = Guid.CreateVersion7();
        var administrator = Guid.CreateVersion7();
        foreach (var (id, isAdmin, daysAgo) in new[] { (member, false, 2), (administrator, true, 1) })
        {
            var createdAt = DateTimeOffset.UtcNow.AddDays(-daysAgo);
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO accounts.users
                    (id, display_name, is_admin, created_at, email_confirmed, phone_number_confirmed,
                     two_factor_enabled, lockout_enabled, access_failed_count)
                VALUES ({id}, 'Before groups', {isAdmin}, {createdAt}, FALSE, FALSE, FALSE, TRUE, 0)
                """);
        }

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
