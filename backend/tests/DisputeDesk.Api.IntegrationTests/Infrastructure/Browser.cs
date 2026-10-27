using System.Net.Http.Json;
using System.Text.Json;
using DisputeDesk.Contracts.Bff;
using Microsoft.AspNetCore.WebUtilities;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>What a browser does against the Api: sign in through the code flow, read JSON, fetch the antiforgery token.</summary>
public static class Browser
{
    /// <summary>
    /// Signs in as <paramref name="sub"/>: GET /api/v1/bff/login, Zitadel issues a code for the authorize request,
    /// then the browser posts it to the callback (response_mode form_post, the handler's default). Returns the
    /// callback's response, the one that sets the session cookie.
    /// </summary>
    public static async Task<HttpResponseMessage> SignInAsync(
        this HttpClient browser, FakeZitadel zitadel, string sub = "login-a", bool emailVerified = true, string returnUrl = "/")
    {
        HttpResponseMessage login = await browser.GetAsync(
            new Uri($"/api/v1/bff/login?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative), TestContext.Current.CancellationToken);
        Uri authorize = login.Headers.Location ?? throw new InvalidOperationException($"Login answered {login.StatusCode} without a redirect.");
        string state = QueryHelpers.ParseQuery(authorize.Query)["state"].ToString();
        string code = zitadel.IssueCode(authorize, sub, emailVerified);

        using var callback = new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code, ["state"] = state });
        return await browser.PostAsync(new Uri("/api/signin-oidc", UriKind.Relative), callback, TestContext.Current.CancellationToken);
    }

    public static async Task<HttpResponseMessage> GetPathAsync(this HttpClient browser, string path) =>
        await browser.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

    public static async Task<string> SessionStateAsync(this HttpClient browser)
    {
        using JsonDocument body = await (await browser.GetPathAsync("/api/v1/bff/session")).ReadJsonAsync();
        return body.RootElement.GetProperty("state").GetString() ?? "";
    }

    public static async Task<string> AntiforgeryTokenAsync(this HttpClient browser)
    {
        AntiforgeryResponse? body = await browser.GetFromJsonAsync<AntiforgeryResponse>(
            new Uri("/api/v1/bff/antiforgery", UriKind.Relative), TestContext.Current.CancellationToken);
        return body?.RequestToken ?? throw new InvalidOperationException("No antiforgery token.");
    }

    /// <summary>POSTs the logout form, with the token in the header when one is given.</summary>
    public static async Task<HttpResponseMessage> LogoutAsync(this HttpClient browser, string? antiforgeryToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/bff/logout", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent([]),
        };
        if (antiforgeryToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        }

        return await browser.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
