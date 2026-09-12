namespace SmartX.Api.Models;

public class DeploymentNode
{
    public string NodeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<DeploymentNode> SubNodes { get; set; } = new();
}
