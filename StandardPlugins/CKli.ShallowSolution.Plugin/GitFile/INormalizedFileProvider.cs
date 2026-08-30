using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using Microsoft.Extensions.FileProviders;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Modified .Net <see cref="IFileProvider"/> contract that:
/// <list type="bullet">
///     <item>Uses <see cref="NormalizedPath"/>.</item>
///     <item>Avoids meaningless <see cref="IFileProvider.Watch(string)"/> burden.</item>
///     <item>
///     Avoids the <see cref="IFileInfo.Exists"/> trap by simply returning null instead of
///     the <see cref="NotFoundDirectoryContents.Singleton"/> or useless <see cref="NotFoundFileInfo"/> instances.
///     </item>
/// </list>
/// The static factory <see cref="GetFiles(Commit, bool, GitFileProviderCache?)"/> can handle
/// the file system or any Git <see cref="Tree"/>. The <see cref="Create(NormalizedPath)"/> applies to the file system.
/// </summary>
public interface INormalizedFileProvider
{
    /// <summary>
    /// Gets a directory at the given path or null.
    /// </summary>
    /// <param name="sub">The relative path that identifies the directory.</param>
    /// <returns>The contents of the directory or null.</returns>
    IDirectoryContents? GetDirectoryContents( NormalizedPath sub );

    /// <summary>
    /// Locates a file at the given path.
    /// </summary>
    /// <param name="sub">The relative path that identifies the file.</param>
    /// <returns>The file information or null.</returns>
    IFileInfo? GetFileInfo( NormalizedPath sub );

    /// <summary>
    /// Gets the content of the commit as a <see cref="INormalizedFileProvider"/> that can be
    /// the physical file system if the commit is the head of the repository.
    /// <para>
    /// Even if a <paramref name="cache"/> is provided, there is no cache when <paramref name="useWorkingFolder"/> is true:
    /// if another commit is checked out, the content must not be used anymore or kittens will die.
    /// </para>
    /// </summary>
    /// <param name="commit">The commit for which content must be returned.</param>
    /// <param name="useWorkingFolder">
    /// True to use the file system if the commit is checked out.
    /// False to always use the committed content and ignores the current file system.
    /// </param>
    /// <param name="cache">Optional cache.</param>
    /// <returns>The commit content.</returns>
    public static INormalizedFileProvider GetFiles( Commit commit, bool useWorkingFolder, GitFileProviderCache? cache = null  )
    {
        var repo = ((IBelongToARepository)commit).Repository;
        return useWorkingFolder && commit.Sha == repo.Head.Tip.Sha
            ? new CheckedOutFileProvider( repo.Info.WorkingDirectory )
            : cache == null
                ? new TreeFolder( commit.Tree )
                : cache.GetFiles( commit );
    }

    /// <summary>
    /// Creates a new file provider on the file system.
    /// </summary>
    /// <param name="root">The root folder to consider.</param>
    /// <returns>The folder content.</returns>
    public static INormalizedFileProvider Create( NormalizedPath root ) => new CheckedOutFileProvider( root );

}
