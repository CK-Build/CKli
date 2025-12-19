using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Adds or updates a plugin. This reloads the plugins (and depending on the <see cref="WorldDefinitionFile.CompileMode"/>
/// recompiles them).
/// <para>
/// This command is public: primary plugin constructors and their <see cref="PluginBase.Initialize(IActivityMonitor)"/> method
/// can observe a non null <see cref="PrimaryPluginContext.Command"/> during the reload steps.
/// </para>
/// </summary>
public sealed class CKliPluginAdd : Command
{
    internal CKliPluginAdd()
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
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && PluginAdd( monitor, this, context, package, allowLTS ) );
    }

    static bool PluginAdd( IActivityMonitor monitor,
                           Command command,
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
            if( !allowLTS && !world.Name.IsDefaultWorld )
            {
                return CKliRepoAdd.RequiresAllowLTS( monitor, world.Name );
            }
            world.SetExecutingCommand( command );
            // AddOrSetPluginPackage handles the WorldDefinition file save and commit.
            return world.AddOrSetPluginPackage( monitor, package.PackageId, package.Version );
        }
        finally
        {
            stack.Dispose();
        }
    }



}
