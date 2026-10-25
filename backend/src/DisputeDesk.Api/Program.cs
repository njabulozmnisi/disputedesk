using DisputeDesk.Api;
using DisputeDesk.Api.Errors;
using DisputeDesk.Api.Health;
using DisputeDesk.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddDisputeDeskHealthChecks();

var app = builder.Build();

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
