namespace Respawn.Games.Tests.Support;

/// <summary>The app's clock, set by a test: a day of the sync passes when the test says so. It starts
/// tomorrow, in the middle of a day of the sync, so that nothing the app dates is already in the past
/// for the test's browser.</summary>
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = SyncSchedule.DayOf(DateTimeOffset.UtcNow).AddDays(1).AddHours(6);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;

    /// <summary>Moves to the same hour of the next day.</summary>
    public void NextDay() => Advance(TimeSpan.FromDays(1));
}
