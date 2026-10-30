using System.Diagnostics;
using Confluent.Kafka;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// Criterion 2 of story 4.2: each delivery error is classified once (AD-17, Failures). A fatal error recreates the
/// producer (librdkafka INTRODUCTION.md, idempotent producer fatal errors); only a permanent record rejection counts an
/// attempt; everything else is transient, because librdkafka already retries it until <c>message.timeout.ms</c>. The
/// fatal path on a running relay is in <see cref="OutboxRelayPrivacyTests"/>.
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayErrorClassificationTests(OutboxFixture outbox)
{
    [Fact]
    public async Task Relay_SendFailsWithAnUnlistedCode_LogsOneCodeOnlyWarningPerFailureAndCountsNoAttempt()
    {
        RelayClock clock = OutboxFixture.NewClock();
        // A day back, so this row is the oldest due row and the one the failing producer gets.
        DateTimeOffset at = clock.GetUtcNow().AddDays(-1);
        OutboxMessage row = await outbox.RaiseAsync(at);
        // A code on neither list and not fatal. Error.Reason is the client's free text, which no log line may carry.
        const string Reason = "REASON-TEXT-UNLISTED";
        var unlisted = new Error(ErrorCode.TopicAuthorizationFailed, Reason, isFatal: false);
        const int Failures = 3;

        await using RelayHost host = await outbox.StartRelayAsync(clock, builder => new FailingProducer(unlisted, Failures, builder.Build()));
        for (int failure = 1; failure <= Failures; failure++)
        {
            // After each failed send the relay waits out its backoff (1, 2 then 4 seconds) with the row untouched.
            await OutboxFixture.WaitUntilAsync(() => clock.RelayWaitsNow && SendDeferredLines(host).Count == failure);
            OutboxMessage during = await outbox.RowAsync(row.Id);
            Assert.Equal((OutboxStatus.Pending, 0, (string?)null), (during.Status, during.Attempts, during.LastError));
            clock.Advance(TimeSpan.FromSeconds(10));
        }

        // The 4th send goes to the real producer.
        await outbox.WaitUntilSentAsync([row.Id], clock);
        OutboxMessage after = await outbox.RowAsync(row.Id);
        Assert.Equal((OutboxStatus.Sent, 0, (string?)null), (after.Status, after.Attempts, after.LastError));

        // One warning per failed send, carrying the code only.
        IReadOnlyList<LogRecord> deferred = SendDeferredLines(host);
        Assert.Equal(Failures, deferred.Count);
        Assert.All(deferred, record =>
        {
            Assert.Equal(LogLevel.Warning, record.LogLevel);
            Assert.Null(record.Exception);
            Assert.Equal(
                new Dictionary<string, string?> { ["ErrorCode"] = "TopicAuthorizationFailed" },
                (record.Attributes ?? []).Where(attribute => attribute.Key != "{OriginalFormat}").ToDictionary(attribute => attribute.Key, attribute => attribute.Value?.ToString()));
        });
        Assert.DoesNotContain(host.LogLines, line => line.Contains(Reason, StringComparison.Ordinal));

        // Each failed send's span carries the same code.
        IReadOnlyList<Activity> failedSpans = [.. host.ProducerSpansFor(row.Id).Where(span => span.Status == ActivityStatusCode.Error)];
        Assert.Equal(Failures, failedSpans.Count);
        Assert.All(failedSpans, span => Assert.Equal("TopicAuthorizationFailed", span.GetTagItem("error.type")));
    }

    private static IReadOnlyList<LogRecord> SendDeferredLines(RelayHost host) =>
        [.. host.Logs.Snapshot().Where(record => record.FormattedMessage?.StartsWith("Outbox relay could not send an event", StringComparison.Ordinal) == true)];

    [Theory]
    [InlineData(ErrorCode.MsgSizeTooLarge)]
    [InlineData(ErrorCode.InvalidMsg)]
    [InlineData(ErrorCode.InvalidMsgSize)]
    [InlineData(ErrorCode.InvalidRecord)]
    [InlineData(ErrorCode.Local_BadMsg)]
    public void Classify_PermanentRecordRejection_IsRejected(ErrorCode code)
    {
        Assert.Equal(OutboxRelay.SendFailure.Rejected, OutboxRelay.Classify(new Error(code, "broker text", false)));
    }

    [Theory]
    // Broker unavailable or timeout (AD-17).
    [InlineData(ErrorCode.Local_AllBrokersDown)]
    [InlineData(ErrorCode.Local_Transport)]
    [InlineData(ErrorCode.Local_MsgTimedOut)]
    [InlineData(ErrorCode.Local_TimedOut)]
    // Leadership moving between brokers.
    [InlineData(ErrorCode.NotLeaderForPartition)]
    [InlineData(ErrorCode.LeaderNotAvailable)]
    // A missing topic.
    [InlineData(ErrorCode.Local_UnknownTopic)]
    [InlineData(ErrorCode.UnknownTopicOrPart)]
    // Unlisted codes, one of them no member of the enum at all.
    [InlineData(ErrorCode.TopicAuthorizationFailed)]
    [InlineData(ErrorCode.Local_QueueFull)]
    [InlineData((ErrorCode)12_345)]
    public void Classify_AnyOtherNonFatalError_IsTransient(ErrorCode code)
    {
        Assert.Equal(OutboxRelay.SendFailure.Transient, OutboxRelay.Classify(new Error(code, "broker text", false)));
    }

    [Theory]
    [InlineData(ErrorCode.OutOfOrderSequenceNumber)]
    [InlineData(ErrorCode.UnknownProducerId)]
    [InlineData(ErrorCode.MsgSizeTooLarge)]
    [InlineData(ErrorCode.Local_MsgTimedOut)]
    public void Classify_IsFatalTrue_IsFatalWhateverTheCode(ErrorCode code)
    {
        Assert.Equal(OutboxRelay.SendFailure.Fatal, OutboxRelay.Classify(new Error(code, "broker text", true)));
    }
}
