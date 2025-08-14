using CalculatorServer.Services;
using Shared;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();

// Add configuration
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

// Add singletons
builder.Services.AddSingleton<LeaderConfiguration>();
builder.Services.AddSingleton<TransactionManager>();

// Add the Calculator service first as it's required by TwoPhaseCommit service
builder.Services.AddSingleton<CalculatorServiceImpl>();
builder.Services.AddSingleton<TwoPhaseCommitServiceImpl>();

// Add Gossip service
builder.Services.AddSingleton<LamportClock>();
builder.Services.AddSingleton<GossipService>();

// Configure Kestrel
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "https://localhost:5001");

// Configure Kestrel for HTTP/2
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(lo => lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Get GossipService instance for cleanup
var gossipService = app.Services.GetRequiredService<GossipService>();

// Handle shutdown
app.Lifetime.ApplicationStopping.Register(() => {
    gossipService.Stop();
});

// Configure the HTTP request pipeline.
app.MapGrpcService<GreeterService>();
app.MapGrpcService<CalculatorServiceImpl>();
app.MapGrpcService<TwoPhaseCommitServiceImpl>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
