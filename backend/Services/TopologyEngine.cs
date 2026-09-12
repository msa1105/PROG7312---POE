namespace SmartX.Api.Services;

using SmartX.Api.Models;

public static class TopologyEngine
{
    public static (bool IsValid, string Reason) ValidateHierarchy(DeploymentNode node, HashSet<string> visitedIds)
    {
        if (string.IsNullOrWhiteSpace(node.NodeId))
            return (false, "Validation failed: Node contains an empty identifier.");

        if (!visitedIds.Add(node.NodeId))
            return (false, $"Validation failed: Cyclic loop or duplicate node detected at ID '{node.NodeId}'.");

        if (!node.IsActive)
            return (false, $"Validation failed: Node '{node.NodeId}' ({node.Name}) is flagged inactive.");

        foreach (var child in node.SubNodes)
        {
            var (isValid, reason) = ValidateHierarchy(child, visitedIds);
            if (!isValid) return (false, reason);
        }

        return (true, "Topology hierarchy validated successfully.");
    }
}
