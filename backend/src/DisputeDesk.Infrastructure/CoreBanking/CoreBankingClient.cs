using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using Microsoft.Extensions.Logging;
using Polly;

namespace DisputeDesk.Infrastructure.CoreBanking;

/// <summary>
/// <see cref="ICoreBankingClient"/> over HTTP to the core-banking stub (AD-21). The customer ID and account reference
/// travel in headers and the ID number in the body, never in the URL, so none of them reaches an access log, a
/// trace or a metric.
/// </summary>
public sealed partial class CoreBankingClient(HttpClient http, ILogger<CoreBankingClient> logger) : ICoreBankingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // A missing field, or null where the record does not allow it, is a JsonException (so Unavailable), never a
        // null that fails later outside the error handling.
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public Task<Result<CustomerMatch>> MatchCustomerAsync(string idNumber, string accountNumber, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "customers/match")
        {
            Content = JsonContent.Create(new { idNumber, accountNumber }),
        };
        return SendAsync<CustomerMatch>(request, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<Account>>> GetAccountsAsync(string customerId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<Account>> accounts = await SendAsync<IReadOnlyList<Account>>(GetRequest("accounts", customerId), cancellationToken);
        if (accounts.IsSuccess && !accounts.Value.All(account => Last4Shape().IsMatch(account.AccountLast4)))
        {
            return Unmasked();
        }

        return accounts;
    }

    public async Task<Result<TransactionPage>> GetTransactionsAsync(string customerId, string? cursor, int limit, CancellationToken cancellationToken)
    {
        string path = cursor is null
            ? $"transactions?limit={limit}"
            : $"transactions?limit={limit}&cursor={Uri.EscapeDataString(cursor)}";
        Result<TransactionPage> page = await SendAsync<TransactionPage>(GetRequest(path, customerId), cancellationToken);
        if (page.IsSuccess && !page.Value.Items.All(IsMasked))
        {
            return Unmasked();
        }

        return page;
    }

    public async Task<Result<Transaction>> GetTransactionAsync(string customerId, string accountRef, string transactionId, CancellationToken cancellationToken)
    {
        // A value that is not shaped like an ID cannot exist, so it is not found. Stopping it here matters: a non-ASCII
        // value makes HttpClient fail before sending, which the resilience handler would count towards opening the
        // shared circuit, and a line break would be refused as a header value.
        if (!IdShape().IsMatch(accountRef) || !IdShape().IsMatch(transactionId))
        {
            return CoreBankingErrors.NotFound;
        }

        HttpRequestMessage request = GetRequest("transaction", customerId);
        request.Headers.Add("X-Account-Ref", accountRef);
        request.Headers.Add("X-Transaction-Id", transactionId);
        Result<Transaction> transaction = await SendAsync<Transaction>(request, cancellationToken);
        if (transaction.IsSuccess && !IsMasked(transaction.Value))
        {
            return Unmasked();
        }

        return transaction;
    }

    /// <summary>
    /// Full account and card numbers never enter DisputeDesk (AD-21, NFR-SEC-5). An answer that breaks the masking is
    /// treated like any other broken answer, as Unavailable, so it is never passed on.
    /// </summary>
    private static bool IsMasked(Transaction transaction) =>
        Last4Shape().IsMatch(transaction.AccountLast4)
        && (transaction.MaskedCard is null || MaskedCardShape().IsMatch(transaction.MaskedCard));

    /// <summary>Logs that an answer broke the masking, without any of its values, and returns Unavailable.</summary>
    private Error Unmasked()
    {
        LogUnmasked(logger);
        return CoreBankingErrors.Unavailable;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Core banking answered with an unmasked account or card number; treated as unavailable")]
    private static partial void LogUnmasked(ILogger logger);

    private static HttpRequestMessage GetRequest(string path, string customerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Customer-Id", customerId);
        return request;
    }

    private async Task<Result<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
        where T : notnull
    {
        using (request)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(CoreBankingRegistration.WholeCallDeadline);
            try
            {
                using HttpResponseMessage response = await http.SendAsync(request, deadline.Token);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return CoreBankingErrors.NotFound;
                }

                // The stub answers 400 to a cursor it does not recognise, a limit outside 1 to 50 or a malformed body.
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    return CoreBankingErrors.InvalidPaging;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return CoreBankingErrors.Unavailable;
                }

                T? body = await response.Content.ReadFromJsonAsync<T>(JsonOptions, deadline.Token);
                if (body is null)
                {
                    return CoreBankingErrors.Unavailable;
                }

                return Result.Success(body);
            }
            catch (HttpRequestException)
            {
                return CoreBankingErrors.Unavailable;
            }
            catch (JsonException)
            {
                // The stub answered with a body that is not the expected JSON.
                return CoreBankingErrors.Unavailable;
            }
            catch (ExecutionRejectedException)
            {
                // The resilience handler gave up: the total timeout ran out, or the circuit is open.
                return CoreBankingErrors.Unavailable;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The whole-call deadline passed, for example while the body was still on its way. A cancellation by
                // the caller is not caught here, so it still propagates.
                return CoreBankingErrors.Unavailable;
            }
        }
    }

    // Account references and transaction IDs look like ACC-A-1 and TXN-A-0001: upper-case letters, digits and hyphens.
    // \z, not $, so a trailing line break does not match.
    [GeneratedRegex(@"^[A-Z0-9-]{1,32}\z")]
    private static partial Regex IdShape();

    // The last 4 digits of an account number. [0-9], not \d, which also matches other scripts' digits.
    [GeneratedRegex(@"^[0-9]{4}\z")]
    private static partial Regex Last4Shape();

    // A card's first 6 and last 4 digits with the 6 in between masked, such as 492181******4417.
    [GeneratedRegex(@"^[0-9]{6}\*{6}[0-9]{4}\z")]
    private static partial Regex MaskedCardShape();
}
