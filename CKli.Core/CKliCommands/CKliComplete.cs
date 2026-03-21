using CK.Core;
using CKli.Core.Completion;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Core;

sealed class CKliComplete : Command
{
    internal CKliComplete()
        : base( null,
                "complete",
                "Returns completions for the given partial command line.",
                [],
                [],
                [] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        var tokens = cmdLine.EatRemainingArgument();
        // When HasHelp is true, the help token (--help/-h/-?/?) was stripped by CommandLineArguments.
        // For the complete command, it's a token being completed, not a help request: restore it.
        if( cmdLine.HasHelp && cmdLine.InitialArguments.Length > 0 )
        {
            tokens.Add( cmdLine.InitialArguments[^1] );
        }
        cmdLine.Close( monitor );

        try
        {
            ManifestData? manifest = null;
            var stackPath = CKliRootEnv.CurrentStackPath;
            if( !stackPath.IsEmptyPath )
            {
                manifest = TryLoadManifest( stackPath );
            }

            var data = new CompletionData( CKliCommands.Commands, manifest );
            var results = data.GetCompletions( tokens.ToArray() );

            foreach( var r in results )
            {
                var desc = r.Description.FirstLine();
                Console.Out.WriteLine( $"{r.Completion}\t{desc}\t{r.Type}" );
            }
        }
        catch( Exception ex )
        {
            Console.Error.WriteLine( $"ckli complete error: {ex.Message}" );
        }
        return ValueTask.FromResult( true );
    }

    static ManifestData? TryLoadManifest( NormalizedPath stackPath )
    {
        var localPath = stackPath.Combine( "$Local" );
        if( !Directory.Exists( localPath ) ) return null;

        foreach( var dir in Directory.EnumerateDirectories( localPath ) )
        {
            var candidate = Path.Combine( dir, "completion.tsv" );
            if( File.Exists( candidate ) )
            {
                try
                {
                    return CompletionManifest.Read( File.ReadAllText( candidate ) );
                }
                catch
                {
                    return null;
                }
            }
        }
        return null;
    }
}
