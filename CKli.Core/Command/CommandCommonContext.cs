using CK.Core;
using CK.Monitoring;
using System.Threading;

namespace CKli.Core;

/// <summary>
/// The immutable command context reflects the <see cref="CKliRootEnv"/>: <see cref="CKliRootEnv.DefaultCommandContext"/>
/// should almost always be used unless the default context needs to be overridden. This is the case in tests: the
/// <see cref=""/>
/// </summary>
public sealed class CommandCommonContext
{
    readonly ISecretsStore _secretsStore;
    readonly NormalizedPath _currentDirectory;
    readonly NormalizedPath _currentStackPath;

    // CKliRootEnv.DefaultCommandContext
    internal CommandCommonContext( ISecretsStore secretsStore, NormalizedPath currentDirectory, NormalizedPath currentStackPath )
    {
        _secretsStore = secretsStore;
        _currentDirectory = currentDirectory;
        _currentStackPath = currentStackPath;
    }

    /// <summary>
    /// Initialize a <see cref="CommandCommonContext"/> that is not the default <see cref="CKliRootEnv.DefaultCommandContext"/>. 
    /// </summary>
    /// <param name="currentDirectory">Current directory to consider.</param>
    /// <param name="secretsStore">Optional secrets store (overrides the default <see cref="CKliRootEnv.SecretsStore"/>).</param>
    public CommandCommonContext( NormalizedPath currentDirectory, ISecretsStore? secretsStore = null )
    {
        Throw.CheckArgument( !currentDirectory.IsEmptyPath );
        _currentDirectory = currentDirectory;
        _secretsStore = secretsStore ?? CKliRootEnv.SecretsStore;
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
    /// Returns a new context with an updated <see cref="CurrentDirectory"/> that is <see cref="NormalizedPath.Combine(NormalizedPath)"/>
    /// with the <paramref name="path"/>.
    /// <para>
    /// The name "With" is a reminder that, as this is an immutable object, the resulting context is the returned value.
    /// </para>
    /// </summary>
    /// <param name="path">The path. Usually relative but may be absolute.</param>
    /// <returns>A new context.</returns>
    public CommandCommonContext With( NormalizedPath path ) => path.IsEmptyPath ? this : new CommandCommonContext( _currentDirectory.Combine( path ).ResolveDots(), _secretsStore );
}
