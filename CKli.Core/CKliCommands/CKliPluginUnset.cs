using CK.Core;
using CKli.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPluginUnset : Command
{
    public CKliPluginUnset()
        : base( null,
                "plugin unset",
                """
                Clears a plugin configuration: the attribute is removed and the plugin's default value applies again.
                The attribute name can be prefixed by the plugin short name ("VersionTag.RemoveUselessFakeTag"):
                this long form is required only when more than one plugin supports the same attribute name.
                Supported attributes are listed by "ckli plugin info".
                """,
                [
                    ("name", """The plugin attribute name to unset.""")
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
        if( !cmdLine.Close( monitor ) ) return ValueTask.FromResult( false );
        return new ValueTask<bool>( CKliPluginSet.SetOrUnsetAsync( monitor, this, context, attributeName, null, scopeAlive ) );
    }

}
