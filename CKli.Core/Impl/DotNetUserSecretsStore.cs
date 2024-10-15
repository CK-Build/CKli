using Microsoft.Extensions.Configuration.UserSecrets;
using CK.Core;
using System.Text.Json;
using System.Threading;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// Implements <see cref="ISecretsStore"/> on dotnet user-secrets (with Id = "CKli").
/// </summary>
public sealed class DotNetUserSecretsStore : ISecretsStore, IDisposable
{
    static string? _secretFilePath;

    // No Empty pattern. https://github.com/dotnet/runtime/issues/59303
    JsonDocument? _document;
    bool _documentLoaded;
    
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

                            dotnet user-secrets set {failed[0]} <<your-secret>> --id CKli
                            {more}
                            """ );
        return null;
    }

    JsonDocument? TryLoadDocument( IActivityMonitor monitor )
    {
        if( !_documentLoaded )
        {
            _documentLoaded = true;
            _secretFilePath ??= PathHelper.GetSecretsPathFromSecretsId( "CKli" );
            try
            {
                if( File.Exists( _secretFilePath ) )
                {
                    _document = JsonDocument.Parse( File.ReadAllBytes( _secretFilePath ),
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

    public void Dispose() => _document?.Dispose();
}
