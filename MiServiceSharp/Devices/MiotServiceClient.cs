using MiServiceSharp.Models;

namespace MiServiceSharp.Devices;

public sealed class MiotServiceClient
{
    internal MiotServiceClient(MiioDeviceClient device, MiotSpecService definition)
    {
        Definition = definition;
        Iid = definition.Iid;
        Properties = definition.Properties.Select(property => new MiotPropertyClient(device, Iid, property)).ToArray();
        Actions = definition.Actions.Select(action => new MiotActionClient(device, definition, action)).ToArray();
    }

    public int Iid { get; }
    public string Name => Definition.Name;
    public MiotSpecService Definition { get; }
    public IReadOnlyList<MiotPropertyClient> Properties { get; }
    public IReadOnlyList<MiotActionClient> Actions { get; }

    public MiotPropertyClient GetProperty(string identifier)
    {
        var matches = Properties.Where(item => MiotSpecType.Matches(item.Definition.Type, item.Iid, identifier)).ToArray();
        if (matches.Length != 1)
        {
            var available = string.Join(", ", Properties.Select(item => $"{item.Iid}={item.Name}"));
            throw new ArgumentException($"Property '{identifier}' matched {matches.Length} properties on {Name}. Available properties are {available}.", nameof(identifier));
        }
        return matches[0];
    }

    public MiotActionClient GetAction(string identifier)
    {
        var matches = Actions.Where(item => MiotSpecType.Matches(item.Definition.Type, item.Iid, identifier)).ToArray();
        if (matches.Length != 1)
        {
            var available = string.Join(", ", Actions.Select(item => $"{item.Iid}={item.Name}"));
            throw new ArgumentException($"Action '{identifier}' matched {matches.Length} actions on {Name}. Available actions are {available}.", nameof(identifier));
        }
        return matches[0];
    }
}
