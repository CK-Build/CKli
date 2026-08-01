using CK.Core;
using CKli.Core;
using System.Linq;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{
    [Description( """Bumps the current repository version number by setting a "+fake" version tag on the "<root>" or "dev/<root>" branch.""" )]
    [CommandPath( "version bump" )]
    public bool VersionBump( IActivityMonitor monitor,
                             CKliEnv context,
                             [Description("The new starting version. Must be a stable Major.Minor.Patch (no -prelease nor +metadata suffix).")]
                             string version )
    {
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null ) return false;

        var v = SVersion.ParseNoThrow( version );
        if( !v.IsValid )
        {
            monitor.Error( v.ErrorMessage );
            return false;
        }
        if( v.IsPrerelease || v.BuildMetaData.Length > 0 )
        {
            monitor.Error( """The version must be a stable Major.Minor.Patch (no -prelease nor +metadata suffix).""" );
            return false;
        }
        var branchInfo = _branchModel.Get( monitor, repo );
        if( branchInfo.Root.GitBranch == null )
        {
            monitor.Error( $"""The root branch '{branchInfo.Namespace.Root}' doesn't exist. Use 'ckli issue --fix' to fix this.""" );
            return false;
        }
        var branch = branchInfo.Root.GitDevBranch ?? branchInfo.Root.GitBranch;
        
        var versionInfo = Get( monitor, repo );
        if( versionInfo.SupVersion != null && v >= versionInfo.SupVersion )
        {
            monitor.Error( $"""Provided version must be lower than configured SupVersion="{versionInfo.SupVersion.ParsedText}".""" );
            return false;
        }
        if( versionInfo.InfVersion != null && v <= versionInfo.InfVersion )
        {
            monitor.Error( $"""Provided version must be greater than configured InfVersion="{versionInfo.InfVersion.ParsedText}".""" );
            return false;
        }
        //
        // Ignore +fake (even if they are published by design). What matters are only non fake published version (regular or deprecated). 
        var maxVersion = versionInfo.AllVersions.Select( tc => tc.Version ).Where( v => !v.HasFakeMetadata && !v.IsLocal() ).Max();
        if( v <= maxVersion )
        {
            monitor.Error( $"""Provided version must be greater than the current maximal version "{maxVersion.ParsedText}".""" );
            return false;
        }
        monitor.Error( $"""Not yet implemented.""" );
        return false;

        // We remove all the "local/" versions.
        var cleanupLocals = versionInfo.AllVersions.Select( tc => tc.Version ).Where( v => v.IsLocal() ).ToList();
        if( cleanupLocals.Count > 0 )
        {
            using( monitor.OpenInfo( $"""Destroying {cleanupLocals.Count} "local/" versions: {cleanupLocals.Select( v => v.ParsedText ).Concatenate()}.""" ) )
            {
                bool success = true;
                foreach( var local in cleanupLocals )
                {
                    success &= DestroyLocalRelease( monitor, repo, local );
                }
            }
        }
        // We remove all the fake versions that are equal or greater to the new version.
        // Note that fake version 
        var cleanupFake = versionInfo.AllVersions.Where( tc => tc.Version.HasFakeMetadata && tc.Version >= v ).ToList();
        if( cleanupFake.Count > 0 )
        {
            using( monitor.OpenInfo( $"""Destroying {cleanupFake.Count} +fake versions: {cleanupFake.Select( v => v.Version.ParsedText ).Concatenate()}.""" ) )
            {
                foreach( var (fakeVersion,tag,tc) in cleanupFake )
                {

                }
            }
        }
    }


}
