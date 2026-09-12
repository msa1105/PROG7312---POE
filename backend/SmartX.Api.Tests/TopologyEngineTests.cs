using SmartX.Api.Models;
using SmartX.Api.Services;
using Xunit;

namespace SmartX.Api.Tests;

public class TopologyEngineTests
{
    [Fact]
    public void ValidateHierarchy_ValidTree_ReturnsTrue()
    {
        var root = new DeploymentNode
        {
            NodeId = "root",
            SubNodes = new List<DeploymentNode>
            {
                new DeploymentNode { NodeId = "child1" },
                new DeploymentNode { NodeId = "child2" }
            }
        };

        var (isValid, reason) = TopologyEngine.ValidateHierarchy(root, new HashSet<string>());

        Assert.True(isValid);
        Assert.Equal("Topology hierarchy validated successfully.", reason);
    }

    [Fact]
    public void ValidateHierarchy_EmptyId_ReturnsFalse()
    {
        var root = new DeploymentNode { NodeId = "" };

        var (isValid, reason) = TopologyEngine.ValidateHierarchy(root, new HashSet<string>());

        Assert.False(isValid);
        Assert.Contains("empty identifier", reason);
    }

    [Fact]
    public void ValidateHierarchy_InactiveNode_ReturnsFalse()
    {
        var root = new DeploymentNode { NodeId = "root", IsActive = false };

        var (isValid, reason) = TopologyEngine.ValidateHierarchy(root, new HashSet<string>());

        Assert.False(isValid);
        Assert.Contains("flagged inactive", reason);
    }

    [Fact]
    public void ValidateHierarchy_CyclicDependency_ReturnsFalse()
    {
        var child = new DeploymentNode { NodeId = "child" };
        var root = new DeploymentNode
        {
            NodeId = "root",
            SubNodes = new List<DeploymentNode> { child }
        };
        // Create cycle
        child.SubNodes.Add(root);

        var (isValid, reason) = TopologyEngine.ValidateHierarchy(root, new HashSet<string>());

        Assert.False(isValid);
        Assert.Contains("Cyclic loop", reason);
    }
}
