using CK.Core;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

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
                [],
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
                                                                    CommandLineArguments cmdLine )
    {
        bool prerelease = cmdLine.EatFlag( "--prerelease" );
        bool stable = cmdLine.EatFlag( "--stable" );
        if( prerelease && stable )
        {
            monitor.Error( "Flags --prerelease and --stable cannot be both specified." );
            return ValueTask.FromResult( false );
        }
        bool allowDowngrade = cmdLine.EatFlag( "--allow-downgrade" );
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        if( context.Screen is InteractiveScreen )
        {
            monitor.Error( "Update command cannot be used in interactive mode." );
            return ValueTask.FromResult( false );
        }
        var info = CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() );
        if( !info.IsValidSyntax )
        {
            monitor.Error( $"Invalid assembly version: {info.ParseErrorMessage} in '{info.RawVersion}'." );
            return ValueTask.FromResult( false );
        }
        if( !prerelease && !stable )
        {
            prerelease = info.Version.IsPrerelease;
        }
        var updateCmd = prerelease
                ? "dotnet tool update CKli -g --prerelease --add-source https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json"
                : "dotnet tool update CKli -g";
        if( allowDowngrade ) updateCmd += " --allow-downgrade";
        monitor.Info( ScreenType.CKliScreenTag, $"""
            Currently installed '{info.Version}'. Will now execute after this CKli instance ends:
            {updateCmd}
            """ );

        if( Environment.OSVersion.Platform == PlatformID.Unix )
        {
            monitor.Warn( "Sorry, this is not available on non Windows platform yet." );
        }
        else
        {
            // This uses windows PowerShell (based on NetFramework 4, the legacy one) should always be available.
            // The new PowerShell Core (based on .Net) is an opt-in.
            // FYI, the cmd approach is insane (and not portable anyway).
            // See https://stackoverflow.com/questions/22558869/wait-for-process-to-end-in-windows-batch-file
            var cmd = $@"Wait-Process -Id {Environment.ProcessId} -Timeout 20 -ErrorAction SilentlyContinue; {updateCmd};echo ''";
            Process.Start( new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NoLogo -NonInteractive -ExecutionPolicy unrestricted -command {cmd}",
                UseShellExecute = false
            } );
        }
        return ValueTask.FromResult( true );
    }

}
