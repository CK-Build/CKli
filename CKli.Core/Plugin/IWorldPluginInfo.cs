namespace CKli.Core;

public interface IWorldPluginInfo
{
    string Name { get; }

    WorldPluginStatus Status { get; }
}

