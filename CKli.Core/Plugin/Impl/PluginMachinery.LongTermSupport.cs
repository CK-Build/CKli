using CK.Core;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace CKli.Core;

public sealed partial class PluginMachinery
{
    /// <summary>
    /// Gets the value of the "ArtifactsPath" property of a world's plugin solution "Directory.Build.props": the
    /// build output must land in the world's <see cref="LocalWorldName.LocalDataFolder"/>, where the
    /// <see cref="RunFolder"/> is.
    /// <para>
    /// The plugin solution of a Long Term Support world is one folder deeper than the default world's one (it is
    /// in the "@ltsName/" folder of the Stack repository) and so is its local data folder ("$Local/@ltsName/").
    /// </para>
    /// </summary>
    /// <param name="worldName">The world name.</param>
    /// <returns>The ArtifactsPath value.</returns>
    internal static string GetArtifactsPath( LocalWorldName worldName )
    {
        var name = GetPluginSolutionName( worldName );
        return worldName.LTSName == null
                ? $"$(MSBuildThisFileDirectory)../$Local/{name}"
                : $"$(MSBuildThisFileDirectory)../../$Local/{worldName.LTSName}/{name}";
    }

    /// <summary>
    /// Snapshots the plugin solution of the <paramref name="source"/> world as the plugin solution of the new
    /// <paramref name="target"/> Long Term Support world: the plugins used during the life of a LTS world are
    /// the ones that were used when it has been created, not the ones the default world uses since.
    /// <para>
    /// The git ignored files (the generated "CKli.CompiledPlugins.cs" and "CKli.Version.props", the tests' outputs,
    /// etc.) are not copied: they are regenerated when the new world is opened. The ".slnx" file is renamed and the
    /// "ArtifactsPath" of the "Directory.Build.props" is redirected to the new world's local data folder.
    /// </para>
    /// <para>
    /// When the source world has no plugin solution yet, there is nothing to snapshot: the target's one is created
    /// when it is first opened.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="stack">The Stack repository.</param>
    /// <param name="source">The world whose plugin solution must be copied.</param>
    /// <param name="target">The new Long Term Support world.</param>
    /// <returns>True on success, false on error.</returns>
    internal static bool SnapshotPluginSolution( IActivityMonitor monitor,
                                                 StackRepository stack,
                                                 LocalWorldName source,
                                                 LocalWorldName target )
    {
        Throw.DebugAssert( target.LTSName != null );
        var sourceName = GetPluginSolutionName( source );
        var sourceRoot = source.SharedDataFolder.AppendPart( sourceName );
        if( !Directory.Exists( sourceRoot ) )
        {
            monitor.Info( $"World '{source.FullName}' has no plugin solution: the one of '{target.FullName}' is created when it is first opened." );
            return true;
        }
        var targetName = GetPluginSolutionName( target );
        var targetRoot = target.SharedDataFolder.AppendPart( targetName );
        using( monitor.OpenInfo( $"Copying '{sourceName}' plugin solution to '{targetRoot}'." ) )
        {
            if( Directory.Exists( targetRoot ) )
            {
                monitor.Error( $"The plugin solution folder '{targetRoot}' already exists." );
                return false;
            }
            var workingFolder = stack.StackWorkingFolder;
            var ignore = stack.GitRepository.Repository.Ignore;
            if( !FileHelper.CopyFolder( monitor,
                                        sourceRoot,
                                        targetRoot,
                                        ( path, isFolder ) =>
                                        {
                                            var relative = path.RemovePrefix( workingFolder ).Path;
                                            return !ignore.IsPathIgnored( isFolder ? relative + '/' : relative );
                                        } ) )
            {
                return false;
            }
            var sourceSlnx = targetRoot.AppendPart( $"{sourceName}.slnx" );
            if( File.Exists( sourceSlnx )
                && !FileHelper.MoveFile( monitor, sourceSlnx, targetRoot.AppendPart( $"{targetName}.slnx" ) ) )
            {
                return false;
            }
            return RedirectArtifactsPath( monitor, targetRoot.AppendPart( "Directory.Build.props" ), GetArtifactsPath( target ) );
        }
    }

    static bool RedirectArtifactsPath( IActivityMonitor monitor, NormalizedPath directoryBuildProps, string artifactsPath )
    {
        try
        {
            var text = File.Exists( directoryBuildProps ) ? File.ReadAllText( directoryBuildProps ) : "";
            var matches = Regex.Matches( text, @"<ArtifactsPath>[^<]*</ArtifactsPath>" );
            if( matches.Count != 1 )
            {
                // Without it, the new world's plugins are compiled where they are never loaded from.
                monitor.Error( $"""
                    Expected exactly one <ArtifactsPath> element in '{directoryBuildProps}', found {matches.Count}.
                    The build output of the plugin solution cannot be redirected to '{artifactsPath}'.
                    """ );
                return false;
            }
            var m = matches[0];
            text = string.Concat( text.AsSpan( 0, m.Index ), $"<ArtifactsPath>{artifactsPath}</ArtifactsPath>", text.AsSpan( m.Index + m.Length ) );
            File.WriteAllText( directoryBuildProps, text );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While updating '{directoryBuildProps}'.", ex );
            return false;
        }
    }
}
