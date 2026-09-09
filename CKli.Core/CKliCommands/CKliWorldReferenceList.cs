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
            foreach( var r in references )
            {
                IRenderable cUrl = r.RawUrl == null
                                    ? s.Text( """(missing Url="..." attribute)""", ConsoleColor.Red )
                                    : r.HasValidUrl
                                        ? s.Text( r.RawUrl, ConsoleColor.Blue ).HyperLink( r.Url )
                                        : s.Text( r.RawUrl, ConsoleColor.Red );
                var where = r.HasValidUrl
                                ? StackRepository.FindExistingStacks( monitor, r.Url )
                                : [];
                rows = rows.AddBelow( cUrl.Box( marginRight: 1 )
                                          .AddRight( s.Text( r.DefaultClone ? "clone" : "no-clone",
                                                             r.DefaultClone ? ConsoleColor.DarkGreen : ConsoleColor.DarkGray )
                                                      .Box( marginRight: 1 ),
                                                     s.Text( r.IsPrivate ? "private" : "public",
                                                             r.IsPrivate ? ConsoleColor.DarkYellow : ConsoleColor.DarkGray )
                                                      .Box( marginRight: 1 ),
                                                     // LTSName has no default value: when absent, the
                                                     // referenced Stack's default world is used.
                                                     s.Text( r.LTSName ?? "(default world)",
                                                             r.LTSName != null ? ConsoleColor.DarkCyan : ConsoleColor.DarkGray )
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
