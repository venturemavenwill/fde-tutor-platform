using FdeTutor.Persistence;
using FdeTutor.ProjectionWorker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("FdeTutor");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:FdeTutor is required for the projection worker.");
}

builder.Services.AddDbContext<FdeTutorDbContext>(
    options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<S083ProjectionBatchProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
