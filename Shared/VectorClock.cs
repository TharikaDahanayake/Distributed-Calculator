public class VectorClock
{
    public string NodeId { get; }
    public Dictionary<string, int> Clock { get; private set; }
    private Stack<Dictionary<string, int>> _history = new();

    // Optionally initialize with all node IDs
    public VectorClock(string nodeId, IEnumerable<string>? allNodeIds = null)
    {
        NodeId = nodeId;
        if (allNodeIds != null)
            Clock = allNodeIds.ToDictionary(id => id, id => id == nodeId ? 0 : 0);
        else
            Clock = new() { [NodeId] = 0 };
    }

    public void Increment()
    {
        SaveState();
        if (!Clock.ContainsKey(NodeId))
            Clock[NodeId] = 0;
        Clock[NodeId]++;
    }

    public void Merge(Dictionary<string, int> incoming)
    {
        foreach (var kvp in incoming)
            Clock[kvp.Key] = Math.Max(Clock.GetValueOrDefault(kvp.Key, 0), kvp.Value);
    }

    public void Rollback()
    {
        if (_history.Count > 0)
        {
            Clock = _history.Pop();
            Console.WriteLine($"[VectorClock] Rollback occurred on node '{NodeId}'. New clock state: {{ {string.Join(", ", Clock.Select(kvp => kvp.Key + ":" + kvp.Value))} }}");
        }
    }
    private void SaveState()
    {
        _history.Push(Clock.ToDictionary(e => e.Key, elementSelector => elementSelector.Value));
    }

    public Dictionary<string, int> GetClockCopy() =>
        Clock.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    // Helper for gRPC proto integration (if needed)
    public void FromProto(IDictionary<string, int> proto)
    {
        foreach (var kvp in proto)
            Clock[kvp.Key] = kvp.Value;
    }

    public Dictionary<string, int> ToProto() => GetClockCopy();
}