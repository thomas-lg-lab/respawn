namespace Respawn.Host.Configuration;

/// <summary>The app does not start, and <see cref="SettingsCheck"/> said why in the log: a setting that
/// cannot be read, or a data directory that cannot be written. The only failure the start ends on with
/// a plain exit; any other is left to crash, with all it says.</summary>
internal sealed class StartRefusedException(string message, Exception inner) : Exception(message, inner);
