using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Testing;

namespace Respawn.Games.Tests.Support;

/// <summary>What the tests of the module set up the same way: an owner, a linked account, a use case
/// called in a scope of its own, a turn of a schedule run by hand, and the values they share. A test
/// class brings them in with <c>using static</c>; one that needs its own keeps a method of the same
/// name, which then wins.</summary>
internal static class Fixtures
{
    /// <summary>A SteamID in the shape of Steam's. Nobody's.</summary>
    public const string SteamId = "76561190000000001";

    /// <summary>The recorded cover, as Steam's image host answers it.</summary>
    public static readonly byte[] Jpeg = FakeSteam.RecordedBytes("cover.jpg");

    /// <summary>The start of an image's SHA-256, as a kept file's name holds it.</summary>
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];

    /// <summary>A new account of the instance, signed in with this browser. Returns its id.</summary>
    public static async Task<Guid> OwnerAsync(HttpClient browser, string email = "first@example.org") =>
        (await browser.RegisterAsync(email).JsonAsync()).GetProperty("id").GetGuid();

    /// <summary>The account of the instance a linked account belongs to.</summary>
    public static async Task<Guid> OwnerAsync(TestApp app, Guid linkedAccount)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        return (await db.LinkedAccounts.AsNoTracking().SingleAsync(a => a.Id == linkedAccount)).OwnerId;
    }

    /// <summary>A Steam account linked to an owner.</summary>
    public static Task<LinkedAccount> LinkAsync(TestApp app, Guid owner, string externalId, string? displayName = null) =>
        UseAsync(app, library => library.LinkAccountAsync(owner, "steam", externalId, displayName, default));

    /// <summary>An account of the instance, signed in, with a Steam account linked to it. Returns the
    /// linked account.</summary>
    public static async Task<Guid> LinkAsync(
        TestApp app, HttpClient browser, string steamId = SteamId, string email = "first@example.org", string source = "steam")
    {
        var owner = await OwnerAsync(browser, email);
        return (await UseAsync(app, library => library.LinkAccountAsync(owner, source, steamId, null, default))).Id;
    }

    /// <summary>A use case, called as a connector or a scheduler will: in a scope of its own.</summary>
    public static async Task<T> UseAsync<T>(TestApp app, Func<ILibraryService, Task<T>> use)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<ILibraryService>());
    }

    /// <summary>One turn of a schedule, as the app's timer runs it. Returns how many of its turns ended:
    /// what that counts is each schedule's.</summary>
    public static Task<int> TurnAsync<TScheduler>(TestApp app)
        where TScheduler : IHostedService =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<TScheduler>()) switch
        {
            DueScheduler<Guid> byAccount => byAccount.RunDueAsync(default),
            DueScheduler<string> byStore => byStore.RunDueAsync(default),
            var other => throw new InvalidOperationException($"{other.GetType().Name} is not a schedule."),
        };
}
