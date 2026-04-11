This directory contains plugins that are used by tests.
They are recompiled using the current build version of the repository.

# CKli.CommandSample.Plugin
This one is a pure test plugin. It contains a primary plugin that depends on a secondary one.
```csharp
public sealed class CommandSamplePlugin : PluginBase
{
    readonly SupportWithCommand _tool;

    public CommandSamplePlugin( PrimaryPluginContext context, SupportWithCommand tool )
        : base( context )
    {
        Throw.CheckArgument( tool != null && tool.World == World );
        _tool = tool;
    }

    //...
}
```
Both plugins handles commands.

# CKli.VSSolutionSample.Plugin
This one was a copy of a "real" `CKli.VSSolution.Plugin` that doesn't exist anymore.
It tests "ckli issue" command and the `Microsoft.VisualStudio.SolutionPersistence` package (we
don't use this anymore, we always use .slnx and directly works with the xml).

