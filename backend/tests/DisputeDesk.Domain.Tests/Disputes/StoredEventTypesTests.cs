using System.Reflection;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Tests.Disputes;

/// <summary>AD-13, AD-14a: the closed set of stored event types, each a sealed record deriving from one abstract record.</summary>
public class StoredEventTypesTests
{
    private static readonly Type[] StoredEventTypes =
    [
        .. typeof(StoredEvent).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(StoredEvent))),
    ];

    [Fact]
    public void StoredEvent_DomainTypes_AreExactlyTheSixTypes()
    {
        Assert.Equal(
            ["DisputeEscalated", "DisputeRaised", "DisputeResolved", "DisputeWithdrawn", "ReviewStarted", "SlaWarningRaised"],
            StoredEventTypes.Select(type => type.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void StoredEvent_EveryType_IsASealedRecordDerivingFromTheAbstractRecord()
    {
        Assert.True(typeof(StoredEvent).IsAbstract);
        Assert.True(IsRecord(typeof(StoredEvent)));
        Assert.All(StoredEventTypes, type =>
        {
            Assert.True(type.IsSealed, type.Name);
            Assert.True(IsRecord(type), type.Name);
            Assert.Equal(typeof(StoredEvent), type.BaseType);
        });
    }

    [Fact]
    public void StoredEvent_SharedFields_AreTheInstantActorTraceAndVersion()
    {
        string[] shared = [.. typeof(StoredEvent).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(["ActorId", "ActorType", "DisputeId", "OccurredAtUtc", "TraceId", "Version"], shared);
    }

    [Theory]
    [InlineData(typeof(ReviewStarted))]
    [InlineData(typeof(DisputeEscalated))]
    [InlineData(typeof(DisputeResolved))]
    [InlineData(typeof(DisputeWithdrawn))]
    public void StatusChange_OwnData_HoldsFromAndToStatus(Type type)
    {
        Assert.Equal(typeof(DisputeStatus), type.GetProperty("FromStatus")?.PropertyType);
        Assert.Equal(typeof(DisputeStatus), type.GetProperty("ToStatus")?.PropertyType);
    }

    [Fact]
    public void DisputeRaised_OwnData_HasAToStatusAndTheSnapshotButNoFromStatus()
    {
        Assert.Null(typeof(DisputeRaised).GetProperty("FromStatus"));
        Assert.Equal(typeof(DisputeStatus), typeof(DisputeRaised).GetProperty("ToStatus")?.PropertyType);
        Assert.Equal(typeof(TransactionSnapshot), typeof(DisputeRaised).GetProperty("Transaction")?.PropertyType);
    }

    [Fact]
    public void DisputeResolved_OwnData_HoldsTheOutcomeAndTheNoteForTheCustomer()
    {
        Assert.Equal(typeof(Outcome), typeof(DisputeResolved).GetProperty("Outcome")?.PropertyType);
        Assert.Equal(typeof(string), typeof(DisputeResolved).GetProperty("Note")?.PropertyType);
    }

    [Fact]
    public void StoredEvent_ToString_PrintsOnlyTheTypeName()
    {
        var transaction = new TransactionSnapshot(
            "account-marker", "transaction-marker", "last4-marker", DateTimeOffset.UnixEpoch, "description-marker", 87.23m, "ZAR",
            "card-marker", TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        var raised = new DisputeRaised("customer-marker", transaction, Reason.WrongAmount, "note-marker", 80m, 7.23m, DisputeStatus.Submitted)
        {
            ActorType = ActorType.Customer,
            ActorId = "login-marker",
            TraceId = "trace-marker",
        };

        // A record prints every property by default; a stored event holds personal data, so it prints only its type.
        Assert.Equal(nameof(DisputeRaised), raised.ToString());
    }

    // The compiler gives every record a protected or private virtual EqualityContract property.
    private static bool IsRecord(Type type) =>
        type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null;
}
