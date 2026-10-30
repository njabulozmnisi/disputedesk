using Confluent.Kafka;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// A producer whose first <paramref name="failures"/> sends fail with <paramref name="error"/>, as Confluent.Kafka reports a
/// failed delivery (a <see cref="ProduceException{TKey,TValue}"/> carrying the message), and whose later sends go to the
/// real <paramref name="producer"/>. The relay uses only <c>ProduceAsync</c> and <c>Dispose</c>.
/// </summary>
public sealed class FailingProducer(Error error, int failures, IProducer<string, string> producer) : IProducer<string, string>
{
    private int _failuresLeft = failures;

    public bool Disposed { get; private set; }

    public string Name => producer.Name;

    public Handle Handle => producer.Handle;

    public Task<DeliveryResult<string, string>> ProduceAsync(string topic, Message<string, string> message, CancellationToken cancellationToken = default)
    {
        if (_failuresLeft == 0)
        {
            return producer.ProduceAsync(topic, message, cancellationToken);
        }

        _failuresLeft--;
        var result = new DeliveryResult<string, string> { Topic = topic, Message = message, Status = PersistenceStatus.NotPersisted };
        return Task.FromException<DeliveryResult<string, string>>(new ProduceException<string, string>(error, result));
    }

    public Task<DeliveryResult<string, string>> ProduceAsync(TopicPartition topicPartition, Message<string, string> message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void Dispose()
    {
        Disposed = true;
        producer.Dispose();
    }

    public void Produce(string topic, Message<string, string> message, Action<DeliveryReport<string, string>>? deliveryHandler = null) =>
        throw new NotSupportedException();

    public void Produce(TopicPartition topicPartition, Message<string, string> message, Action<DeliveryReport<string, string>>? deliveryHandler = null) =>
        throw new NotSupportedException();

    public int Poll(TimeSpan timeout) => throw new NotSupportedException();

    public int Flush(TimeSpan timeout) => throw new NotSupportedException();

    public void Flush(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void InitTransactions(TimeSpan timeout) => throw new NotSupportedException();

    public void BeginTransaction() => throw new NotSupportedException();

    public void CommitTransaction(TimeSpan timeout) => throw new NotSupportedException();

    public void CommitTransaction() => throw new NotSupportedException();

    public void AbortTransaction(TimeSpan timeout) => throw new NotSupportedException();

    public void AbortTransaction() => throw new NotSupportedException();

    public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) =>
        throw new NotSupportedException();

    public int AddBrokers(string brokers) => throw new NotSupportedException();

    public void SetSaslCredentials(string username, string password) => throw new NotSupportedException();
}
