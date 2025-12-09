using CK.Core;
using LibGit2Sharp;
using System;

namespace CKli.Core;

/// <summary>
/// The immutable command context reflects the <see cref="CKliRootEnv"/>: <see cref="CKliRootEnv.DefaultCKliEnv"/>
/// should almost always be used unless the default context needs to be overridden. This is the case in tests: 
/// <see cref="ChangeDirectory(NormalizedPath)"/> is used to change the <see cref="CurrentDirectory"/> (and potentially the <see cref="CurrentStackPath"/>).
/// <para>
/// This object is not really immutable: <see cref="StartCommandHandlingLocalTime"/> and <see cref="StartCommandHandlingUtc"/> are internally updated:
/// command handlers use this to rely on a shared time for the currently executed command and <see cref="Committer"/> uses it. 
/// </para>
/// </summary>
public sealed class CKliEnv
{
    readonly IScreen _screen;
    readonly ISecretsStore _secretsStore;
    readonly NormalizedPath _currentDirectory;
    readonly NormalizedPath _currentStackPath;
    DateTimeOffset _startCommandHandlingLocalTime;
    Signature? _committer;

    internal CKliEnv( IScreen screen,
                      ISecretsStore secretsStore,
                      NormalizedPath currentDirectory,
                      NormalizedPath currentStackPath,
                      DateTimeOffset startCommandHandlingLocalTime )
    {
        _screen = screen;
        _secretsStore = secretsStore;
        _currentDirectory = currentDirectory;
        _currentStackPath = currentStackPath;
        _startCommandHandlingLocalTime = startCommandHandlingLocalTime;
    }

    internal void OnStartCommandHandling()
    {
        _startCommandHandlingLocalTime = DateTimeOffset.Now;
    }

    /// <summary>
    /// Initialize a <see cref="CKliEnv"/> that is not the default <see cref="CKliRootEnv.DefaultCKliEnv"/>.
    /// Mainly to support tests.
    /// </summary>
    /// <param name="currentDirectory">Current directory to consider.</param>
    /// <param name="secretsStore">Optional secrets store (overrides the default <see cref="CKliRootEnv.SecretsStore"/>).</param>
    /// <param name="screen">Optional screen (overrides the default <see cref="CKliRootEnv.Screen"/>).</param>
    public CKliEnv( NormalizedPath currentDirectory, ISecretsStore? secretsStore = null, IScreen? screen = null )
    {
        Throw.CheckArgument( !currentDirectory.IsEmptyPath );
        _currentDirectory = currentDirectory;
        _secretsStore = secretsStore ?? CKliRootEnv.SecretsStore;
        _screen = screen ?? CKliRootEnv.Screen;
        _currentStackPath = StackRepository.FindGitStackPath( currentDirectory );
        _startCommandHandlingLocalTime = DateTimeOffset.Now;
    }

    /// <summary>
    /// Gets the current directory.
    /// </summary>
    public NormalizedPath CurrentDirectory => _currentDirectory;

    /// <summary>
    /// Gets the current <see cref="StackRepository.StackWorkingFolder"/> if <see cref="CurrentDirectory"/> is in a Stack directory
    /// or the <see cref="NormalizedPath.IsEmptyPath"/>.
    /// </summary>
    public NormalizedPath CurrentStackPath => _currentStackPath;

    /// <summary>
    /// Gets the secrets store to use.
    /// </summary>
    public ISecretsStore SecretsStore => _secretsStore;

    /// <summary>
    /// Gets the screen to use.
    /// </summary>
    public IScreen Screen => _screen;

    /// <summary>
    /// Gets the renderable unit (from the <see cref="ScreenType"/>).
    /// </summary>
    public IRenderable RenderableUnit => _screen.ScreenType.Unit;

    /// <summary>
    /// Returns a new context with an updated <see cref="CurrentDirectory"/> that is <see cref="NormalizedPath.Combine(NormalizedPath)"/>
    /// with the <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path. Usually relative but may be absolute.</param>
    /// <returns>A new context.</returns>
    public CKliEnv ChangeDirectory( NormalizedPath path )
    {
        if( path.IsEmptyPath ) return this;
        var currentDirectory = _currentDirectory.Combine( path ).ResolveDots();
        var currentStackPath = StackRepository.FindGitStackPath( currentDirectory );
        return new CKliEnv( _screen, _secretsStore, currentDirectory, currentStackPath, _startCommandHandlingLocalTime );
    }

    /// <summary>
    /// Gets the UTC start date and time of the current command handling.
    /// </summary>
    public DateTime StartCommandHandlingUtc => _startCommandHandlingLocalTime.UtcDateTime;

    /// <summary>
    /// Gets the local start date and time of the current command handling.
    /// </summary>
    public DateTimeOffset StartCommandHandlingLocalTime => _startCommandHandlingLocalTime;

    /// <summary>
    /// Gets the git signature of "CKli" ("none" email) with <see cref="Signature.When"/>
    /// that is this <see cref="StartCommandHandlingLocalTime"/>.
    /// <para>
    /// This should be used as the committer identity but may be also used as the author.
    /// </para>
    /// </summary>
    public Signature Committer
    {
        get
        {
            if( _committer == null || _committer.When != _startCommandHandlingLocalTime )
            {
                _committer = new Signature( "CKli", "none", _startCommandHandlingLocalTime );
            }
            return _committer;
        }
    }
}
