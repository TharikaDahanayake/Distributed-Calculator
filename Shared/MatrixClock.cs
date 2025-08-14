using Serilog;

using System;

public static class MatrixClockSimulator
{
    private static readonly Random _rand = new();

    // Simulate a random network partition (returns true if partition occurs)
    public static bool SimulateNetworkPartition(double probability = 0.1)
    {
        if (_rand.NextDouble() < probability)
        {
            Log.Warning("Simulated network partition: messages may be delayed or lost.");
            Console.WriteLine("[Sim] Simulated network partition: messages may be delayed or lost.");
            return true;
        }
        return false;
    }

    // Simulate a random node failure (returns true if failure should occur)
    public static bool SimulateNodeFailure(double probability = 0.2)
    {
        if (_rand.NextDouble() < probability)
        {
            Log.Warning("Simulated node failure: message dropped or node unavailable.");
            Console.WriteLine("[Sim] Simulated node failure: message dropped or node unavailable.");
            return true;
        }
        return false;
    }

    // Simulate a random processing delay (in ms)
    public static void SimulateRandomDelay(int minMs = 1000, int maxMs = 5000)
    {
        int delay = _rand.Next(minMs, maxMs + 1);
        Log.Information("Simulating processing delay: {Delay} ms", delay);
        Console.WriteLine($"[Sim] Simulating processing delay: {delay} ms");
        System.Threading.Thread.Sleep(delay);
    }
    // Simulate skipping a clock update (returns true if update should be skipped)
    public static bool SimulateClockInconsistency(double probability = 0.1)
    {
        if (_rand.NextDouble() < probability)
        {
            Log.Warning("Simulated clock inconsistency: skipping clock update.");
            Console.WriteLine("[Sim] Simulated clock inconsistency: skipping clock update.");
            return true;
        }
        return false;
    }
}

public class MatrixRow
{
    public Dictionary<string, int> Row { get; set; } = new Dictionary<string, int>();
}

public class MatrixClock
{
    public string NodeId { get; }
    public Dictionary<string, Dictionary<string, int>> Clock { get; private set; }
    private static readonly Random _rand = new();

    public MatrixClock(string nodeId, IEnumerable<string> allNodeIds)
    {
        NodeId = nodeId;
        Clock = allNodeIds.ToDictionary(
            id => id,
            id => allNodeIds.ToDictionary(innerId => innerId, _ => 0)
        );
    }

    public void Increment()
    {
        Clock[NodeId][NodeId]++;
    }

    public void Merge(Dictionary<string, Dictionary<string, int>> incoming)
    {
        foreach (var node in incoming.Keys)
        {
            foreach (var inner in incoming[node].Keys)
            {
                Clock[node][inner] = Math.Max(Clock[node][inner], incoming[node][inner]);
            }
        }
    }

    public Dictionary<string, Dictionary<string, int>> GetClockCopy()
    {
        return Clock.ToDictionary(
            outer => outer.Key,
            outer => outer.Value.ToDictionary(inner => inner.Key, inner => inner.Value)
        );
    }

    public Dictionary<string, MatrixRow> ToProto()
    {
        return Clock.ToDictionary(
            outer => outer.Key,
            outer => new MatrixRow { Row = new Dictionary<string, int>(outer.Value) }
        );
    }

    public static MatrixClock FromProto(string nodeId, Dictionary<string, MatrixRow> proto)
    {
        var allNodeIds = proto.Keys;
        var clock = new MatrixClock(nodeId, allNodeIds);
        foreach (var outer in proto)
            foreach (var inner in outer.Value.Row)
                clock.Clock[outer.Key][inner.Key] = inner.Value;
        return clock;
    }

    // Simulate a random node failure (returns true if failure should occur)
    public static bool SimulateNodeFailure(double probability = 0.2)
    {
        if (_rand.NextDouble() < probability)
        {
            Console.WriteLine("[Sim] Simulated node failure: message dropped or node unavailable.");
            return true;
        }
        return false;
    }

    // Simulate a random processing delay (in ms)
    public static void SimulateRandomDelay(int minMs = 1000, int maxMs = 5000)
    {
        int delay = _rand.Next(minMs, maxMs + 1);
        Console.WriteLine($"[Sim] Simulating processing delay: {delay} ms");
        System.Threading.Thread.Sleep(delay);
    }

    // Simulate skipping a clock update (returns true if update should be skipped)
    public static bool SimulateClockInconsistency(double probability = 0.1)
    {
        if (_rand.NextDouble() < probability)
        {
            Console.WriteLine("[Sim] Simulated clock inconsistency: skipping clock update.");
            return true;
        }
        return false;
    }
}