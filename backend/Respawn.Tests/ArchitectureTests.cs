using System.Reflection;
using NetArchTest.Rules;
using Respawn.Accounts.Api;
using Respawn.Accounts.Infrastructure;
using Respawn.Accounts.Services;
using Respawn.SharedKernel;

namespace Respawn.Tests;

/// <summary>
/// The dependency rules of the solution, read from the compiled assemblies.
///
/// The app is split by responsibility: each module is hexagonal (a core with its ports, outbound
/// adapters, an inbound adapter), modules only meet through the shared kernel, and the host is the one
/// project that knows them all. A new module adds its assemblies to the lists below.
/// </summary>
public sealed class ArchitectureTests
{
    private const string Host = "Respawn.Host";

    /// <summary>What a core must not know about: frameworks and drivers.</summary>
    private static readonly string[] Technical =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions",
        "Microsoft.IdentityModel",
        "Npgsql",
        "Sodium",
    ];

    private static readonly Assembly Core = typeof(AccountService).Assembly;
    private static readonly Assembly Outbound = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Inbound = typeof(AccountsApi).Assembly;
    private static readonly Assembly Kernel = typeof(ErrorResponse).Assembly;

    [Fact]
    public void A_core_knows_no_framework_and_none_of_its_adapters() =>
        Allowed(Core, [.. Technical, "Respawn.Accounts.Infrastructure", "Respawn.Accounts.Api", Host]);

    [Fact]
    public void An_inbound_adapter_knows_the_core_and_not_how_it_is_stored() =>
        Allowed(Inbound, ["Respawn.Accounts.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "Sodium", Host]);

    [Fact]
    public void An_outbound_adapter_knows_the_core_and_not_how_it_is_served() =>
        Allowed(Outbound, ["Respawn.Accounts.Api", "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.Http", Host]);

    [Fact]
    public void The_shared_kernel_knows_no_module_and_no_framework() =>
        Allowed(Kernel, [.. Technical, "Respawn.Accounts", Host]);

    [Fact]
    public void The_projects_reference_each_other_as_the_rules_say()
    {
        // The same rules on the references themselves, so that an unused one is caught too.
        Assert.Empty(References(Kernel));
        Assert.Empty(References(Core));
        Assert.Equal(["Respawn.Accounts", "Respawn.SharedKernel"], References(Inbound));
        Assert.Equal(["Respawn.Accounts", "Respawn.SharedKernel"], References(Outbound));
    }

    private static void Allowed(Assembly assembly, string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        Assert.True(
            result.IsSuccessful,
            $"{assembly.GetName().Name} must not depend on these: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    private static string[] References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Respawn.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
}
