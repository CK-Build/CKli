using Microsoft.Extensions.Configuration.UserSecrets;
using CK.Core;
using System.Text.Json;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// Implements <see cref="ISecretsStore"/> on dotnet user-secrets.
/// <para>
/// Note that the Id is <see cref="CKliRootEnv.InstanceName"/> by default.
/// </para>
/// </summary>
public sealed class DotNetUserSecretsStore : ISecretsStore, IDisposable
{
    static string? _secretFilePath;

    readonly string? _userSecretsId;
    // No Empty pattern. https://github.com/dotnet/runtime/issues/59303
    JsonDocument? _document;
    bool _documentLoaded;
    DateTime _loadedWriteTimeUtc;
    long _loadedLength;


    /// <summary>
    /// Default constructor use <see cref="CKliRootEnv.InstanceName"/> as the secrets identifier.
    /// </summary>
    public DotNetUserSecretsStore()
        : this( null! )
    {
    }

    /// <summary>
    /// Initializes a new secrets store with a secrets identifier.
    /// This constructor should be used only for tests.
    /// </summary>
    /// <param name="userSecretsId">The secrets identifier to use.</param>
    public DotNetUserSecretsStore( string userSecretsId )
    {
        _userSecretsId = userSecretsId;
    }

    /// <inheritdoc />
    public string? TryGetRequiredSecret( IActivityMonitor monitor, IEnumerable<string> keys )
    {
        Throw.CheckNotNullArgument( keys );
        Throw.CheckArgument( keys.Any() && keys.All( k => !string.IsNullOrWhiteSpace( k ) ) );
        var d = TryLoadDocument( monitor );
        if( d != null )
        {
            try
            {
                foreach( var key in keys )
                {
                    if( d.RootElement.TryGetProperty( key, out var vE ) )
                    {
                        return vE.GetString();
                    }
                }
            }
            catch( Exception ex )
            {
                monitor.Error( "While reading user secrets store.", ex );
            }
        }
        var failed = keys.Reverse().ToList();

        var more = failed.Count == 1
                        ? null
                        : $"""

                            Instead of '{failed[0]}', if you are allowed to obtain a valid secret for one of the following keys:
                            '{failed.Skip( 1 ).Concatenate( "', '" )}'
                            Register one of them as they enable more operations.
                            """;

        monitor.Error( $"""
                            This operation requires the secret '{failed[0]}'.
                            Please obtain this secret (typically a Personal Access Token) and register it on this machine:

                            dotnet user-secrets set {failed[0]} <<your-secret>> --id {_userSecretsId ?? CKliRootEnv.InstanceName}
                            {more}
                            """ );
        return null;
    }

    JsonDocument? TryLoadDocument( IActivityMonitor monitor )
    {
        _secretFilePath ??= PathHelper.GetSecretsPathFromSecretsId( _userSecretsId ?? CKliRootEnv.InstanceName );
        // A test harness registers and removes secrets while the process runs (a test that pushes must set
        // the FILESYSTEM_GIT one and clear it afterwards): the cached document must then be dropped or the
        // test would be reading a store from before its own setup.
        // This is only done when testing: for a regular ckli command the store is read once and for all,
        // its secrets must not change under the feet of a running operation.
        if( _documentLoaded && CKliRootEnv.IsTestRun && HasFileChanged() )
        {
            _document?.Dispose();
            _document = null;
            _documentLoaded = false;
        }
        if( !_documentLoaded )
        {
            _documentLoaded = true;
            try
            {
                CaptureFileState();
                if( File.Exists( _secretFilePath ) )
                {
                    var bytes = File.ReadAllBytes( _secretFilePath );
                    // Skips the BOM.
                    int start = 0;
                    if( bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF )
                    {
                        start = 3;
                    }
                    _document = JsonDocument.Parse( new System.Buffers.ReadOnlySequence<byte>( bytes, start, bytes.Length - start ),
                                                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip } );
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While reading secret store at '{_secretFilePath}'.", ex );
            }
        }
        return _document;
    }

    /// <summary>
    /// Gets whether the secret file changed since <see cref="CaptureFileState"/> has been called.
    /// A missing file is captured as a null write time and a -1 length: creating or deleting it is a change.
    /// </summary>
    bool HasFileChanged()
    {
        Throw.DebugAssert( _secretFilePath != null );
        var f = new FileInfo( _secretFilePath );
        return f.Exists
                ? f.LastWriteTimeUtc != _loadedWriteTimeUtc || f.Length != _loadedLength
                : _loadedLength != -1;
    }

    /// <summary>
    /// Captures the state of the secret file that <see cref="HasFileChanged"/> compares against.
    /// </summary>
    void CaptureFileState()
    {
        Throw.DebugAssert( _secretFilePath != null );
        var f = new FileInfo( _secretFilePath );
        if( f.Exists )
        {
            _loadedWriteTimeUtc = f.LastWriteTimeUtc;
            _loadedLength = f.Length;
        }
        else
        {
            _loadedWriteTimeUtc = default;
            _loadedLength = -1;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _document?.Dispose();
}
