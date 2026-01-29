using System;
using System.Text.RegularExpressions;

namespace CKli.Core.GitHosting;

/// <summary>
/// Utility for parsing and normalizing Git remote URLs.
/// Handles both HTTPS and SSH formats.
/// </summary>
public static partial class RemoteUrlParser
{
    // SSH SCP format: user@host:path (e.g., git@github.com:owner/repo.git)
    [GeneratedRegex( @"^[^@]+@(?<host>[^:]+):(?<path>.+?)(?:\.git)?$", RegexOptions.Compiled )]
    private static partial Regex SshScpUrlRegex();

    // SSH scheme format: ssh://git@host:port/path or ssh://git@host/path
    [GeneratedRegex( @"^ssh://(?:[^@]+@)?(?<host>[^:/]+)(?::(?<port>\d+))?(?<path>/.+?)(?:\.git)?$", RegexOptions.Compiled )]
    private static partial Regex SshSchemeUrlRegex();

    /// <summary>
    /// Normalizes a Git remote to use the <see cref="Uri.UriSchemeHttps"/>.
    /// </summary>
    /// <param name="remoteUrl">The URL to normalize (SSH or HTTPS).</param>
    /// <returns>The normalized HTTPS URL, or null if parsing failed.</returns>
    public static Uri? TryNormalizeToHttps( string remoteUrl )
    {
        if( string.IsNullOrWhiteSpace( remoteUrl ) ) return null;

        remoteUrl = remoteUrl.Trim();

        // Already an HTTPS URL
        if( remoteUrl.StartsWith( "https://", StringComparison.OrdinalIgnoreCase ) )
        {
            return Uri.TryCreate( remoteUrl.TrimEnd( '/' ), UriKind.Absolute, out var uri ) ? uri : null;
        }

        // Try HTTP (will be normalized to HTTPS)
        if( remoteUrl.StartsWith( "http://", StringComparison.OrdinalIgnoreCase ) )
        {
            var httpsUrl = "https://" + remoteUrl.Substring( 7 );
            return Uri.TryCreate( httpsUrl.TrimEnd( '/' ), UriKind.Absolute, out var uri ) ? uri : null;
        }

        // Try SSH scheme format (ssh://git@host:port/path)
        var sshSchemeMatch = SshSchemeUrlRegex().Match( remoteUrl );
        if( sshSchemeMatch.Success )
        {
            var host = sshSchemeMatch.Groups["host"].Value;
            var path = sshSchemeMatch.Groups["path"].Value;
            // Port is intentionally not included in HTTPS URL
            return new Uri( $"https://{host}{path}" );
        }

        // Try SCP-style SSH format (git@host:path)
        var sshScpMatch = SshScpUrlRegex().Match( remoteUrl );
        if( sshScpMatch.Success )
        {
            var host = sshScpMatch.Groups["host"].Value;
            var path = sshScpMatch.Groups["path"].Value;
            return new Uri( $"https://{host}/{path}" );
        }

        // Try as a scheme-less URL (github.com/owner/repo)
        if( !remoteUrl.Contains( '@' ) && remoteUrl.Contains( '/' ) )
        {
            var cleanUrl = remoteUrl.TrimEnd( '/' );
            if( cleanUrl.EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) )
            {
                cleanUrl = cleanUrl.Substring( 0, cleanUrl.Length - 4 );
            }
            return Uri.TryCreate( $"https://{cleanUrl}", UriKind.Absolute, out var uri ) ? uri : null;
        }

        return null;
    }

    /// <summary>
    /// Extracts the host from any URL format.
    /// Port numbers are stripped - only the hostname is returned.
    /// </summary>
    /// <param name="remoteUrl">The URL to extract the host from.</param>
    /// <returns>The host (without port), or null if parsing failed.</returns>
    public static string? GetHost( string remoteUrl )
    {
        if( string.IsNullOrWhiteSpace( remoteUrl ) ) return null;

        remoteUrl = remoteUrl.Trim();

        // SSH scheme format (ssh://git@host:port/path)
        var sshSchemeMatch = SshSchemeUrlRegex().Match( remoteUrl );
        if( sshSchemeMatch.Success )
        {
            return sshSchemeMatch.Groups["host"].Value;
        }

        // SCP-style SSH format (user@host:path)
        var sshScpMatch = SshScpUrlRegex().Match( remoteUrl );
        if( sshScpMatch.Success )
        {
            return sshScpMatch.Groups["host"].Value;
        }

        // Try HTTPS/HTTP or scheme-less - Uri.Host already excludes port
        var normalized = TryNormalizeToHttps( remoteUrl );
        return normalized?.Host;
    }

    /// <summary>
    /// Parses the owner and repository name from a standard path (e.g., "owner/repo").
    /// Supports nested groups (e.g., "group/subgroup/repo").
    /// </summary>
    /// <param name="path">The path portion of the URL.</param>
    /// <returns>The owner and repository name, or null if parsing failed.</returns>
    public static (string Owner, string RepoName)? ParseStandardPath( string path )
    {
        if( string.IsNullOrWhiteSpace( path ) ) return null;

        path = path.Trim().Trim( '/' );
        if( path.EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) )
        {
            path = path.Substring( 0, path.Length - 4 );
        }

        // Split by '/' - for GitLab, owner can contain multiple segments (groups)
        var parts = path.Split( '/' );
        if( parts.Length < 2 ) return null;

        // The last part is the repo, everything before is the owner/group
        var repoName = parts[^1];
        var owner = string.Join( '/', parts[..^1] );

        if( string.IsNullOrWhiteSpace( owner ) || string.IsNullOrWhiteSpace( repoName ) )
            return null;

        return (owner, repoName);
    }
}
