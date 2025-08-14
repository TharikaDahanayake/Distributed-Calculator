
using Grpc.Core;
using CalculatorServer; // For generated gRPC types
using Shared; // For VectorClock
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace CalculatorServer.Services;

public class CalculatorServiceImpl : CalculatorService.CalculatorServiceBase
{
    private readonly ILogger<CalculatorServiceImpl> _logger;
    private readonly LamportClock _clock;
    private readonly LeaderConfiguration _leaderConfig;
    private readonly TransactionManager _transactionManager;
    private static readonly Random _random = new();
    private readonly string _serverUrl;
    private readonly IConfiguration _configuration;

    public CalculatorServiceImpl(
        ILogger<CalculatorServiceImpl> logger,
        LeaderConfiguration leaderConfig,
        TransactionManager transactionManager,
        IConfiguration configuration)
    {
        _logger = logger;
        _clock = new LamportClock();
        _leaderConfig = leaderConfig;
        _transactionManager = transactionManager;
        _configuration = configuration;
        _serverUrl = configuration["ASPNETCORE_URLS"] ?? "https://localhost:5001";
    }

    public override async Task<CalculationResponse> Square(CalculationRequest request, ServerCallContext context)
    {
        return await HandleCalculation(request, x => x * x, "Square");
    }

    public override async Task<CalculationResponse> Cube(CalculationRequest request, ServerCallContext context)
    {
        return await HandleCalculation(request, x => x * x * x, "Cube");
    }

    private async Task<CalculationResponse> HandleCalculation(CalculationRequest request, Func<int, int> operation, string opName)
    {
        try
        {
            // Check if this server is the leader
            if (!_leaderConfig.IsLeader(_serverUrl))
            {
                return new CalculationResponse
                {
                    Result = 0,
                    Timestamp = _clock.GetTime(),
                    IsSuccess = false,
                    Message = $"Not the leader. Current leader is: {_leaderConfig.CurrentLeader}",
                    RedirectTo = _leaderConfig.CurrentLeader
                };
            }

            // Update Lamport clock on message receive
            _clock.UpdateOnReceive(request.Timestamp);
            
            // Simulate delay
            await Task.Delay(_random.Next(2000, 5001));
            
            // Simulate error (which could represent leader failure)
            if (request.Number < 0 || SimulateError())
            {
                // If leader fails, promote a backup
                string nextLeader = _leaderConfig.GetNextBackup();
                await _leaderConfig.UpdateLeader(nextLeader);
                
                return new CalculationResponse
                {
                    Result = 0,
                    Timestamp = _clock.GetTime(),
                    IsSuccess = false,
                    Message = $"Leader failed. New leader is: {nextLeader}",
                    RedirectTo = nextLeader
                };
            }
            
            // Perform calculation
            int result = operation(request.Number);
            
            // Increment clock before sending response
            _clock.Increment();
            
            return new CalculationResponse
            {
                Result = result,
                Timestamp = _clock.GetTime(),
                IsSuccess = true,
                Message = $"{opName} successful (processed by leader)"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in {opName}: {ex.Message}");
            return new CalculationResponse
            {
                Result = 0,
                Timestamp = _clock.GetTime(),
                IsSuccess = false,
                Message = ex.Message
            };
        }
    }

    private bool SimulateError()
    {
        return _random.Next(4) == 0;
    }
}
