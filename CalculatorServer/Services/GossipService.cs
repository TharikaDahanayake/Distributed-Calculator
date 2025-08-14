using Grpc.Core;
using System.Threading;
using Shared;
using Grpc.Net.Client;
using CalculatorServer;

namespace CalculatorServer.Services;

public class GossipService
{
    private readonly ILogger<GossipService> _logger;
    private readonly LamportClock _clock;
    private readonly string _currentServer;
    private readonly List<string> _peerServers;
    private readonly Random _random;
    private Timer _gossipTimer;
    private readonly string _logPath;


    
    
    public GossipService(ILogger<GossipService> logger, LamportClock clock, IConfiguration configuration)
    {
        _logger = logger;
        _clock = clock;
        _currentServer = configuration["ASPNETCORE_URLS"] ?? "https://localhost:5001";
        _random = new Random();
        _logPath = Path.Combine(AppContext.BaseDirectory, "gossip.log");

        // Define peer servers (you can move this to configuration)
        _peerServers = new List<string>
        {
            "https://localhost:5001",
            "https://localhost:5002"
        };

        // Remove current server from peers
        _peerServers.Remove(_currentServer);

        // Start gossip timer (every 10 seconds)
        _gossipTimer = new Timer(GossipTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    private async void GossipTick(object state)
    {
        
        try
        {
            if (_peerServers.Count == 0)
            {
                await LogGossip("No peer servers available");
                return;
            }


            
            // Select a random peer
            var randomPeer = _peerServers[_random.Next(_peerServers.Count)];
            await LogGossip($"Selected peer: {randomPeer}");

            // Create channel to peer
            using var channel = GrpcChannel.ForAddress(randomPeer);
            var client = new ClockSyncService.ClockSyncServiceClient(channel);

            // Send current clock value
            var request = new ClockSyncRequest { CurrentClock = _clock.GetTime() };

            try
            {
                var response = await client.GetLatestClockAsync(request);

                // Update local clock if peer's clock is higher
                if (response.SyncedClock > _clock.GetTime())
                {
                    _clock.UpdateOnReceive((int)response.SyncedClock);
                    await LogGossip($"Updated clock from peer. New value: {_clock.GetTime()}");
                }
                else
                {
                    await LogGossip($"No update needed. Local clock ({_clock.GetTime()}) >= Peer clock ({response.SyncedClock})");
                }

                // Log convergence status
                await LogGossip($"Clock difference with {randomPeer}: {Math.Abs(response.SyncedClock - _clock.GetTime())}");
            }
            catch (Exception ex)
            {
                await LogGossip($"Failed to gossip with {randomPeer}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            await LogGossip($"Error in gossip tick: {ex.Message}");
        }
    }

    private async Task LogGossip(string message)
    {
        var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} - Server {_currentServer} - Clock {_clock.GetTime()} - {message}";
        _logger.LogInformation(logEntry);
        await File.AppendAllLinesAsync(_logPath, new[] { logEntry });
    }

    public void Stop()
    {
        _gossipTimer?.Dispose();
    }
}
