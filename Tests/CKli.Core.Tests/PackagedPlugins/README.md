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
This one is a copy of the "real" `CKli.VSSolution.Plugin` that is used to test "ckli issue"
command and the new (as of 2025) `Microsoft.VisualStudio.SolutionPersistence` package.


