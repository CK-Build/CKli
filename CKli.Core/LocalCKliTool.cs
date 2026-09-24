using CK.Core;
using System;
using System.IO;

namespace CKli.Core;

/// <summary>
/// Installs CKli as a local .NET tool in the root folder of a Long Term Support world.
/// <para>
/// A LTS world is pinned to the CKli version that created it (its CKliVersion attribute): the global "ckli" follows
/// the latest version, so it cannot open it. The local tool manifest of the world's root folder applies to that folder
/// and everything below it, so "dotnet ckli" (instead of "ckli") runs the pinned version there.
/// </para>
/// </summary>
static class LocalCKliTool
{
    /// <summary>
    /// The feed that carries every CKli version (stable, prerelease and CI builds): the only source of the install,
    /// so that a pinned prerelease is installed like a stable version and no user level configuration interferes.
    /// </summary>
    internal const string FeedUrl = "https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json";

    /// <summary>
    /// Gets the command that runs CKli in a folder where <see cref="Ensure"/> has installed it.
    /// </summary>
    internal const string LocalCommand = "dotnet ckli";

    /// <summary>
    /// Ensures that CKli <paramref name="version"/> is the local tool of <paramref name="folder"/>: creates the tool
    /// manifest if needed, then installs (or moves back) the "CKli" tool to the version, downgrading it if needed:
    /// the pin is the truth. This is idempotent.
    /// <para>
    /// Nothing is installed under a test harness (<see cref="CKliRootEnv.IsTestRun"/>): this would download the
    /// tool from the feed.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="folder">The root folder of the Long Term Support world.</param>
    /// <param name="version">The pinned CKli version.</param>
    /// <returns>True when installed, false otherwise (a warning has been logged).</returns>
    internal static bool Ensure( IActivityMonitor monitor, NormalizedPath folder, SVersion version )
    {
        return Install( monitor, folder, version, allowDowngrade: true ) && !CKliRootEnv.IsTestRun;
    }

    /// <summary>
    /// Installs or updates the "CKli" local tool of <paramref name="folder"/> to <paramref name="version"/>,
    /// creating the tool manifest if needed. The commands run in <paramref name="folder"/>: a manifest of a
    /// sub folder (a repository may have its own) must not receive CKli.
    /// <para>
    /// Under a test harness (<see cref="CKliRootEnv.IsTestRun"/>), the commands are only logged and this succeeds.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="folder">The root folder of the Long Term Support world.</param>
    /// <param name="version">The version to install.</param>
    /// <param name="allowDowngrade">Whether the tool can be moved to a lower version.</param>
    /// <returns>True on success, false otherwise (a warning has been logged).</returns>
    internal static bool Install( IActivityMonitor monitor, NormalizedPath folder, SVersion version, bool allowDowngrade )
    {
        var install = $"tool update CKli --version {version}{(allowDowngrade ? " --allow-downgrade" : "")} --source {FeedUrl}";
        if( CKliRootEnv.IsTestRun )
        {
            monitor.Info( $"Test run: skipping 'dotnet new tool-manifest' and 'dotnet {install}' in '{folder}'." );
            return true;
        }
        using( monitor.OpenInfo( $"Installing CKli '{version}' as a local tool in '{folder}'." ) )
        {
            try
            {
                Directory.CreateDirectory( folder );
                // "dotnet new tool-manifest" fails when the manifest exists (the SDK 10 writes it in the folder,
                // the previous ones in its ".config/" sub folder).
                if( !File.Exists( folder.AppendPart( "dotnet-tools.json" ) )
                    && !File.Exists( folder.Combine( ".config/dotnet-tools.json" ) )
                    && ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", "new tool-manifest", folder, null ) != 0 )
                {
                    monitor.Warn( $"Command 'dotnet new tool-manifest' failed in '{folder}'." );
                    return false;
                }
                // "update" installs the tool when it is missing, and moves it to the version otherwise (it fails
                // for a version that doesn't exist, and for a lower one without "--allow-downgrade").
                if( ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", install, folder, null ) != 0 )
                {
                    monitor.Warn( $"Command 'dotnet {install}' failed in '{folder}'." );
                    return false;
                }
                return true;
            }
            catch( Exception ex )
            {
                monitor.Warn( $"While installing CKli '{version}' as a local tool in '{folder}'.", ex );
                return false;
            }
        }
    }
}
