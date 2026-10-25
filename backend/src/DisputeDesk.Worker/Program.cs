using DisputeDesk.Worker;

using var host = WorkerHost.CreateBuilder(args).Build();
await host.RunAsync();
