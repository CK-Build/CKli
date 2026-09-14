using CK.Core;
using CKli.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPluginSet : Command
{
    public CKliPluginSet()
        : base( null,
                "plugin set",
                """
                Configures a plugin attribute.
                The attribute name can be prefixed by the plugin short name ("VersionTag.RemoveUselessFakeTag"):
                this long form is required only when more than one plugin supports the same attribute name.
                Supported attributes are listed by "ckli plugin info".
                """,
                [
                    ("name", """The plugin attribute name to configure."""),
                    ("value", """The value to set. For a boolean attribute, this must be "true" or "false".""")
                ],
                [], [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        var attributeName = cmdLine.EatArgument();
        var attributeValue = cmdLine.EatArgument();
        if( !cmdLine.Close( monitor ) ) return ValueTask.FromResult( false );

        return new ValueTask<bool>( SetOrUnsetAsync( monitor, this, context, attributeName, attributeValue, scopeAlive ) );
    }

    internal static async Task<bool> SetOrUnsetAsync( IActivityMonitor monitor,
                                                      Command command,
                                                      CKliEnv context,
                                                      string attributeName,
                                                      string? attributeValue,
                                                      CancellationToken scopeAlive )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command, scopeAlive );
            // On success the World's DefinitionFile is dirty: Close saves and commits it.
            return await world.PluginSetAsync( monitor, attributeName, attributeValue ).ConfigureAwait( false )
                   && stack.Close( monitor );
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }
}
