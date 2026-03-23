using CK.Core;
using CKli.Core;
using CKli.Core.Completion;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli;

public static partial class CKliCommands
{
    /// <summary>
    /// Console entry point: the suggestions are returned on <see cref="Console.Out"/>.
    /// </summary>
    /// <param name="arguments">The arguments to complete.</param>
    public static void HandleCompletion( ReadOnlySpan<string> arguments )
    {
        var o = (StreamWriter)Console.Out;
        o.AutoFlush = false;
        foreach( var s in GetCompletionSuggestions( arguments ) )
        {
            o.Write( s.Completion );
            o.Write( '\t' );
            o.Write( FirstLine( s.Description ) );
            o.Write( '\t' );
            o.WriteLine( s.Type );
        }
        o.Flush();

        static ReadOnlySpan<char> FirstLine( ReadOnlySpan<char> s )
        {
            var idx = s.IndexOfAny( '\r', '\n' );
            return idx >= 0 ? s[..idx] : s;
        }
    }

    /// <summary>
    /// Generates the completions from the provided <paramref name="arguments"/>.
    /// This is an empty set if completions are not available.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The computed completions.</returns>
    public static IEnumerable<(string Completion, string Description, string Type)> GetCompletionSuggestions( ReadOnlySpan<string> arguments )
    {
        if( FindCompletionFile( out var f ) )
        {
            using( var s = File.OpenRead( f ) )
            using( var r = new CKBinaryReader( s ) )
            {
                // TODO: ReflectionPluginCollector.Factory.GenerateCode() must generate
                //       the global file (at GetCompletionFilePath()) and the world file
                //       at GetCompletionFilePath( NormalizedPath stackWorkingFolder, string? ltsName ).
                //
                // The file is read and GetCompletions does the job.
                var d = new CompletionData( r );
                return d.GetCompletions( arguments );
            }
        }
        return [];

        static bool FindCompletionFile( out NormalizedPath f )
        {
            NormalizedPath currentDirectory = Environment.CurrentDirectory;
            // Are we in a Stack?
            var stackPath = StackRepository.FindGitStackPath( currentDirectory );
            if( stackPath.Parts.Count >= 2 )
            {
                // Are we in a LTS folder?
                Throw.DebugAssert( stackPath.LastPart == StackRepository.PublicStackName || stackPath.LastPart == StackRepository.PrivateStackName );
                if( currentDirectory.Parts.Count > stackPath.Parts.Count - 1 && WorldName.IsValidLTSName( stackPath.Parts[^2] ) )
                {
                    var ltsName = currentDirectory.Parts[stackPath.Parts.Count - 1];
                    if( WorldName.IsValidLTSName( ltsName ) )
                    {
                        // If a completion file exists for this world full name, it's the one.
                        f = GetCompletionFilePath( stackPath, ltsName );
                        if( File.Exists( f ) ) return true;
                    }
                }
                // If a completion file exists for the default world, it's the one.
                f = GetCompletionFilePath( stackPath, null );
                if( File.Exists( f ) ) return true;
            }
            // Use the global "out of stack" completion file.
            f = GetCompletionFilePath();
            return File.Exists( f );
        }

    }

    /// <summary>
    /// Gets the path of the global completion file with only the intrinsic commands.
    /// </summary>
    /// <returns></returns>
    public static NormalizedPath GetCompletionFilePath()
    {
        return CKliRootEnv.GetAppLocalDataPath( null ).AppendPart( "ckli.completion" );
    }

    /// <summary>
    /// Gets the path of the completion file for the default world or for a given LTS name.
    /// The file may not exist.
    /// </summary>
    /// <param name="stackWorkingFolder">The stack "/.PrivateStack" or ".PublicSTack" path.</param>
    /// <param name="ltsName">Optional Long Term Support name.</param>
    /// <returns>The path to the completion file.</returns>
    public static NormalizedPath GetCompletionFilePath( NormalizedPath stackWorkingFolder, string? ltsName )
    {
        Throw.CheckArgument( stackWorkingFolder.Parts.Count > 2
                             && (stackWorkingFolder.LastPart == StackRepository.PublicStackName || stackWorkingFolder.LastPart == StackRepository.PrivateStackName) );
        string stackName = stackWorkingFolder.Parts[^2].ToLowerInvariant();
        return ltsName == null
                ? stackWorkingFolder.AppendPart( $"{stackName}.completion" )
                : stackWorkingFolder.AppendPart( ltsName.ToLowerInvariant() ).AppendPart( $"{stackName}{ltsName}.completion" );
    }

}
