using System.Diagnostics;
using DisputeDesk.Api;
using DisputeDesk.Api.Errors;
using DisputeDesk.Api.Health;
using DisputeDesk.Api.Security;
using DisputeDesk.Infrastructure;
using DisputeDesk.Infrastructure.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);
builder.AddDisputeDeskTelemetry("disputedesk-api", typeof(Program).Assembly)
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

builder.Services.AddInfrastructure();
// traceId is the W3C trace ID of the request (32 hex characters), the value to search for in the trace viewer.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToHexString() ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddDisputeDeskHealthChecks();

var app = builder.Build();

app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDisputeDeskHealthChecks();
app.MapApiV1();

await app.RunAsync();
