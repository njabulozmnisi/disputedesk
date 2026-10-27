using System.Reflection;
using DisputeDesk.Application.Abstractions;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// The Clean Architecture layer rules, read from compiled assembly metadata. A violation fails the
/// test run, and CI with it.
/// </summary>
public class LayerTests
{
    private static readonly Assembly DomainAssembly = Assembly.Load("DisputeDesk.Domain");
    private static readonly Assembly ApplicationAssembly = Assembly.Load("DisputeDesk.Application");
    private static readonly Assembly InfrastructureAssembly = Assembly.Load("DisputeDesk.Infrastructure");
    private static readonly Assembly ContractsAssembly = Assembly.Load("DisputeDesk.Contracts");
    private static readonly Assembly ApiAssembly = Assembly.Load("DisputeDesk.Api");
    private static readonly Assembly WorkerAssembly = Assembly.Load("DisputeDesk.Worker");

    private static readonly Assembly CoreBankingStubAssembly = Assembly.Load("DisputeDesk.CoreBankingStub");

    private static readonly Assembly[] SolutionAssemblies =
        [DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ContractsAssembly, ApiAssembly, WorkerAssembly];

    public static TheoryData<Type> ExternalSystemPorts => [typeof(ICoreBankingClient), typeof(ISimSwapCheck), typeof(ISmsSender)];

    [Fact]
    public void DomainAssembly_References_NoOtherSolutionAssembly()
    {
        IEnumerable<string> forbidden = SolutionAssemblies.Where(assembly => assembly != DomainAssembly).Select(NameOf);

        AssertReferencesNone(DomainAssembly, forbidden);
    }

    [Fact]
    public void ApplicationAssembly_References_NoInfrastructureHostsOrNpgsql()
    {
        AssertReferencesNone(ApplicationAssembly, [NameOf(InfrastructureAssembly), NameOf(ApiAssembly), NameOf(WorkerAssembly)]);

        IEnumerable<string> npgsql = ReferencedNames(ApplicationAssembly).Where(name => name.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.Empty(npgsql);
    }

    [Fact]
    public void IAppDbContext_Implementations_AreOnlyInInfrastructure()
    {
        var implementations = SolutionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(IAppDbContext)))
            .ToList();

        Assert.NotEmpty(implementations);
        Assert.All(implementations, type => Assert.True(
            type.Assembly == InfrastructureAssembly,
            $"{type.FullName} implements {nameof(IAppDbContext)} outside Infrastructure."));
    }

    [Theory]
    [MemberData(nameof(ExternalSystemPorts))]
    public void ExternalSystemPort_IsDeclaredInApplication_AndImplementedOnlyInInfrastructure(Type port)
    {
        Assert.Equal(ApplicationAssembly, port.Assembly);

        var implementations = SolutionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(port))
            .ToList();

        Assert.NotEmpty(implementations);
        Assert.All(implementations, type => Assert.True(
            type.Assembly == InfrastructureAssembly,
            $"{type.FullName} implements {port.Name} outside Infrastructure."));
    }

    [Fact]
    public void CoreBankingStubAssembly_References_NoSolutionAssembly()
    {
        AssertReferencesNone(CoreBankingStubAssembly, SolutionAssemblies.Select(NameOf));
    }

    [Fact]
    public void SolutionAssemblies_WithEntryPoint_AreOnlyApiAndWorker()
    {
        IOrderedEnumerable<string> hosts = SolutionAssemblies
            .Where(assembly => assembly.EntryPoint is not null)
            .Select(NameOf)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["DisputeDesk.Api", "DisputeDesk.Worker"], hosts);
    }

    private static string NameOf(Assembly assembly) => assembly.GetName().Name ?? assembly.FullName ?? string.Empty;

    private static IEnumerable<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty);

    private static void AssertReferencesNone(Assembly assembly, IEnumerable<string> forbidden)
    {
        var violations = ReferencedNames(assembly).Intersect(forbidden, StringComparer.Ordinal).ToList();

        Assert.True(violations.Count == 0, $"{NameOf(assembly)} references {string.Join(", ", violations)}.");
    }
}
