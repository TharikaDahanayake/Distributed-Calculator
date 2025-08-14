using Grpc.Core;

namespace CalculatorServer.Services;

public class TwoPhaseCommitServiceImpl : TwoPhaseCommitService.TwoPhaseCommitServiceBase
{
    private readonly ILogger<TwoPhaseCommitServiceImpl> _logger;
    private readonly TransactionManager _transactionManager;
    private readonly CalculatorServiceImpl _calculatorService;
    private readonly string _serverId;

    public TwoPhaseCommitServiceImpl(
        ILogger<TwoPhaseCommitServiceImpl> logger,
        TransactionManager transactionManager,
        CalculatorServiceImpl calculatorService,
        IConfiguration configuration)
    {
        _logger = logger;
        _transactionManager = transactionManager;
        _calculatorService = calculatorService;
        _serverId = configuration["ASPNETCORE_URLS"] ?? "https://localhost:5001";
    }

    public override async Task<PrepareResponse> Prepare(PrepareRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"Received prepare request for transaction {request.TransactionId}");
            
            // Check if we can perform the operation
            var canProceed = await ValidateOperation(request);
            
            // Vote YES only if we can perform the operation
            var response = new PrepareResponse
            {
                Ready = canProceed,
                ParticipantId = _serverId,
                Message = canProceed ? "Ready to proceed" : "Cannot perform operation"
            };

            await _transactionManager.PrepareTransaction(request.TransactionId, _serverId, canProceed);
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in prepare phase: {ex.Message}");
            return new PrepareResponse
            {
                Ready = false,
                ParticipantId = _serverId,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public override async Task<CommitResponse> Commit(CommitRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"Received commit request for transaction {request.TransactionId}");
            
            var transaction = _transactionManager.GetTransaction(request.TransactionId);
            if (transaction == null)
            {
                return new CommitResponse
                {
                    Success = false,
                    Message = "Transaction not found"
                };
            }

            // Perform the actual operation
            var result = await ExecuteOperation(transaction);
            
            await _transactionManager.CommitTransaction(request.TransactionId);
            
            return new CommitResponse
            {
                Success = true,
                Message = "Operation committed successfully",
                Result = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in commit phase: {ex.Message}");
            await _transactionManager.AbortTransaction(request.TransactionId, ex.Message);
            return new CommitResponse
            {
                Success = false,
                Message = $"Commit failed: {ex.Message}"
            };
        }
    }

    public override async Task<AbortResponse> Abort(AbortRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"Received abort request for transaction {request.TransactionId}: {request.Reason}");
            
            await _transactionManager.AbortTransaction(request.TransactionId, request.Reason);
            
            return new AbortResponse
            {
                Success = true,
                Message = "Transaction aborted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in abort phase: {ex.Message}");
            return new AbortResponse
            {
                Success = false,
                Message = $"Error during abort: {ex.Message}"
            };
        }
    }

    private async Task<bool> ValidateOperation(PrepareRequest request)
    {
        // Simulate validation - check if number is valid for the operation
        if (request.Number < 0)
        {
            return false;
        }

        // Simulate resource check
        await Task.Delay(100); // Simulated delay
        return true;
    }

    private async Task<int> ExecuteOperation(TransactionState transaction)
    {
        var calculationRequest = new CalculationRequest
        {
            Number = transaction.Operation == "Square" ? 
                     transaction.Number : 
                     transaction.IntermediateResult,
            Timestamp = 0  // You might want to use a proper timestamp here
        };

        var response = transaction.Operation == "Square" ?
            await _calculatorService.Square(calculationRequest, null) :
            await _calculatorService.Cube(calculationRequest, null);

        if (!response.IsSuccess)
        {
            throw new Exception($"Operation failed: {response.Message}");
        }

        return response.Result;
    }
}
