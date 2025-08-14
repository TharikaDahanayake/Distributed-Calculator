using System.Collections.Concurrent;

namespace CalculatorServer.Services;

public class TransactionState
{
    public int TransactionId { get; set; }
    public string Operation { get; set; }
    public int Number { get; set; }
    public int IntermediateResult { get; set; }
    public bool IsPrepared { get; set; }
    public bool IsCommitted { get; set; }
    public bool IsAborted { get; set; }
    public Dictionary<string, bool> ParticipantVotes { get; set; }

    public TransactionState(int id, string operation, int number)
    {
        TransactionId = id;
        Operation = operation;
        Number = number;
        ParticipantVotes = new Dictionary<string, bool>();
        IsPrepared = false;
        IsCommitted = false;
        IsAborted = false;
    }
}

public class TransactionManager
{
    private readonly ConcurrentDictionary<int, TransactionState> _transactions;
    private readonly ILogger<TransactionManager> _logger;
    private int _nextTransactionId = 1;
    private readonly string _logPath;

    public TransactionManager(ILogger<TransactionManager> logger)
    {
        _transactions = new ConcurrentDictionary<int, TransactionState>();
        _logger = logger;
        _logPath = Path.Combine(AppContext.BaseDirectory, "transaction.log");
    }

    private async Task LogTransaction(string message)
    {
        var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} - {message}";
        _logger.LogInformation(logEntry);
        await File.AppendAllLinesAsync(_logPath, new[] { logEntry });
    }

    public async Task<int> CreateTransaction(string operation, int number)
    {
        var transactionId = Interlocked.Increment(ref _nextTransactionId);
        var transaction = new TransactionState(transactionId, operation, number);
        _transactions[transactionId] = transaction;
        await LogTransaction($"Transaction {transactionId} created: {operation} on {number}");
        return transactionId;
    }

    public async Task<bool> PrepareTransaction(int transactionId, string participantId, bool vote)
    {
        if (_transactions.TryGetValue(transactionId, out var transaction))
        {
            transaction.ParticipantVotes[participantId] = vote;
            await LogTransaction($"Transaction {transactionId}: Participant {participantId} voted {vote}");
            return true;
        }
        return false;
    }

    public async Task<bool> CommitTransaction(int transactionId)
    {
        if (_transactions.TryGetValue(transactionId, out var transaction))
        {
            if (transaction.ParticipantVotes.Values.All(v => v))
            {
                transaction.IsCommitted = true;
                await LogTransaction($"Transaction {transactionId} COMMITTED");
                return true;
            }
            else
            {
                await AbortTransaction(transactionId, "Not all participants voted yes");
                return false;
            }
        }
        return false;
    }

    public async Task<bool> AbortTransaction(int transactionId, string reason)
    {
        if (_transactions.TryGetValue(transactionId, out var transaction))
        {
            transaction.IsAborted = true;
            await LogTransaction($"Transaction {transactionId} ABORTED: {reason}");
            return true;
        }
        return false;
    }

    public bool SetIntermediateResult(int transactionId, int result)
    {
        if (_transactions.TryGetValue(transactionId, out var transaction))
        {
            transaction.IntermediateResult = result;
            return true;
        }
        return false;
    }

    public TransactionState GetTransaction(int transactionId)
    {
        return _transactions.TryGetValue(transactionId, out var transaction) ? transaction : null;
    }
}
