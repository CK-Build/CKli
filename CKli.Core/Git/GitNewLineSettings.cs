using CK.Core;
using LibGit2Sharp;
using System;

namespace CKli.Core;

/// <summary>
/// The setting that makes Git check out the wrong newline.
/// </summary>
/// <param name="Key">The configuration key: "core.autocrlf" or "core.eol".</param>
/// <param name="Value">The value of the key.</param>
/// <param name="Level">The level of the configuration that defines the key.</param>
/// <param name="WorkingNewLine">The newline that Git writes in the working folder.</param>
public sealed record GitNewLineProblem( string Key, string Value, ConfigurationLevel Level, string WorkingNewLine );

/// <summary>
/// Computes the newline that Git writes in a working folder for a "text=auto" file
/// from the "core.autocrlf" and "core.eol" settings.
/// </summary>
public static class GitNewLineSettings
{
    /// <summary>
    /// Gets the newline of the platform: "\r\n" on Windows, "\n" everywhere else.
    /// </summary>
    /// <param name="isWindows">True for Windows.</param>
    /// <returns>The expected newline.</returns>
    public static string GetExpectedNewLine( bool isWindows ) => isWindows ? "\r\n" : "\n";

    /// <summary>
    /// Gets the newline Git writes in the working folder for a "text=auto" file: "core.autocrlf" wins
    /// when it is true (CRLF) or "input" (LF), otherwise "core.eol" decides ("crlf", "lf", or the platform's
    /// newline for "native" or when it is not set).
    /// </summary>
    /// <param name="autoCrlf">The "core.autocrlf" value if any.</param>
    /// <param name="eol">The "core.eol" value if any.</param>
    /// <param name="isWindows">True for Windows.</param>
    /// <returns>The newline.</returns>
    public static string GetWorkingNewLine( string? autoCrlf, string? eol, bool isWindows )
    {
        if( IsTrue( autoCrlf ) ) return "\r\n";
        if( IsInput( autoCrlf ) ) return "\n";
        if( "crlf".Equals( eol, StringComparison.OrdinalIgnoreCase ) ) return "\r\n";
        if( "lf".Equals( eol, StringComparison.OrdinalIgnoreCase ) ) return "\n";
        return GetExpectedNewLine( isWindows );
    }

    /// <summary>
    /// Finds the setting that makes Git check out a newline that is not the one of the platform.
    /// This never changes any setting.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repository">The repository to check.</param>
    /// <param name="isWindows">True for Windows.</param>
    /// <returns>
    /// Null when the settings give the newline of the platform, when the repository is bare,
    /// or when its configuration cannot be read (a warning is logged).
    /// </returns>
    public static GitNewLineProblem? Check( IActivityMonitor monitor, Repository repository, bool isWindows )
    {
        if( repository.Info.IsBare ) return null;
        try
        {
            var autoCrlf = repository.Config.Get<string>( "core.autocrlf" );
            var eol = repository.Config.Get<string>( "core.eol" );
            // A key without value ("[core] autocrlf") is true for Git.
            var autoCrlfValue = autoCrlf != null ? autoCrlf.Value ?? "true" : null;
            var newLine = GetWorkingNewLine( autoCrlfValue, eol?.Value, isWindows );
            if( newLine == GetExpectedNewLine( isWindows ) ) return null;
            // The entry that decides: "core.autocrlf" hides "core.eol" when it is true or "input".
            return IsTrue( autoCrlfValue ) || IsInput( autoCrlfValue )
                    ? new GitNewLineProblem( autoCrlf!.Key, autoCrlfValue!, autoCrlf.Level, newLine )
                    : new GitNewLineProblem( eol!.Key, eol.Value, eol.Level, newLine );
        }
        catch( Exception ex )
        {
            monitor.Warn( $"Unable to read the Git configuration of '{repository.Info.WorkingDirectory}'. Skipping line endings check.", ex );
            return null;
        }
    }

    static bool IsTrue( string? v ) => v != null
                                       && (v.Equals( "true", StringComparison.OrdinalIgnoreCase )
                                           || v.Equals( "yes", StringComparison.OrdinalIgnoreCase )
                                           || v.Equals( "on", StringComparison.OrdinalIgnoreCase )
                                           || (int.TryParse( v, out var i ) && i != 0));

    static bool IsInput( string? v ) => "input".Equals( v, StringComparison.OrdinalIgnoreCase );
}
