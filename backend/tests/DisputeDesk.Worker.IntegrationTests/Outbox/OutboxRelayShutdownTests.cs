using DisputeDesk.Infrastructure.Persistence;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// Criterion 5 of story 4.2: on SIGTERM the relay finishes the one record in flight and takes no new one, and the host
/// stops within the default 30-second shutdown timeout (AD-25; NFR-RES-5; Microsoft Learn, <c>BackgroundService</c> and
/// the Generic Host shutdown timeout).
/// </summary>
[Collection(OutboxRelayTestGroup.Name)]
public class OutboxRelayShutdownTests(OutboxFixture outbox)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_StoppedWithASendInFlight_FinishesThatRecordOnlyAndStopsWithinTheShutdownTimeout(bool brokerReturnsBeforeTheDeliveryTimeout)
    {
        RelayClock clock = OutboxFixture.NewClock();
        RelayHost host = await outbox.StartRelayAsync(clock);
        OutboxMessage inFlight;
        OutboxMessage further;
        OutboxMessage other;
        TimeSpan stopTime;
        await using (host)
        {
            // Every due row already sent, waiting out its idle loop.
            await OutboxFixture.WaitUntilAsync(() => clock.RelayWaitsNow);
            DateTimeOffset at = clock.GetUtcNow();
            inFlight = await outbox.RaiseAsync(at);
            further = await outbox.AddVersionAsync(inFlight.DisputeId, 2, at);
            // A second later, so this dispute's head comes after the one in flight.
            other = await outbox.RaiseAsync(at.AddSeconds(1));

            await outbox.PauseBrokerAsync();
            bool paused = true;
            try
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                await OutboxFixture.WaitUntilAsync(() => host.SendStarted(inFlight.Id));
                Task<TimeSpan> stopping = host.StopAsync();
                if (brokerReturnsBeforeTheDeliveryTimeout)
                {
                    await outbox.UnpauseBrokerAsync();
                    paused = false;
                }

                stopTime = await stopping;
            }
            finally
            {
                if (paused)
                {
                    await outbox.UnpauseBrokerAsync();
                }
            }
        }

        Assert.InRange(stopTime, TimeSpan.Zero, TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
        OutboxMessage after = await outbox.RowAsync(inFlight.Id);
        if (brokerReturnsBeforeTheDeliveryTimeout)
        {
            Assert.Equal(OutboxStatus.Sent, after.Status);
            Assert.NotEmpty(outbox.RecordsFor(inFlight.DisputeId));
        }
        else
        {
            // The delivery timed out first: Pending for the next start, no attempt counted. The broker may still append
            // the record it was holding, so the topic is not checked for it.
            Assert.Equal((OutboxStatus.Pending, 0, (string?)null), (after.Status, after.Attempts, after.LastError));
        }

        // No further row was sent after the stop.
        Assert.Equal(OutboxStatus.Pending, (await outbox.RowAsync(further.Id)).Status);
        Assert.Equal(OutboxStatus.Pending, (await outbox.RowAsync(other.Id)).Status);
        Assert.DoesNotContain(outbox.RecordsFor(inFlight.DisputeId), record => record.Message.Value.Contains(further.Id.ToString(), StringComparison.Ordinal));
        Assert.Empty(outbox.RecordsFor(other.DisputeId));
    }
}
