using System.Reflection;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// A record's generated <c>ToString</c> prints every public property (C# records specification, "Printing members"), so a
/// record that holds personal data leaks it into any log line or interpolation that names it (OWASP Logging Cheat Sheet:
/// data to exclude). Each such record seals <c>ToString</c>, so neither it nor a derived record prints its members.
/// </summary>
public class PersonalDataRecordTests
{
    // shortcut: name list, a [PersonalData] attribute when a field slips past it
    private static readonly HashSet<string> PersonalDataNames =
    [
        "IdNumber", "AccountNumber", "CustomerId", "LoginSub", "UserId", "Note", "MaskedCard", "AccountRef", "TransactionId", "Cellphone",
    ];

    private static readonly Assembly[] CheckedAssemblies =
    [
        Assembly.Load("DisputeDesk.Contracts"),
        Assembly.Load("DisputeDesk.Application"),
        Assembly.Load("DisputeDesk.Domain"),
        Assembly.Load("DisputeDesk.Infrastructure"),
    ];

    [Fact]
    public void Record_HoldingPersonalData_SealsToString()
    {
        var violations = CheckedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => IsRecord(type) && HoldsPersonalData(type) && !SealsToString(type))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(violations.Count == 0, "Records holding personal data with the generated ToString:\n" + string.Join('\n', violations));
    }

    [Theory]
    [InlineData(typeof(SealedWithCustomerId), true, true)]
    [InlineData(typeof(OpenWithNote), true, false)]
    [InlineData(typeof(UnsealedOverrideWithUserId), true, false)]
    [InlineData(typeof(ListOfOpenRecords), true, false)]
    [InlineData(typeof(NestedOpenRecord), true, false)]
    [InlineData(typeof(DerivedFromSealedBase), true, true)]
    [InlineData(typeof(NoPersonalData), false, false)]
    public void Rule_SampleRecord_IsClassifiedByItsMembersAndToString(Type sample, bool holdsPersonalData, bool sealsToString)
    {
        Assert.True(IsRecord(sample));
        Assert.Equal(holdsPersonalData, HoldsPersonalData(sample));
        Assert.Equal(sealsToString, SealsToString(sample));
    }

    [Fact]
    public void IsRecord_PlainClass_IsFalse()
    {
        Assert.False(IsRecord(typeof(PlainClassWithCustomerId)));
    }

    // Every record, class or struct, has a synthesized PrintMembers method (C# records specification, "Printing members").
    private static bool IsRecord(Type type) =>
        type.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is not null;

    private static bool HoldsPersonalData(Type type) => HoldsPersonalData(type, []);

    // A property on the name list, or a property holding a record (directly, nullable or in a collection) that has one.
    private static bool HoldsPersonalData(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (PersonalDataNames.Contains(property.Name))
            {
                return true;
            }

            Type held = HeldType(property.PropertyType);
            if (IsRecord(held) && HoldsPersonalData(held, visited))
            {
                return true;
            }
        }

        return false;
    }

    // The element type of a collection, the underlying type of a nullable value type, otherwise the type itself.
    private static Type HeldType(Type type)
    {
        Type? enumerable = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerable?.GetGenericArguments()[0] ?? Nullable.GetUnderlyingType(type) ?? type;
    }

    // Declared on the record itself, or on a base record: C# lets no derived record replace a sealed ToString (CS0239).
    private static bool SealsToString(Type type) =>
        type.GetMethod(nameof(ToString), Type.EmptyTypes) is { IsFinal: true } toString
        && toString.DeclaringType is { } declaringType
        && (declaringType == type || type.IsSubclassOf(declaringType));

    private sealed record SealedWithCustomerId(string CustomerId)
    {
        public sealed override string ToString() => nameof(SealedWithCustomerId);
    }

    private sealed record OpenWithNote(string Note);

    private sealed record UnsealedOverrideWithUserId(string UserId)
    {
        public override string ToString() => nameof(UnsealedOverrideWithUserId);
    }

    private sealed record ListOfOpenRecords(IReadOnlyList<OpenWithNote> Items);

    private sealed record NestedOpenRecord(OpenWithNote Inner);

    private abstract record SealedBase
    {
        public sealed override string ToString() => GetType().Name;
    }

    private sealed record DerivedFromSealedBase(string AccountNumber) : SealedBase;

    private sealed record NoPersonalData(int Count);

    private sealed class PlainClassWithCustomerId
    {
        public string CustomerId { get; init; } = "";
    }
}
