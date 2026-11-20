namespace Respawn.SharedKernel;

/// <summary>
/// Marks a route the browser navigates to, on its way through another site or back from it: a refusal
/// the host makes before the route runs (too many requests) is not answered as an error, which the
/// visitor would see as a page of JSON, but sends the browser back to the app with the outcome
/// <see cref="Navigation.Failed"/> under <paramref name="outcomeParameter"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NavigationAttribute(string outcomeParameter) : Attribute
{
    public string OutcomeParameter { get; } = outcomeParameter;
}
