using System.Reflection;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure;
using RiskGuard.Modules.Action;
using RiskGuard.Modules.Ingestion;
using RiskGuard.Modules.Risk;
using RiskGuard.Modules.Tenant;

namespace RiskGuard.UnitTests.Architecture;

/// <summary>Garante em CI as regras de dependencia do Monolito Modular / Clean Architecture.</summary>
public class ModuleBoundariesTests
{
    private const string Prefix = "RiskGuard.";

    public static TheoryData<Assembly> ModuleAssemblies => new()
    {
        typeof(TenantModule).Assembly,
        typeof(IngestionModule).Assembly,
        typeof(RiskModule).Assembly,
        typeof(ActionModule).Assembly,
    };

    [Fact]
    public void Core_must_not_depend_on_any_other_project()
    {
        ProjectReferencesOf(typeof(IModule).Assembly).Should().BeEmpty();
    }

    [Fact]
    public void Infrastructure_may_only_depend_on_Core()
    {
        ProjectReferencesOf(typeof(DependencyInjection).Assembly)
            .Should().BeEquivalentTo(["RiskGuard.Core"]);
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Modules_may_only_depend_on_Core_and_Infrastructure(Assembly module)
    {
        // Infrastructure: apenas a base de persistencia (ModuleDbContext). Nunca outro modulo nem a Api.
        ProjectReferencesOf(module).Should().BeSubsetOf(["RiskGuard.Core", "RiskGuard.Infrastructure"]);
    }

    [Fact]
    public void Catalog_exposes_the_four_modules_from_the_SDD()
    {
        ModuleCatalog().Select(m => m.Name)
            .Should().BeEquivalentTo("Tenant", "Ingestion", "Risk", "Action");
    }

    private static IReadOnlyList<IModule> ModuleCatalog() =>
    [
        new TenantModule(),
        new IngestionModule(),
        new RiskModule(),
        new ActionModule(),
    ];

    private static IEnumerable<string> ProjectReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal));
}
