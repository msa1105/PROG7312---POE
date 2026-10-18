using SmartX.Api.Models;
using SmartX.Api.Services;
using Xunit;

namespace SmartX.Api.Tests;

public class LiveTopologyTests
{
    private static DeviceTopologyInput Input(string mac, string location, bool online, int alerts = 0, double? delivery = 100) =>
        new(new SensorDevice { MacAddress = mac, DeploymentLocation = location },
            new DeviceConnectionStatus(online, online ? 0.5 : 12, 3, 10, delivery),
            alerts);

    [Fact]
    public void BuildLiveTopology_BubblesDegradedStateUpward_AndAggregatesIndices()
    {
        var root = TopologyEngine.BuildLiveTopology(new[]
        {
            Input("AA:00:00:00:00:01", "Facility Alpha > Greenhouse 1 > Rack A", online: true, delivery: 100),
            Input("AA:00:00:00:00:02", "Facility Alpha > Greenhouse 1 > Rack B", online: false, delivery: 50),
            Input("AA:00:00:00:00:03", "Facility Alpha > Substation", online: true, alerts: 1, delivery: 90),
        });

        Assert.Equal("Facility Alpha", root.Name);
        Assert.Equal("facility", root.Kind);
        Assert.True(root.IsDegraded);
        Assert.Equal(3, root.TotalDevices);
        Assert.Equal(2, root.OnlineDevices);
        Assert.Equal(1, root.ActiveAlerts);
        Assert.Equal(80.0, root.PacketDeliveryPercent);

        var greenhouse = root.SubNodes.Single(n => n.Name == "Greenhouse 1");
        Assert.Equal("zone", greenhouse.Kind);
        Assert.Equal("Facility Alpha > Greenhouse 1", greenhouse.NodeId);
        Assert.True(greenhouse.IsDegraded);
        Assert.Equal(50.0, greenhouse.AvailabilityPercent);
        Assert.Equal(75.0, greenhouse.PacketDeliveryPercent);

        var rackA = greenhouse.SubNodes.Single(n => n.Name == "Rack A");
        Assert.Equal("device", rackA.Kind);
        Assert.False(rackA.IsDegraded);

        var substation = root.SubNodes.Single(n => n.Name == "Substation");
        Assert.Equal("device", substation.Kind);
        Assert.True(substation.IsDegraded); // unacknowledged alert keeps the node degraded
    }

    [Fact]
    public void BuildLiveTopology_MultipleFacilities_AreGroupedUnderGateway()
    {
        var root = TopologyEngine.BuildLiveTopology(new[]
        {
            Input("AA:00:00:00:00:01", "Facility Alpha > Zone 1 > Node", online: true),
            Input("AA:00:00:00:00:02", "Facility Beta > Zone 1 > Node", online: true),
        });

        Assert.Equal("gateway", root.Kind);
        Assert.All(root.SubNodes, f => Assert.Equal("facility", f.Kind));
        Assert.False(root.IsDegraded);
    }

    [Fact]
    public void LiveTopology_ValidationFailsOnOfflineNode_WithAuditTrail()
    {
        var root = TopologyEngine.BuildLiveTopology(new[]
        {
            Input("AA:00:00:00:00:01", "Facility Alpha > Zone 1 > Node A", online: true),
            Input("AA:00:00:00:00:02", "Facility Alpha > Zone 1 > Node B", online: false),
        });

        var trail = new List<string>();
        var (isValid, reason) = TopologyEngine.ValidateHierarchy(TopologyEngine.ToDeploymentNode(root), new HashSet<string>(), trail);

        Assert.False(isValid);
        Assert.Contains("AA:00:00:00:00:02", reason);
        Assert.StartsWith("[RECURSION] Depth 0", trail[0]);
        Assert.StartsWith("[FAILED]", trail[^1]);
    }
}
