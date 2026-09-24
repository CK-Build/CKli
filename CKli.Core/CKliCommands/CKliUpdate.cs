using CK.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// CKli update command. 
/// </summary>
sealed class CKliUpdate : Command
{
    internal CKliUpdate()
        : base( null,
                "update",
                "Auto update CKli (must not be in interactive mode).",
                [],
                [
                    (["--version"], "Optional explicit version to install. This is required in a LTS world.", Multiple: false)
                ],
                [
                    (["--stable"], "Consider stable versions only even if the current version is a prerelease."),
                    (["--prerelease"], "Consider prerelease versions (including CI builds) even if the current version is stable."),
                    (["--allow-downgrade"], """
                                            Allow package downgrade.
                                            Useful to come back to the last stable version when --prerelease has been used.
                                            """)
                ] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        bool prerelease = cmdLine.EatFlag( "--prerelease" );
        bool stable = cmdLine.EatFlag( "--stable" );
        bool allowDowngrade = cmdLine.EatFlag( "--allow-downgrade" );
        string? sVersion = cmdLine.EatSingleOption( "--version" );
        SVersion? version = null;
        if( sVersion != null )
        {
            version = SVersion.ParseNoThrow( sVersion, mustBeCSVersion: true );
            if( !version.IsValid )
            {
                monitor.Error( $"Invalid --version option: {version.ErrorMessage}" );
                return ValueTask.FromResult( false );
            }
        }
        if( prerelease && stable )
        {
            monitor.Error( "Flags --prerelease and --stable cannot be both specified." );
            return ValueTask.FromResult( false );
        }
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        if( context.Screen is InteractiveScreen )
        {
            monitor.Error( "Update command cannot be used in interactive mode." );
            return ValueTask.FromResult( false );
        }
        bool localLTSTool = false;
        // In a Long Term Support world ("@ltsName/..."), CKli is frozen on the world's pinned version by default.
        if( context.LTSName != null )
        {
            if( version == null )
            {
                monitor.Error( $"""
                    The --version option is required to update the CKli version in the Long Term Support world '{context.LTSName}'.
                    """ );
                return ValueTask.FromResult( false );
            }
            localLTSTool = true;
        }
        var info = InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() );
        if( !info.IsValidSyntax )
        {
            monitor.Error( $"Invalid assembly version: {info.ParseErrorMessage} in '{info.RawVersion}'." );
            return ValueTask.FromResult( false );
        }
        if( !prerelease && !stable )
        {
            prerelease = info.Version.IsPrerelease;
        }
        if( localLTSTool )
        {
            // The local tool is updated in-process: "dotnet ckli" runs from the NuGet cache's folder of its
            // version, so updating it only rewrites the manifest (and restores the other version's folder).
            // Nothing that runs here is overwritten, unlike the global tool below.
            Throw.DebugAssert( version != null && context.LTSName != null );
            return ValueTask.FromResult( UpdateLTSWorld( monitor, context, context.LTSName, version, allowDowngrade ) );
        }
        var updateCmd = "dotnet tool update CKli -g";
        if( version != null )
        {
            updateCmd += " --version " + version.ToString();
        }
        else if( prerelease )
        {
            updateCmd += " --prerelease";
        }
        if( allowDowngrade ) updateCmd += " --allow-downgrade";
        updateCmd += $" --no-http-cache --source " + LocalCKliTool.FeedUrl;
        monitor.Info( ScreenType.CKliScreenTag, $"""
            Currently installed '{info.Version}'. Will now execute after this CKli instance ends:
            {updateCmd}
            """ );

        try
        {
            if( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
            {
                // This uses windows PowerShell (based on NetFramework 4, the legacy one) should always be available.
                // The new PowerShell Core (based on .Net) is an opt-in.
                // FYI, the cmd approach is insane (and not portable anyway).
                // See https://stackoverflow.com/questions/22558869/wait-for-process-to-end-in-windows-batch-file
                var cmd = $@"Wait-Process -Id {Environment.ProcessId} -Timeout 20 -ErrorAction SilentlyContinue; {updateCmd}";
                Process.Start( new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NoLogo -NonInteractive -ExecutionPolicy unrestricted -command {cmd}",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetTempPath()
                } );
            }
            else if( RuntimeInformation.IsOSPlatform( OSPlatform.Linux ) || RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) )
            {
                // On Unix platforms, use a shell script that waits for the current process to exit.
                // kill -0 checks if process exists (POSIX standard, works on Linux and macOS).
                // The wait loop checks every 0.5 seconds with a natural ~20 second timeout.
                var shellCmd = $"while kill -0 {Environment.ProcessId} 2>/dev/null; do sleep 0.5; done; exec {updateCmd}";
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                };
                // Use ArgumentList to avoid .NET's argument parsing - pass arguments directly
                psi.ArgumentList.Add( "-c" );
                psi.ArgumentList.Add( shellCmd );
                Process.Start( psi );
            }
            else
            {
                monitor.Warn( $"""
                    Auto-update is not supported on this platform. Please run manually:
                    {updateCmd}
                    """ );
            }
        }
        catch( Exception ex )
        {
            monitor.Error( "Unable to start update process.", ex );
            return ValueTask.FromResult( false );
        }
        return ValueTask.FromResult( true );
    }

    // Updates the CKli version of a Long Term Support world: its local tool, the CKliVersion pin of its definition
    // file and the CKli.Version.props of its plugin solution. These 3 steps cannot be atomic: they are ordered so
    // that an interrupted update is either retried or healed.
    // 1 - The local tool first: this is what checks that the version exists (and refuses a downgrade that has not
    //     been allowed) before anything else is changed. If a later step fails, the next command run in the world
    //     sees a tool that is not the pinned one, and the pin mismatch moves the tool back to the pin.
    // 2 - The pin, committed and pushed in the Stack repository: the other developers get it with their next pull
    //     (and the pin mismatch installs it as their local tool).
    // 3 - The CKli.Version.props is deleted rather than rewritten: its (re)generation is what triggers the
    //     recompilation of the plugins against the new CKli.Plugins.Core. Written here, the next command would
    //     find it up to date and use plugins compiled against the previous version.
    static bool UpdateLTSWorld( IActivityMonitor monitor, CKliEnv context, string ltsName, SVersion version, bool allowDowngrade )
    {
        var stack = StackRepository.TryOpenFromPath( monitor, context, out _, skipPullStack: false );
        if( stack == null ) return false;
        try
        {
            var worldName = stack.FindWorldName( monitor, ltsName );
            if( worldName == null ) return false;
            var definitionPath = worldName.XmlDescriptionFilePath;
            // The definition file is not loaded as a WorldDefinitionFile: that checks the very pin that changes
            // here, and refuses it when the running CKli is not the pinned one.
            var doc = XDocument.Load( definitionPath, LoadOptions.PreserveWhitespace );
            var root = doc.Root;
            Throw.DebugAssert( root != null );
            var current = root.Attribute( XNames.CKliVersion )?.Value;
            var props = worldName.SharedDataFolder.Combine( $"{PluginMachinery.GetPluginSolutionName( worldName )}/{PluginMachinery.CKliVersionPropsFileName}" );
            monitor.Info( ScreenType.CKliScreenTag, $"""
                Updating the CKli version of the Long Term Support world '{worldName.FullName}' from '{current ?? "(none)"}' to '{version}':
                1 - its local tool in '{worldName.WorldRoot}' (the one '{LocalCKliTool.LocalCommand}' runs),
                2 - the CKliVersion attribute of '{definitionPath.LastPart}' (committed and pushed in the Stack repository),
                3 - the '{PluginMachinery.CKliVersionPropsFileName}' of its plugin solution (deleted: it is regenerated and
                    the plugins are recompiled by the next command).
                These steps cannot be done atomically: on error, fix the cause and run this command again.
                """ );
            if( !LocalCKliTool.Install( monitor, worldName.WorldRoot, version, allowDowngrade ) )
            {
                monitor.Error( $"""
                    Unable to install CKli '{version}' as the local tool of '{worldName.WorldRoot}' (see the warning above).
                    Nothing has been changed.
                    """ );
                return false;
            }
            root.SetAttributeValue( XNames.CKliVersion, version.ToString() );
            doc.SafeSave( definitionPath );
            if( !stack.Commit( monitor, $"Updated CKli version of '{worldName.FullName}' to '{version}'." )
                || !stack.PushChanges( monitor )
                || !FileHelper.DeleteFile( monitor, props ) )
            {
                monitor.Error( $"""
                    The CKli version of '{worldName.FullName}' is partially updated. Fix the cause and run this command again.
                    """ );
                return false;
            }
            monitor.Info( ScreenType.CKliScreenTag, $"""
                CKli '{version}' is now the CKli of '{worldName.FullName}': use '{LocalCKliTool.LocalCommand}' there.
                """ );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"""
                While updating the CKli version of Long Term Support world '{ltsName}'. Fix the cause and run this command again.
                """, ex );
            return false;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
