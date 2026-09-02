using CK.Core;
using System.Collections.Generic;

namespace CKli.Core.Tests;

/// <summary>
/// Mock secrets store that records which keys were requested. 
/// </summary>
sealed class RecordingSecretsStore : ISecretsStore
{
    public List<string[]> RequestedKeys { get; } = new();

    public string? TryGetRequiredSecret( IActivityMonitor monitor, IEnumerable<string> keys )
    {
        var keyArray = keys is string[] arr ? arr : new List<string>( keys ).ToArray();
        RequestedKeys.Add( keyArray );
        // No secret found. The error matters as much as the null: the real store explains how to register
        // the secret and a silent mock turns a miswired store into a bare "false" (it once hid a
        // GetWriteCredentials failure behind a PushBranch that logged nothing).
        monitor.Error( $"RecordingSecretsStore has no secret for '{keyArray.Concatenate( "', '" )}'." );
        return null;
    }
}
