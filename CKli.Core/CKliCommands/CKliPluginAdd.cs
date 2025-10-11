using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPluginAdd : Command
{
    public CKliPluginAdd()
        : base( null,
                "plugin add",
                "Adds a new plugin (or sets the version of an existing one) in the current World's plugins.",
                [("package", """Package "Name@Version" or ""CKli.Name.Plugin@Version" to add. CKli plugin packages are normalized to "CKli.XXX.Plugin".""")],
                [],
                [
                    (["--allow-lts"], "Allows the current world to be a Long Term Support world.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        string sPackage = cmdLine.EatArgument();
        if( !PackageInstance.TryParse( sPackage, out var package ) )
        {
            monitor.Error( $"""Invalid <package> argument '{sPackage}'. It must be like "name@version".""" );
            return ValueTask.FromResult( false );
        }
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        return ValueTask.FromResult( cmdLine.CheckNoRemainingArguments( monitor )
                                     && PluginAdd( monitor, context, package, allowLTS ) );
    }

    static bool PluginAdd( IActivityMonitor monitor,
                           CKliEnv context,
                           PackageInstance package,
                           bool allowLTS )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            if( world.DefinitionFile.IsPluginsDisabled )
            {
                return CKliPluginCreate.RequiresEnabledPlugins( monitor );
            }
            if( !allowLTS && !world.Name.IsDefaultWorld )
            {
                return CKliRepoAdd.RequiresAllowLTS( monitor, world.Name );
            }
            return world.AddOrSetPluginPackage( monitor, package.PackageId, package.Version );
        }
        finally
        {
            stack.Dispose();
        }
    }



}
