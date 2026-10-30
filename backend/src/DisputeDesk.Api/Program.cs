using DisputeDesk.Api;
using DisputeDesk.Api.ApiReference;
using DisputeDesk.Api.Authorization;
using DisputeDesk.Api.Bff;
using DisputeDesk.Api.Disputes;
using DisputeDesk.Api.Errors;
using DisputeDesk.Api.Health;
using DisputeDesk.Api.Linking;
using DisputeDesk.Api.Operations;
using DisputeDesk.Api.Security;
using DisputeDesk.Api.Transactions;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Infrastructure;
using DisputeDesk.Infrastructure.CoreBanking;
using DisputeDesk.Infrastructure.Linking;
using DisputeDesk.Infrastructure.Observability;
using DisputeDesk.Infrastructure.Seeding;
using DisputeDesk.Infrastructure.Sms;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Local Compose mounts the provisioner's Zitadel client settings here (infra/README.md). Elsewhere the file is
// absent and the Zitadel:* keys come from the environment. The environment variables and the command line are added
// again after it, so they still override it, as with appsettings.json.
builder.Configuration.AddJsonFile("/run/zitadel/zitadel-clients.json", optional: true);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
if (BuildTimeOpenApi.IsRunning)
{
    builder.Configuration.AddInMemoryCollection(BuildTimeOpenApi.StandInSettings);
}

builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);
builder.AddDisputeDeskTelemetry("disputedesk-api", typeof(Program).Assembly)
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

builder.Services.AddForwardedHeadersTrust();
builder.Services.AddInfrastructure();
builder.Services.AddCoreBanking();
builder.Services.AddSmsSender();
builder.Services.AddLinking();
builder.Services.AddSeeder();
builder.Services.ConfigureHttpJsonOptions(options => DisputeDeskJson.Apply(options.SerializerOptions));
builder.Services.AddValidation();
// A parameter that fails to bind is a 400 in every environment. Development's default (true) throws
// BadHttpRequestException instead, which the exception handler would answer as a 500.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddDisputeDeskProblemDetails();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddApiReference();
builder.Services.AddDisputeDeskHealthChecks();
builder.Services.AddBff();
builder.Services.AddRequestTimeouts();
builder.Services.AddCustomerRoutes();
builder.Services.AddTransactions();
builder.Services.AddDisputes();
builder.Services.AddOperationsAuthentication();
builder.Services.AddOperations();
builder.Services.AddLinkingRoutes();
builder.Services.AddLinkingStart();

WebApplication app = builder.Build();

// First, so everything after it sees the client address, scheme and host the gateway forwarded (AD-23).
app.UseForwardedHeaders();
app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
// After routing (implicit here), so it reads each endpoint's timeout; a timeout the endpoint does not answer is a 504.
app.UseRequestTimeouts();

app.MapApiReference();
app.MapDisputeDeskHealthChecks();
RouteGroupBuilder apiV1 = app.MapApiV1();
apiV1.MapBff();
apiV1.MapCustomerRoutes().MapTransactions().MapDisputes();
apiV1.MapOperations();
apiV1.MapLinkingRoutes().MapLinkingStart().MapLinkingVerify();

await app.RunAsync();
