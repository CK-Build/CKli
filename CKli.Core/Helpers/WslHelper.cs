using System;

namespace CKli.Core;

/// <summary>
/// Windows Subsystem for Linux specifics.
/// </summary>
public static class WslHelper
{
    /// <summary>
    /// Gets the WSL distribution name when running in a WSL distribution hosted by Windows Terminal, null otherwise.
    /// Windows Terminal opens links on the Windows side, where a path of the distribution only exists
    /// under "\\wsl.localhost\&lt;distribution&gt;".
    /// </summary>
    internal static string? WindowsTerminalDistroName { get; } = Environment.GetEnvironmentVariable( "WT_SESSION" ) != null
                                                                 && Environment.GetEnvironmentVariable( "WSL_DISTRO_NAME" ) is { Length: > 0 } name
                                                                    ? name
                                                                    : null;

    /// <summary>
    /// Gets the url of a target as Windows sees it from outside the distribution: a local path is moved
    /// under "file://wsl.localhost/&lt;distribution&gt;/", any other url is left unchanged.
    /// </summary>
    /// <param name="target">The link target.</param>
    /// <param name="distroName">The WSL distribution name.</param>
    /// <returns>The url to use on the Windows side.</returns>
    public static string ToWslUrl( Uri target, string distroName )
    {
        var url = target.ToString();
        // shortcut: a path under /mnt/<drive> is denied through wsl.localhost, map it to file:///<drive>:/ if stacks on Windows drives matter.
        return target.IsAbsoluteUri && target.IsFile && target.Host.Length == 0
                ? $"file://wsl.localhost/{distroName}{url["file://".Length..]}"
                : url;
    }
}
