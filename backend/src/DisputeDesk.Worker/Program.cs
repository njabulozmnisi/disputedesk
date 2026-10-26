using DisputeDesk.Worker;

using IHost host = WorkerHost.CreateBuilder(args).Build();
await host.RunAsync();
