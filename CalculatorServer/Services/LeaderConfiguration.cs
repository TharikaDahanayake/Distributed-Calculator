using System.Text.Json;

namespace CalculatorServer.Services;

public class LeaderConfiguration
{
    private readonly string _configPath;
    private readonly ILogger<LeaderConfiguration> _logger;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public int LeaderPort { get; private set; }
    public List<int> BackupPorts { get; private set; }
    public string CurrentLeader { get; private set; } = "https://localhost:5001";

    public LeaderConfiguration(ILogger<LeaderConfiguration> logger, string configPath = "leader.config")
    {
        _logger = logger;
        _configPath = configPath;
        BackupPorts = new List<int>();
        LoadConfiguration().Wait();
    }

    private async Task LoadConfiguration()
    {
        try
        {
            await _semaphore.WaitAsync();
            var jsonString = await File.ReadAllTextAsync(_configPath);
            var config = JsonSerializer.Deserialize<JsonElement>(jsonString);
            
            LeaderPort = config.GetProperty("LeaderPort").GetInt32();
            BackupPorts = config.GetProperty("BackupPorts").EnumerateArray()
                              .Select(x => x.GetInt32())
                              .ToList();
            CurrentLeader = config.GetProperty("CurrentLeader").GetString() ?? "https://localhost:5001";
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading leader configuration: {ex.Message}");
            // Default values if file cannot be read
            LeaderPort = 5001;
            BackupPorts = new List<int> { 5002 };
            CurrentLeader = "https://localhost:5001";
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UpdateLeader(string newLeader)
    {
        try
        {
            await _semaphore.WaitAsync();
            CurrentLeader = newLeader;
            var config = new
            {
                LeaderPort = LeaderPort,
                BackupPorts = BackupPorts,
                CurrentLeader = newLeader
            };
            
            var jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_configPath, jsonString);
            _logger.LogInformation($"Updated leader to: {newLeader}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating leader configuration: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool IsLeader(string serverUrl)
    {
        return CurrentLeader.Equals(serverUrl, StringComparison.OrdinalIgnoreCase);
    }

    public string GetNextBackup()
    {
        var currentPort = int.Parse(CurrentLeader.Split(':').Last());
        var currentIndex = BackupPorts.IndexOf(currentPort);
        
        // If current leader is not in backup list or is last in list, use first backup
        var nextPort = (currentIndex == -1 || currentIndex == BackupPorts.Count - 1) 
            ? BackupPorts[0] 
            : BackupPorts[currentIndex + 1];
            
        return $"https://localhost:{nextPort}";
    }
}
