using SmartX.Api.Models;

namespace SmartX.Api.Services;

public record CommandRequest(string MacAddress, string DeviceName, bool TargetState, bool? PreviousState);

public record CommandStackSummary(int Depth, CommandBatch? Top);

/// <summary>
/// Manual override command pipeline.
/// - Dictionary&lt;mac, Queue&lt;DeviceCommand&gt;&gt;: one FIFO outbox per device, so commands reach a device in issue order.
/// - Dictionary&lt;Guid, DeviceCommand&gt;: O(1) lookup when a device acknowledges a command by id.
/// - Stack&lt;CommandBatch&gt;: LIFO undo history; Undo pops the most recent operator action and issues its inverse.
/// Reference: Stack<T> Class (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.stack-1
/// Reference: Queue<T> Class (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.queue-1
/// Reference: Dictionary<TKey,TValue> Class (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2
/// </summary>
public class CommandDispatcher
{
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private const int MaxHistory = 200;

    private readonly Dictionary<string, Queue<DeviceCommand>> _outboxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, DeviceCommand> _commandsById = new();
    private readonly Stack<CommandBatch> _undoStack = new();
    private readonly List<CommandBatch> _history = new();
    private readonly object _lock = new();

    /// <summary>Raised after any command or batch changes state (used for real-time push).</summary>
    public event Action? Changed;

    public CommandBatch Issue(string label, CommandSource source, IEnumerable<CommandRequest> requests, string issuedBy = "Operator-Gateway", Guid? revertsBatchId = null)
    {
        var batchId = Guid.NewGuid();
        var commands = requests.Select(r => new DeviceCommand
        {
            BatchId = batchId,
            MacAddress = r.MacAddress,
            DeviceName = r.DeviceName,
            TargetState = r.TargetState,
            PreviousState = r.PreviousState,
            Source = source,
            IssuedBy = issuedBy
        }).ToList();

        if (commands.Count == 0)
            throw new ArgumentException("A command batch must address at least one device.", nameof(requests));

        var batch = new CommandBatch { Id = batchId, Label = label, Source = source, Commands = commands, RevertsBatchId = revertsBatchId };

        lock (_lock)
        {
            foreach (var command in commands)
            {
                if (!_outboxes.TryGetValue(command.MacAddress, out var outbox))
                {
                    outbox = new Queue<DeviceCommand>();
                    _outboxes[command.MacAddress] = outbox;
                }
                outbox.Enqueue(command);
                _commandsById[command.Id] = command;
            }

            // Undo batches are not themselves undoable; every other action becomes the new top of the stack
            if (source != CommandSource.Undo)
            {
                _undoStack.Push(batch);
            }

            _history.Add(batch);
            TrimHistory();
        }

        Changed?.Invoke();
        return batch;
    }

    /// <summary>
    /// Pops the most recent (non-reverted) batch and issues the inverse commands, restoring each device's previous state.
    /// Returns null when there is nothing to undo.
    /// </summary>
    public (CommandBatch Reverted, CommandBatch UndoBatch)? Undo(string issuedBy = "Operator-Gateway")
    {
        CommandBatch? target = null;
        List<CommandRequest> inverse = new();

        lock (_lock)
        {
            while (_undoStack.Count > 0)
            {
                var candidate = _undoStack.Pop();
                if (candidate.IsReverted) continue;

                inverse = candidate.Commands
                    .Where(c => c.PreviousState.HasValue && c.PreviousState.Value != c.TargetState)
                    .Select(c => new CommandRequest(c.MacAddress, c.DeviceName, c.PreviousState!.Value, c.TargetState))
                    .ToList();

                candidate.IsReverted = true;
                candidate.RevertedAt = DateTime.UtcNow;
                target = candidate;
                break;
            }
        }

        if (target == null) return null;

        if (inverse.Count == 0)
        {
            // Nothing changed state (e.g. the device was already in the requested state) - record a no-op undo
            Changed?.Invoke();
            return (target, new CommandBatch { Label = $"Undo: {target.Label} (no state change)", Source = CommandSource.Undo, RevertsBatchId = target.Id });
        }

        var undo = Issue($"Undo: {target.Label}", CommandSource.Undo, inverse, issuedBy, target.Id);
        return (target, undo);
    }

    /// <summary>
    /// Device-side poll: dequeues pending commands from the device's FIFO outbox and marks them Delivered.
    /// Expired commands are dropped instead of being delivered late.
    /// </summary>
    public List<DeviceCommand> DequeueForDevice(string macAddress, int maxCommands = 10)
    {
        var delivered = new List<DeviceCommand>();
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            if (!_outboxes.TryGetValue(macAddress, out var outbox)) return delivered;

            while (outbox.Count > 0 && delivered.Count < maxCommands)
            {
                var command = outbox.Dequeue();
                if (command.Status != CommandStatus.Queued) continue;

                if (now - command.IssuedAt > CommandTimeout)
                {
                    command.Status = CommandStatus.Expired;
                    command.CompletedAt = now;
                    continue;
                }

                command.Status = CommandStatus.Delivered;
                command.DeliveredAt = now;
                delivered.Add(command);
            }
        }

        if (delivered.Count > 0) Changed?.Invoke();
        return delivered;
    }

    /// <summary>Device acknowledgement of an applied (or failed) command.</summary>
    public DeviceCommand? Acknowledge(string macAddress, Guid commandId, bool success, bool? appliedState, string? message)
    {
        DeviceCommand? command;
        lock (_lock)
        {
            if (!_commandsById.TryGetValue(commandId, out command)) return null;
            if (!command.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase)) return null;
            if (!command.IsPending) return command;

            command.Status = success ? CommandStatus.Acknowledged : CommandStatus.Failed;
            command.CompletedAt = DateTime.UtcNow;
            command.AppliedState = appliedState;
            command.DeviceMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        }

        Changed?.Invoke();
        return command;
    }

    /// <summary>Marks commands that were never acknowledged within the timeout as Expired.</summary>
    public int ExpireStale(DateTime nowUtc)
    {
        int expired = 0;
        lock (_lock)
        {
            foreach (var command in _commandsById.Values)
            {
                if (command.IsPending && nowUtc - command.IssuedAt > CommandTimeout)
                {
                    command.Status = CommandStatus.Expired;
                    command.CompletedAt = nowUtc;
                    expired++;
                }
            }
        }

        if (expired > 0) Changed?.Invoke();
        return expired;
    }

    public DeviceCommand? GetCommand(Guid id)
    {
        lock (_lock) return _commandsById.TryGetValue(id, out var command) ? command : null;
    }

    /// <summary>
    /// Target state of the newest command still in flight for a device (null when none). Commands issued in quick
    /// succession must treat this as the device's state, because the device has not reported it yet.
    /// </summary>
    public bool? LatestPendingTarget(string macAddress)
    {
        lock (_lock)
        {
            return _outboxes.TryGetValue(macAddress, out var outbox)
                ? _commandsById.Values.Where(c => c.IsPending && c.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(c => c.IssuedAt).Select(c => (bool?)c.TargetState).FirstOrDefault()
                : null;
        }
    }

    /// <summary>The state a device is expected to be in: its latest in-flight target, otherwise what it last reported.</summary>
    public bool? ExpectedState(string macAddress, bool? reportedState) => LatestPendingTarget(macAddress) ?? reportedState;

    public int PendingCount(string macAddress)
    {
        lock (_lock)
        {
            return _outboxes.TryGetValue(macAddress, out var outbox) ? outbox.Count(c => c.IsPending) : 0;
        }
    }

    /// <summary>Most recent batches first.</summary>
    public List<CommandBatch> GetHistory(int limit = 50)
    {
        lock (_lock)
        {
            return Enumerable.Reverse(_history).Take(limit).ToList();
        }
    }

    public CommandStackSummary GetUndoStack()
    {
        lock (_lock)
        {
            var top = _undoStack.FirstOrDefault(b => !b.IsReverted);
            return new CommandStackSummary(_undoStack.Count(b => !b.IsReverted), top);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _outboxes.Clear();
            _commandsById.Clear();
            _undoStack.Clear();
            _history.Clear();
        }
        Changed?.Invoke();
    }

    private void TrimHistory()
    {
        if (_history.Count <= MaxHistory) return;

        var removed = _history.Take(_history.Count - MaxHistory).ToList();
        _history.RemoveRange(0, removed.Count);
        foreach (var command in removed.SelectMany(b => b.Commands).Where(c => !c.IsPending))
        {
            _commandsById.Remove(command.Id);
        }
    }
}
