using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CKli.Core;

public sealed partial class GitRepository
{
    const string _persistentDeferredPushFileName = "CKLI_DEFERRED_PUSH";
    HashSet<string>? _persistentDeferredPushRefSpecs;

    /// <summary>
    /// Gets the ref specs that will be pushed with the next successful <see cref="Push"/>, be it in this process
    /// or in any subsequent one: they are saved in the "CKLI_DEFERRED_PUSH" file of the ".git" folder and are cleared
    /// only once pushed. Use <see cref="AddPersistentDeferredPushRefSpecs(IActivityMonitor, IEnumerable{string})"/> to
    /// register new ones.
    /// <para>
    /// This complements the <see cref="DeferredPushRefSpecs"/> that are lost when the process ends without a push: this is
    /// deliberate for them, a failing process must not impact the remotes. Persistent ones are for local changes that are
    /// complete by themselves and that must be shared, typically version tags that have been rebuilt: forgetting them
    /// leaves the other clones with stale references.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> PersistentDeferredPushRefSpecs => GetPersistentDeferredPushRefSpecs();

    /// <summary>
    /// Adds ref specs to the <see cref="PersistentDeferredPushRefSpecs"/> and saves them.
    /// <para>
    /// A ref spec for which <see cref="IsRefusedPushRefSpec(ReadOnlySpan{char})"/> is true is an error: nothing is added.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="refSpecs">The ref specs to push with the next successful push.</param>
    /// <returns>True on success, false on error.</returns>
    public bool AddPersistentDeferredPushRefSpecs( IActivityMonitor monitor, IEnumerable<string> refSpecs )
    {
        var specs = refSpecs.ToList();
        var refused = specs.Where( spec => IsRefusedPushRefSpec( spec ) ).ToList();
        if( refused.Count > 0 )
        {
            monitor.Error( $"Ref specs '{refused.Concatenate( "', '" )}' cannot be pushed from '{DisplayPath}': 'local/' and 'building/' references must never be pushed (nor wildcards that may match them)." );
            return false;
        }
        var set = GetPersistentDeferredPushRefSpecs();
        bool changed = false;
        foreach( var spec in specs ) changed |= set.Add( spec );
        return !changed || SavePersistentDeferredPushRefSpecs( monitor );
    }

    string PersistentDeferredPushFilePath => Path.Combine( _git.Info.Path, _persistentDeferredPushFileName );

    HashSet<string> GetPersistentDeferredPushRefSpecs()
    {
        if( _persistentDeferredPushRefSpecs == null )
        {
            var path = PersistentDeferredPushFilePath;
            _persistentDeferredPushRefSpecs = File.Exists( path )
                                                ? new HashSet<string>( File.ReadAllLines( path ).Where( l => !string.IsNullOrWhiteSpace( l ) ) )
                                                : new HashSet<string>();
        }
        return _persistentDeferredPushRefSpecs;
    }

    bool SavePersistentDeferredPushRefSpecs( IActivityMonitor monitor )
    {
        Throw.DebugAssert( _persistentDeferredPushRefSpecs != null );
        var path = PersistentDeferredPushFilePath;
        try
        {
            if( _persistentDeferredPushRefSpecs.Count == 0 )
            {
                File.Delete( path );
            }
            else
            {
                File.WriteAllLines( path, _persistentDeferredPushRefSpecs );
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While saving the persistent deferred push ref specs of '{DisplayPath}'.", ex );
            return false;
        }
    }
}
