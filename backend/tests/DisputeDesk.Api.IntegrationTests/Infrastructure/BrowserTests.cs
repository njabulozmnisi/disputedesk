using System.Net;
using System.Text;
using Xunit.Sdk;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>The body helper the response comparisons rely on: it removes only a real <c>traceId</c>.</summary>
public class BrowserTests
{
    [Fact]
    public async Task BodyWithoutTraceId_TraceIdPresent_ReturnsTheRestOfTheBody()
    {
        using HttpResponseMessage response = Json("""{"title":"Not found","traceId":"00-abc-def-01"}""");

        Assert.Equal("""{"title":"Not found"}""", await response.BodyWithoutTraceIdAsync());
    }

    [Theory]
    [InlineData("""{"title":"Not found"}""")]
    [InlineData("""{"title":"Not found","traceId":null}""")]
    [InlineData("""{"title":"Not found","traceId":""}""")]
    [InlineData("""{"title":"Not found","traceId":42}""")]
    public async Task BodyWithoutTraceId_TraceIdMissingNullEmptyOrNotAString_Fails(string json)
    {
        using HttpResponseMessage response = Json(json);

        await Assert.ThrowsAnyAsync<XunitException>(response.BodyWithoutTraceIdAsync);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.NotFound) { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") };
}
