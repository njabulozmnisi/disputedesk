using DisputeDesk.Api;
using DisputeDesk.Api.Authorization;
using DisputeDesk.Api.Bff;
using DisputeDesk.Api.Errors;
using DisputeDesk.Api.Health;
using DisputeDesk.Api.Security;
using DisputeDesk.Api.Transactions;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Infrastructure;
using DisputeDesk.Infrastructure.CoreBanking;
using DisputeDesk.Infrastructure.Observability;
using DisputeDesk.Infrastructure.Seeding;
using DisputeDesk.Infrastructure.Sms;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

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

builder.Services.AddInfrastructure();
builder.Services.AddCoreBanking();
builder.Services.AddSmsSender();
builder.Services.AddSeeder();
builder.Services.ConfigureHttpJsonOptions(options => DisputeDeskJson.Apply(options.SerializerOptions));
builder.Services.AddValidation();
// A parameter that fails to bind is a 400 in every environment. Development's default (true) throws
// BadHttpRequestException instead, which the exception handler would answer as a 500.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddDisputeDeskProblemDetails();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddDisputeDeskHealthChecks();
builder.Services.AddBff();
builder.Services.AddCustomerRoutes();
builder.Services.AddTransactions();

WebApplication app = builder.Build();

app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDisputeDeskHealthChecks();
RouteGroupBuilder apiV1 = app.MapApiV1();
apiV1.MapBff();
apiV1.MapCustomerRoutes().MapTransactions();

await app.RunAsync();
