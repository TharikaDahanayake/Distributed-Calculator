using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Net.Client;
using System.Net.Http;
using CalculatorServer;
using Serilog;
using System.IO;
using System.Text.Json;

public class ServerManager
{
    private readonly List<string> serverAddresses;
    private readonly HttpClientHandler handler;
    private string? currentLeader;
    private const string LEADER_CONFIG_FILE = "leader_config.json";
    private Dictionary<string, CalculatorService.CalculatorServiceClient> clients;

    public ServerManager(List<string> servers)
    {
        serverAddresses = servers;
        handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        clients = new Dictionary<string, CalculatorService.CalculatorServiceClient>();
        InitializeClients();
        LoadOrInitializeLeader();
    }

    private void InitializeClients()
    {
        foreach (var server in serverAddresses)
        {
            var channel = GrpcChannel.ForAddress(server, new GrpcChannelOptions
            {
                HttpHandler = handler,
                ThrowOperationCanceledOnCancellation = true
            });
            clients[server] = new CalculatorService.CalculatorServiceClient(channel);
        }
    }

    private void LoadOrInitializeLeader()
    {
        try
        {
            if (File.Exists(LEADER_CONFIG_FILE))
            {
                var config = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(LEADER_CONFIG_FILE));
                currentLeader = config["leader"];
                Log.Information("Loaded leader from config: {Leader}", currentLeader);
            }
            else
            {
                // Default to first server as leader
                currentLeader = serverAddresses[0];
                SaveLeaderConfig();
                Log.Information("Initialized new leader: {Leader}", currentLeader);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error loading leader config: {Error}", ex.Message);
            currentLeader = serverAddresses[0];
            SaveLeaderConfig();
        }
    }

    private void SaveLeaderConfig()
    {
        try
        {
            var config = new Dictionary<string, string> { { "leader", currentLeader } };
            File.WriteAllText(LEADER_CONFIG_FILE, JsonSerializer.Serialize(config));
        }
        catch (Exception ex)
        {
            Log.Error("Error saving leader config: {Error}", ex.Message);
        }
    }

    public async Task<CalculationResponse> ExecuteOperation(
        int number, 
        string operation, 
        int timestamp,
        int maxRetries = 2)
    {
        int retryCount = 0;
        while (retryCount <= maxRetries)
        {
            try
            {
                var client = clients[currentLeader];
                var request = new CalculationRequest
                {
                    Number = number,
                    Timestamp = timestamp
                };

                CalculationResponse response;
                if (operation == "square")
                    response = await client.SquareAsync(request);
                else
                    response = await client.CubeAsync(request);

                return response;
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to contact leader {Leader}: {Error}", currentLeader, ex.Message);
                retryCount++;

                if (retryCount <= maxRetries)
                {
                    // Try failover to next server
                    await FailoverToNextServer();
                    Log.Information("Failing over to new leader: {Leader}", currentLeader);
                }
                else
                {
                    throw new Exception("All servers are unavailable");
                }
            }
        }

        throw new Exception("Operation failed after all retries");
    }

    private Task FailoverToNextServer()
    {
        var currentIndex = serverAddresses.IndexOf(currentLeader);
        var nextIndex = (currentIndex + 1) % serverAddresses.Count;
        currentLeader = serverAddresses[nextIndex];
        SaveLeaderConfig();
        return Task.CompletedTask;
    }

    public string GetCurrentLeader() => currentLeader;
}
