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

    /// <summary>Contracts uses the Domain enums that are identical on the wire (AD-1), and no other solution assembly.</summary>
    [Fact]
    public void ContractsAssembly_References_OnlyDomainOfTheSolution()
    {
        IEnumerable<string> solutionNames = SolutionAssemblies.Select(NameOf);

        IEnumerable<string> referenced = ReferencedNames(ContractsAssembly).Intersect(solutionNames, StringComparer.Ordinal);

        Assert.Equal([NameOf(DomainAssembly)], referenced);
    }

    /// <summary>
    /// The enums identical on the wire exist once, in Domain (AD-1). Contracts keeps only the two customer-view enums,
    /// whose members differ from Domain's, and the four enums with no Domain twin.
    /// </summary>
    [Fact]
    public void ContractsAssembly_Enums_AreOnlyThoseWithNoIdenticalDomainTwin()
    {
        IEnumerable<string> enums = ContractsAssembly.GetTypes().Where(type => type.IsEnum).Select(type => type.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["AuditAction", "AuditResult", "CustomerStatus", "SessionState", "StoredEventType", "TimelineActor"], enums);
    }

    /// <summary>The assembly reference allows any Domain type, so this holds Contracts to the enums (AD-1).</summary>
    [Fact]
    public void ContractsAssembly_PublicSignatures_NameOnlyEnumsFromDomain()
    {
        List<Type> domainTypes = [.. ContractsAssembly.GetExportedTypes()
            .SelectMany(SignatureTypes)
            .SelectMany(WithItsParts)
            .Where(type => type.Assembly == DomainAssembly)
            .Distinct()];

        Assert.NotEmpty(domainTypes);
        Assert.All(domainTypes, type => Assert.True(type.IsEnum, $"{type.FullName} is in a Contracts signature and is not an enum."));
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

    /// <summary>
    /// No type shares its name with a solution namespace (.NET Framework Design Guidelines, "Names of Namespaces"):
    /// inside any <c>DisputeDesk.*</c> namespace the name would resolve to the namespace first (CS0234).
    /// </summary>
    [Fact]
    public void SolutionTypes_Name_IsNoSolutionNamespaceSegment()
    {
        Type[] types = [.. SolutionAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => !type.IsNested)];
        var segments = types
            .SelectMany(type => (type.Namespace ?? "").Split('.'))
            .ToHashSet(StringComparer.Ordinal);

        var violations = types
            .Where(type => segments.Contains(type.Name))
            .Select(type => type.FullName)
            .ToList();

        Assert.True(violations.Count == 0, "Types named like a namespace:\n" + string.Join('\n', violations));
    }

    /// <summary>The type of every public field and property, every constructor and method parameter, and every method's return.</summary>
    private static IEnumerable<Type> SignatureTypes(Type type) =>
        type.GetFields().Select(field => field.FieldType)
            .Concat(type.GetProperties().Select(property => property.PropertyType))
            .Concat(type.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType))
            .Concat(type.GetMethods().SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)));

    /// <summary>
    /// <paramref name="type"/> and its generic arguments, at any depth. An array or an <c>out</c> parameter, such as a
    /// record's <c>Deconstruct</c> has, stands for its element.
    /// </summary>
    private static IEnumerable<Type> WithItsParts(Type type) =>
        type.GetElementType() is { } element
            ? WithItsParts(element)
            : type.GetGenericArguments().SelectMany(WithItsParts).Prepend(type);

    private static string NameOf(Assembly assembly) => assembly.GetName().Name ?? assembly.FullName ?? string.Empty;

    private static IEnumerable<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty);

    private static void AssertReferencesNone(Assembly assembly, IEnumerable<string> forbidden)
    {
        var violations = ReferencedNames(assembly).Intersect(forbidden, StringComparer.Ordinal).ToList();

        Assert.True(violations.Count == 0, $"{NameOf(assembly)} references {string.Join(", ", violations)}.");
    }
}
