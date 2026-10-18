namespace SmartX.Api.Services;

using SmartX.Api.Models;

/// <summary>
/// A node of the live deployment hierarchy (Facility > Zone > Sub-Zone > Device) with recursively
/// aggregated reliability indices. Degraded state bubbles upward from any descendant.
/// </summary>
public record LiveTopologyNode(
    string NodeId,
    string Name,
    string Kind,
    string? MacAddress,
    bool IsActive,
    bool IsDegraded,
    int TotalDevices,
    int OnlineDevices,
    int ActiveAlerts,
    double? AvailabilityPercent,
    double? PacketDeliveryPercent,
    List<LiveTopologyNode> SubNodes
);

public record DeviceTopologyInput(SensorDevice Device, DeviceConnectionStatus Status, int ActiveAlerts);

public static class TopologyEngine
{
    public const string PathSeparator = ">";

    public static (bool IsValid, string Reason) ValidateHierarchy(DeploymentNode node, HashSet<string> visitedIds, List<string>? auditTrail = null, int depth = 0)
    {
        if (string.IsNullOrWhiteSpace(node.NodeId))
            return Fail(auditTrail, "Validation failed: Node contains an empty identifier.");

        if (!visitedIds.Add(node.NodeId))
            return Fail(auditTrail, $"Validation failed: Cyclic loop or duplicate node detected at ID '{node.NodeId}'.");

        if (!node.IsActive)
            return Fail(auditTrail, $"Validation failed: Node '{node.NodeId}' ({node.Name}) is flagged inactive.");

        auditTrail?.Add($"[RECURSION] Depth {depth}: Node '{node.NodeId}' ({node.Name}) OK - active, {node.SubNodes.Count} sub-node(s)");

        foreach (var child in node.SubNodes)
        {
            var (isValid, reason) = ValidateHierarchy(child, visitedIds, auditTrail, depth + 1);
            if (!isValid) return (false, reason);
        }

        return (true, "Topology hierarchy validated successfully.");
    }

    private static (bool, string) Fail(List<string>? auditTrail, string reason)
    {
        auditTrail?.Add($"[FAILED] {reason}");
        return (false, reason);
    }

    /// <summary>
    /// Builds the live hierarchy from each device's "Facility > Zone > ... > Node" deployment path.
    /// The final path segment names the device itself; preceding segments become aggregator nodes.
    /// </summary>
    public static LiveTopologyNode BuildLiveTopology(IEnumerable<DeviceTopologyInput> devices)
    {
        var root = new GroupBuilder("Smart-X Gateway", "gateway");

        foreach (var input in devices)
        {
            var segments = input.Device.DeploymentLocation
                .Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var leafName = segments.Count > 0 ? segments[^1] : input.Device.MacAddress;
            var groupPath = segments.Count > 1 ? segments[..^1] : new List<string> { "Unassigned" };

            var current = root;
            foreach (var segment in groupPath)
            {
                current = current.GetOrAddChild(segment);
            }
            current.Devices.Add((leafName, input));
        }

        // A single facility becomes the root itself; otherwise the gateway groups all facilities
        var effectiveRoot = root.Children.Count == 1 && root.Devices.Count == 0 ? root.Children.Values.First() : root;
        return Aggregate(effectiveRoot).Node;
    }

    /// <summary>
    /// Post-order recursive aggregation: a parent's indices are computed from its children's.
    /// </summary>
    private static (LiveTopologyNode Node, double DeliverySum, int DeliveryCount) Aggregate(GroupBuilder group)
    {
        var subNodes = new List<LiveTopologyNode>();
        int total = 0, online = 0, alerts = 0, deliveryCount = 0;
        double deliverySum = 0;
        bool degraded = false;

        foreach (var child in group.Children.Values)
        {
            var (node, childDeliverySum, childDeliveryCount) = Aggregate(child);
            subNodes.Add(node);
            total += node.TotalDevices;
            online += node.OnlineDevices;
            alerts += node.ActiveAlerts;
            deliverySum += childDeliverySum;
            deliveryCount += childDeliveryCount;
            degraded |= node.IsDegraded;
        }

        foreach (var (leafName, input) in group.Devices.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            bool isOnline = input.Status.IsOnline;
            bool deviceDegraded = !isOnline || input.ActiveAlerts > 0;
            subNodes.Add(new LiveTopologyNode(
                NodeId: input.Device.MacAddress,
                Name: leafName,
                Kind: "device",
                MacAddress: input.Device.MacAddress,
                IsActive: isOnline,
                IsDegraded: deviceDegraded,
                TotalDevices: 1,
                OnlineDevices: isOnline ? 1 : 0,
                ActiveAlerts: input.ActiveAlerts,
                AvailabilityPercent: isOnline ? 100 : 0,
                PacketDeliveryPercent: input.Status.PacketDeliveryPercent,
                SubNodes: new()));

            total++;
            if (isOnline) online++;
            alerts += input.ActiveAlerts;
            degraded |= deviceDegraded;
            if (input.Status.PacketDeliveryPercent is double delivery)
            {
                deliverySum += delivery;
                deliveryCount++;
            }
        }

        var groupNode = new LiveTopologyNode(
            NodeId: group.PathId,
            Name: group.Name,
            Kind: group.PathId == "gateway" ? "gateway" : group.PathId.Contains(PathSeparator) ? "zone" : "facility",
            MacAddress: null,
            IsActive: true,
            IsDegraded: degraded,
            TotalDevices: total,
            OnlineDevices: online,
            ActiveAlerts: alerts,
            AvailabilityPercent: total == 0 ? null : Math.Round(online * 100.0 / total, 1),
            PacketDeliveryPercent: deliveryCount == 0 ? null : Math.Round(deliverySum / deliveryCount, 1),
            SubNodes: subNodes);

        return (groupNode, deliverySum, deliveryCount);
    }

    /// <summary>
    /// Converts the live hierarchy into the DeploymentNode shape consumed by <see cref="ValidateHierarchy"/>.
    /// </summary>
    public static DeploymentNode ToDeploymentNode(LiveTopologyNode node) => new()
    {
        NodeId = node.NodeId,
        Name = node.Name,
        IsActive = node.IsActive,
        SubNodes = node.SubNodes.Select(ToDeploymentNode).ToList()
    };

    private sealed class GroupBuilder(string name, string pathId)
    {
        public string Name { get; } = name;
        public string PathId { get; } = pathId;
        public Dictionary<string, GroupBuilder> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Name, DeviceTopologyInput Input)> Devices { get; } = new();

        public GroupBuilder GetOrAddChild(string segment)
        {
            if (!Children.TryGetValue(segment, out var child))
            {
                var childPath = PathId == "gateway" ? segment : $"{PathId} {PathSeparator} {segment}";
                child = new GroupBuilder(segment, childPath);
                Children[segment] = child;
            }
            return child;
        }
    }
}
