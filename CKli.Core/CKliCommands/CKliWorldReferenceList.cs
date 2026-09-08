using CK.Core;
using CKli.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliWorldReferenceList : Command
{
    public CKliWorldReferenceList()
        : base( null,
                "world reference list",
                """
                Lists the <Reference /> elements of the current world: the other Stacks that this world uses
                and where they are cloned on this machine.
                """,
                [], [], [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        return ValueTask.FromResult( cmdLine.Close( monitor ) && ListReferences( monitor, context ) );
    }

    static bool ListReferences( IActivityMonitor monitor, CKliEnv context )
    {
        if( !StackRepository.OpenFromPath( monitor, context, out var stack, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            var worldName = stack.GetWorldNameFromPath( monitor, context.CurrentDirectory );
            if( worldName == null )
            {
                return false;
            }
            var definitionFile = worldName.LoadDefinitionFile( monitor );
            if( definitionFile == null )
            {
                return false;
            }
            var s = context.Screen.ScreenType;
            var references = definitionFile.References;
            if( references.Count == 0 )
            {
                context.Screen.Display( s.Text( $"World '{worldName.FullName}' has no <Reference />." ) );
                return true;
            }
            var rows = s.Unit;
            foreach( var e in references )
            {
                var sUrl = e.Attribute( XNames.Url )?.Value;
                bool isValidUrl = sUrl != null && Uri.TryCreate( sUrl, UriKind.Absolute, out var url );
                IRenderable cUrl = sUrl == null
                                    ? s.Text( """(missing Url="..." attribute)""", ConsoleColor.Red )
                                    : isValidUrl
                                        ? s.Text( sUrl, ConsoleColor.Blue ).HyperLink( new Uri( sUrl ) )
                                        : s.Text( sUrl, ConsoleColor.Red );
                // The attributes have been validated when the definition file has been loaded.
                bool defaultClone = (bool?)e.Attribute( XNames.DefaultClone ) is not false;
                bool isPrivate = (bool?)e.Attribute( XNames.Private ) is true;
                // LTSName has no default value: when absent, the referenced Stack's default world is used.
                var ltsName = e.Attribute( XNames.LTSName )?.Value;
                var where = isValidUrl
                                ? StackRepository.FindExistingStacks( monitor, new Uri( sUrl! ) )
                                : [];
                rows = rows.AddBelow( cUrl.Box( marginRight: 1 )
                                          .AddRight( s.Text( defaultClone ? "clone" : "no-clone",
                                                             defaultClone ? ConsoleColor.DarkGreen : ConsoleColor.DarkGray )
                                                      .Box( marginRight: 1 ),
                                                     s.Text( isPrivate ? "private" : "public",
                                                             isPrivate ? ConsoleColor.DarkYellow : ConsoleColor.DarkGray )
                                                      .Box( marginRight: 1 ),
                                                     s.Text( ltsName ?? "(default world)",
                                                             ltsName != null ? ConsoleColor.DarkCyan : ConsoleColor.DarkGray )
                                                      .Box( marginRight: 1 ),
                                                     s.Text( where.Count > 0
                                                                ? where[0].RemoveLastPart().Path
                                                                : "(not cloned here)",
                                                             ConsoleColor.DarkGray ) ) );
            }
            context.Screen.Display( s.Text( $"{references.Count} reference(s) in world '{worldName.FullName}':" )
                                     .AddBelow( rows.TableLayout() ) );
            return true;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
