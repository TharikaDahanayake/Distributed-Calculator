using Grpc.Core;
using System;
using System.Threading.Tasks;
using Shared;

namespace CalculatorServer.Services
{
    public class ClockSyncServiceImpl : ClockSyncService.ClockSyncServiceBase
    {
        private readonly ILogger<ClockSyncServiceImpl> _logger;
        private readonly LamportClock _clock;
        private static readonly Random _random = new();
        private DateTime _lastSyncTime = DateTime.UtcNow;

        public ClockSyncServiceImpl(ILogger<ClockSyncServiceImpl> logger)
        {
            _logger = logger;
            _clock = new LamportClock();
        }

        public override async Task<ClockSyncResponse> GetLatestClock(ClockSyncRequest request, ServerCallContext context)
        {
            try
            {
                // Update local clock if incoming clock is higher
                _clock.UpdateOnReceive((int)request.CurrentClock);
                
                // Calculate time since last sync
                var timeSinceSync = DateTime.UtcNow - _lastSyncTime;
                bool isDiverged = timeSinceSync.TotalSeconds > 5;

                if (isDiverged)
                {
                    _logger.LogWarning("Diverged: Clocks out of sync for more than 5 seconds");
                }

                // Simulate network delay to demonstrate eventual consistency
                await Task.Delay(_random.Next(100, 1000));

                // Update last sync time
                _lastSyncTime = DateTime.UtcNow;

                // Increment clock before sending response
                _clock.Increment();

                return new ClockSyncResponse
                {
                    SyncedClock = _clock.GetTime(),
                    IsDiverged = isDiverged,
                    Message = isDiverged ? "Diverged" : "Synchronized"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Clock sync failed: {ex.Message}");
                return new ClockSyncResponse
                {
                    SyncedClock = _clock.GetTime(),
                    IsDiverged = true,
                    Message = $"Sync failed: {ex.Message}"
                };
            }
        }

        // Method to get the current clock time
        public long GetCurrentTime()
        {
            return _clock.GetTime();
        }
    }
}
