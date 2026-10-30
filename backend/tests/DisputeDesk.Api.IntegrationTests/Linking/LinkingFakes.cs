using System.Collections.Concurrent;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// In-process fakes of the three ports the linking job calls, answering for the fixture customers, each able to hold
/// its next call until the test releases it. The job's database work is real; only the external systems are faked.
/// </summary>
public static class LinkingFakes
{
    /// <summary>A fixture customer's details as <c>fixtures/local.json</c> holds them.</summary>
    public sealed record Customer(string IdNumber, string AccountNumber, string CustomerId, string Cellphone)
    {
        // The details are personal data even in a test: never printed by an assertion message.
        public override string ToString() => nameof(Customer);
    }

    /// <summary>Linked (to a login the test links).</summary>
    public static readonly Customer A = new("9001015000085", "1000000011", "CUS-000001", "+27820000001");

    /// <summary>The SIM-swap check answers a recent swap for this customer's cellphone.</summary>
    public static readonly Customer C = new("9202295801081", "1000000031", "CUS-000003", "+27820000003");

    /// <summary>Not linked; passes every check.</summary>
    public static readonly Customer D = new("8804125123080", "1000000041", "CUS-000004", "+27820000004");

    /// <summary>Well-formed details that match no customer: D's ID number with an account that is not D's.</summary>
    public static readonly Customer NoMatch = new("8804125123080", "1000000049", "", "");

    /// <summary>Holds one call until <see cref="Release"/>, or until the caller's token is cancelled.</summary>
    public sealed class Hold
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the held call has arrived and is waiting.</summary>
        public Task Reached => _reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

        public void Release() => _released.TrySetResult();

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            _reached.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>The three fakes of one test Api.</summary>
    public sealed class Ports
    {
        public CoreBankingFake CoreBanking { get; } = new();

        public SimSwapFake SimSwap { get; } = new();

        public SmsFake Sms { get; } = new();

        public void AddTo(IServiceCollection services)
        {
            // Registered after the real typed clients, so these win (the last registration is the one resolved).
            services.AddSingleton<ICoreBankingClient>(CoreBanking);
            services.AddSingleton<ISimSwapCheck>(SimSwap);
            services.AddSingleton<ISmsSender>(Sms);
        }
    }

    /// <summary>Hands out a <see cref="Hold"/> for the next call only.</summary>
    public abstract class Holdable
    {
        private readonly ConcurrentQueue<Hold> _holds = new();

        /// <summary>Holds the next call that has no hold yet; calls after it pass.</summary>
        public Hold HoldNext()
        {
            var hold = new Hold();
            _holds.Enqueue(hold);
            return hold;
        }

        protected async Task WaitIfHeldAsync(CancellationToken cancellationToken)
        {
            if (_holds.TryDequeue(out Hold? hold))
            {
                await hold.WaitAsync(cancellationToken);
            }
        }
    }

    /// <summary>Matches only the fixture customers' details; can answer unavailable.</summary>
    public sealed class CoreBankingFake : Holdable, ICoreBankingClient
    {
        private static readonly Customer[] Customers = [A, C, D];
        private int _calls;

        public bool Unavailable { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public async Task<Result<CustomerMatch>> MatchCustomerAsync(string idNumber, string accountNumber, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await WaitIfHeldAsync(cancellationToken);
            if (Unavailable)
            {
                return CoreBankingErrors.Unavailable;
            }

            Customer? customer = Customers.FirstOrDefault(customer => customer.IdNumber == idNumber && customer.AccountNumber == accountNumber);
            return customer is null ? CoreBankingErrors.NotFound : new CustomerMatch(customer.CustomerId, customer.Cellphone);
        }

        public Task<Result<IReadOnlyList<Account>>> GetAccountsAsync(string customerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Linking never reads accounts.");

        public Task<Result<TransactionPage>> GetTransactionsAsync(string customerId, string? cursor, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Linking never reads transactions.");

        public Task<Result<Transaction>> GetTransactionAsync(string customerId, string accountRef, string transactionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Linking never reads a transaction.");
    }

    /// <summary>How the SIM-swap check answers.</summary>
    public enum SimSwapAnswer
    {
        /// <summary>As the fixtures say: a recent swap for customer C, none for the others.</summary>
        FromFixtures,

        /// <summary>The check fails with an error, which the port reports as failed (SimSwapCheckTests).</summary>
        Error,

        /// <summary>
        /// The check times out, which the port reports as failed. This fake only answers false; the timeout itself is
        /// proved at the port (SimSwapCheckTests).
        /// </summary>
        Timeout,
    }

    public sealed class SimSwapFake : Holdable, ISimSwapCheck
    {
        private readonly ConcurrentQueue<string> _cellphones = new();

        public SimSwapAnswer Answer { get; set; } = SimSwapAnswer.FromFixtures;

        /// <summary>The cellphone of each check, in order.</summary>
        public IReadOnlyCollection<string> Cellphones => _cellphones;

        public async Task<bool> PassesAsync(string cellphone, CancellationToken cancellationToken)
        {
            _cellphones.Enqueue(cellphone);
            await WaitIfHeldAsync(cancellationToken);
            return Answer switch
            {
                SimSwapAnswer.FromFixtures => cellphone != C.Cellphone,
                SimSwapAnswer.Error or SimSwapAnswer.Timeout => false,
                _ => throw new ArgumentOutOfRangeException(nameof(cellphone), Answer, "Unknown answer."),
            };
        }
    }

    public sealed class SmsFake : Holdable, ISmsSender
    {
        private readonly ConcurrentQueue<(string Cellphone, string Message)> _received = new();

        /// <summary>Thrown by every send while set, after the message was received.</summary>
        public Exception? Failure { get; set; }

        /// <summary>Every message handed to the sender, the failed ones included.</summary>
        public IReadOnlyCollection<(string Cellphone, string Message)> Received => _received;

        public async Task SendAsync(string cellphone, string message, CancellationToken cancellationToken)
        {
            _received.Enqueue((cellphone, message));
            await WaitIfHeldAsync(cancellationToken);
            if (Failure is not null)
            {
                throw Failure;
            }
        }

        /// <summary>The six-digit OTP of the only message received.</summary>
        public string OnlyOtp() => System.Text.RegularExpressions.Regex.Match(Assert.Single(_received).Message, "[0-9]{6}").Value;
    }
}
