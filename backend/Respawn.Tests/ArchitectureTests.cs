using System.Reflection;
using NetArchTest.Rules;
using Npgsql;
using Respawn.Persistence;
using Respawn.SharedKernel;
using Respawn.Testing;

namespace Respawn.Tests;

/// <summary>
/// The dependency rules of the solution, read from the compiled assemblies.
///
/// The app is split by responsibility: each module is hexagonal (a core with its ports, outbound
/// adapters, an inbound adapter), modules only meet through the shared kernel, and the host is the one
/// project that knows them all. A new module adds one line to <see cref="Modules"/>.
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

    private static readonly AppModule[] Modules =
    [
        new(
            "Respawn.Accounts",
            "accounts",
            typeof(Accounts.Services.AccountService).Assembly,
            typeof(Accounts.Infrastructure.DependencyInjection).Assembly,
            typeof(Accounts.Api.AccountsApi).Assembly,
            []),
        new(
            "Respawn.Games",
            "games",
            typeof(Games.Services.LibraryService).Assembly,
            typeof(Games.Infrastructure.DependencyInjection).Assembly,
            typeof(Games.Api.LibraryController).Assembly,
            [typeof(Games.Steam.SteamOptions).Assembly]),
    ];

    private static readonly Assembly Kernel = typeof(ErrorResponse).Assembly;
    private static readonly Assembly Persistence = typeof(DatabaseOptions).Assembly;
    private static readonly Assembly Composition = typeof(Program).Assembly;

    private static string[] ModuleNames => [.. Modules.Select(module => module.Name)];

    [Fact]
    public void A_core_knows_no_framework_and_none_of_its_adapters()
    {
        foreach (var module in Modules)
        {
            Allowed(module.Core, [.. Technical, module.Name + ".Infrastructure", module.Name + ".Api", .. module.ConnectorNames, Host]);
        }
    }

    [Fact]
    public void An_inbound_adapter_knows_the_core_and_not_how_it_is_stored()
    {
        foreach (var module in Modules)
        {
            Allowed(module.Inbound, [module.Name + ".Infrastructure", .. module.ConnectorNames, "Microsoft.EntityFrameworkCore", "Npgsql", "Sodium", Host]);
        }
    }

    [Fact]
    public void An_outbound_adapter_knows_the_core_and_not_how_it_is_served()
    {
        foreach (var module in Modules)
        {
            Allowed(module.Outbound, [module.Name + ".Api", .. module.ConnectorNames, "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.Http", Host]);
        }
    }

    [Fact]
    public void A_connector_knows_the_core_and_nothing_else_of_the_app()
    {
        // An adapter to an outside source, in a project of its own: it could leave the repository
        // with the core's ports, so it knows neither the database nor how the app is served.
        foreach (var module in Modules)
        {
            foreach (var connector in module.Connectors)
            {
                string[] siblings = [.. module.ConnectorNames.Where(name => name != connector.GetName().Name)];
                Allowed(
                    connector,
                    [module.Name + ".Infrastructure", module.Name + ".Api", .. siblings, "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", Host]);
            }
        }
    }

    [Fact]
    public void A_module_knows_no_other_module()
    {
        // Not even another module's core: the day one module needs another, it is through a contract
        // made for that, and this rule names it.
        foreach (var module in Modules)
        {
            string[] others = [.. ModuleNames.Where(name => name != module.Name)];
            foreach (var assembly in module.Assemblies)
            {
                Allowed(assembly, others);
            }
        }
    }

    [Fact]
    public void The_shared_kernel_knows_no_module_and_no_framework() =>
        Allowed(Kernel, [.. Technical, .. ModuleNames, Host]);

    [Fact]
    public void The_host_composes_and_holds_no_database_driver() =>
        Allowed(Composition, ["Npgsql", "Microsoft.EntityFrameworkCore", "Sodium", "Microsoft.IdentityModel"]);

    [Fact]
    public void The_database_connection_knows_no_module_and_not_how_it_is_served() =>
        Allowed(Persistence, [.. ModuleNames, "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", Host]);

    [Fact]
    public void The_projects_reference_each_other_as_the_rules_say()
    {
        // The same rules on the references themselves, so that an unused one is caught too.
        Assert.Empty(References(Kernel));
        Assert.Equal(["Respawn.SharedKernel"], References(Persistence));
        foreach (var module in Modules)
        {
            Assert.Empty(References(module.Core));
            Assert.Equal([module.Name, "Respawn.SharedKernel"], References(module.Inbound));
            Assert.Equal([module.Name, "Respawn.SharedKernel"], References(module.Outbound));
            Assert.All(module.Connectors, connector => Assert.Equal([module.Name], References(connector)));
        }
    }

    [Fact]
    public async Task A_module_keeps_its_tables_in_its_own_schema()
    {
        // Read from the database the migrations made: every module has its schema, with its own
        // migration history, and no table points into another module's.
        await using var app = await TestApp.StartAsync();
        using var connection = TestDatabase.Open(TestDatabase.Url);

        var schemas = Rows(connection, "SELECT DISTINCT table_schema FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema', 'public')");
        Assert.Equal(Modules.Select(module => module.Schema).Order(), schemas.Order());
        Assert.Equal(
            Modules.Select(module => module.Schema).Order(),
            Rows(connection, "SELECT table_schema FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'").Order());
        Assert.Empty(Rows(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'"));

        Assert.Empty(Rows(
            connection,
            """
            SELECT c.conname
            FROM pg_constraint c
            JOIN pg_class here ON here.oid = c.conrelid
            JOIN pg_class there ON there.oid = c.confrelid
            WHERE c.contype = 'f' AND here.relnamespace <> there.relnamespace
            """));
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

    private static List<string> Rows(NpgsqlConnection connection, string query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    /// <summary>A module: its name, which its assemblies' names start with, its PostgreSQL schema, its
    /// three assemblies, and its connectors to outside sources.</summary>
    private sealed record AppModule(
        string Name, string Schema, Assembly Core, Assembly Outbound, Assembly Inbound, Assembly[] Connectors)
    {
        public Assembly[] Assemblies => [Core, Outbound, Inbound, .. Connectors];

        public string[] ConnectorNames => [.. Connectors.Select(connector => connector.GetName().Name!)];
    }
}
