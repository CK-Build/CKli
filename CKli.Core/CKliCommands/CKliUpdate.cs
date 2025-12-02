using CK.Core;
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
                    (["--prerelease"], "Consider prerelease versions (inclduing CI builds).")
                ] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool prerelease = cmdLine.EatFlag( "--prerelease" );
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        if( context.Screen is InteractiveScreen )
        {
            monitor.Warn( "Sorry, update command cannot be used in interactive mode." );
            return ValueTask.FromResult( false );
        }
        var updateCmd = prerelease
                ? "dotnet tool update CKli -g --prerelease --add-source https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json"
                : "dotnet tool update CKli -g --allow-downgrade";

        var info = CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() );
        monitor.Info( ScreenType.CKliScreenTag, $"""
            Currently installed '{info.Version}'. Will now execute after this CKli instance ends:
            {updateCmd}
            """ );

        // This uses PowerShell and this is not ideal.
        // IMO, the cmd approach is insane (and not portable).
        // See https://stackoverflow.com/questions/22558869/wait-for-process-to-end-in-windows-batch-file
        // I'd rather use a C# 10 file-based application...
        var pid = Process.GetCurrentProcess().Id;
        var cmd = $@"Wait-Process -Id {pid} -Timeout 20 -ErrorAction SilentlyContinue; {updateCmd}";
        Process.Start( new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NoLogo -NonInteractive -ExecutionPolicy unrestricted -command {cmd}",
            UseShellExecute = false
        } );
        return ValueTask.FromResult( true );
    }

}
