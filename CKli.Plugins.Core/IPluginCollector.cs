namespace CKli.Core;

public interface IPluginCollector
{
    void AddPrimaryPlugin<T>() where T : WorldPlugin;

    void AddSupportPlugin<T>() where T : WorldPlugin;

    IWorldPlugins Build();
}

