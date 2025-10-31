using CK.Core;

namespace CKli.Core;

/// <summary>
/// The immutable command context reflects the <see cref="CKliRootEnv"/>: <see cref="CKliRootEnv.DefaultCKliEnv"/>
/// should almost always be used unless the default context needs to be overridden. This is the case in tests: 
/// <see cref="ChangeDirectory(NormalizedPath)"/> is used to change the <see cref="CurrentDirectory"/> (and potentially the <see cref="CurrentStackPath"/>).
/// </summary>
public sealed class CKliEnv
{
    readonly IScreen _screen;
    readonly ISecretsStore _secretsStore;
    readonly NormalizedPath _currentDirectory;
    readonly NormalizedPath _currentStackPath;

    // CKliRootEnv.DefaultCommandContext & CKliCommands.ExecuteAsync that sets an interactive screen.
    internal CKliEnv( IScreen screen, ISecretsStore secretsStore, NormalizedPath currentDirectory, NormalizedPath currentStackPath )
    {
        _screen = screen;
        _secretsStore = secretsStore;
        _currentDirectory = currentDirectory;
        _currentStackPath = currentStackPath;
    }

    /// <summary>
    /// Initialize a <see cref="CKliEnv"/> that is not the default <see cref="CKliRootEnv.DefaultCKliEnv"/>. 
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
    public CKliEnv ChangeDirectory( NormalizedPath path ) => path.IsEmptyPath
                                                                ? this
                                                                : new CKliEnv( _currentDirectory.Combine( path ).ResolveDots(), _secretsStore, _screen );

}
