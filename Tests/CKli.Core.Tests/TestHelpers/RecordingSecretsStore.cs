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
        // Return null to indicate no secret found.
        return null; 
    }
}
