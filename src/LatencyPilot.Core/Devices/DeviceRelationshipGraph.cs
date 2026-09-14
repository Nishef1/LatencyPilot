namespace LatencyPilot.Core.Devices;

public sealed class DeviceRelationshipGraph
{
    private const int MaximumTraversalDepth = 64;
    private readonly Dictionary<string, PnPDeviceSnapshot> devicesById;

    public DeviceRelationshipGraph(DeviceInventorySnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        devicesById = inventory.Devices.ToDictionary(
            static device => device.InstanceId,
            StringComparer.OrdinalIgnoreCase);
    }

    public PnPDeviceSnapshot? TryGetDevice(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return devicesById.TryGetValue(instanceId, out var device) ? device : null;
    }

    public IReadOnlyList<PnPDeviceSnapshot> GetKnownAncestors(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var ancestors = new List<PnPDeviceSnapshot>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { instanceId };
        var current = TryGetDevice(instanceId);

        for (var depth = 0; depth < MaximumTraversalDepth && current is not null; depth++)
        {
            if (current.Parent.ReadStatus != DeviceParentReadStatus.Available ||
                string.IsNullOrWhiteSpace(current.Parent.ParentInstanceId) ||
                !visited.Add(current.Parent.ParentInstanceId))
            {
                break;
            }

            if (!devicesById.TryGetValue(current.Parent.ParentInstanceId, out var parent))
            {
                break;
            }

            ancestors.Add(parent);
            current = parent;
        }

        return ancestors;
    }

    public PnPDeviceSnapshot? FindNearestKnownCommonAncestor(
        string firstInstanceId,
        string secondInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondInstanceId);

        var secondAncestors = GetKnownAncestors(secondInstanceId)
            .ToDictionary(static device => device.InstanceId, StringComparer.OrdinalIgnoreCase);

        return GetKnownAncestors(firstInstanceId)
            .FirstOrDefault(ancestor => secondAncestors.ContainsKey(ancestor.InstanceId));
    }

    public bool ShareKnownAncestor(string firstInstanceId, string secondInstanceId) =>
        FindNearestKnownCommonAncestor(firstInstanceId, secondInstanceId) is not null;
}
