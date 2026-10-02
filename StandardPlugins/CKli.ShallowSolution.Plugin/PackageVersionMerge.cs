using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Merges two commits whose conflicts are package versions only.
/// <para>
/// Two branches that are built independently both rewrite the references to the World's packages: a merge
/// between them typically conflicts on the very same &lt;PackageReference Version="..." /&gt; lines, each side
/// referencing its own build. Git cannot merge them, but a <see cref="IPackageVersionResolver"/> knows the
/// version both sides must reference.
/// </para>
/// <para>
/// The versions are aligned before merging rather than repaired afterwards: in the conflicting project files
/// of both sides, every package whose version differs between the two sides is set to its resolved version
/// (a text edit of the attribute value: nothing else in the files changes). The two aligned trees are then
/// merged: the version lines have been changed the same way on both sides and don't conflict anymore, so any
/// conflict that remains is a real one. The merge commit has the aligned merge's tree and the two original
/// commits as its parents.
/// </para>
/// <para>
/// The project files are the ones <see cref="MutableSolution.UpdatePackages"/> updates: the &lt;PackageVersion&gt;
/// of a "Directory.Packages.props", the &lt;PackageReference&gt; (Version and VersionOverride) of any other
/// project file and "Directory.Build.props". A conflict in any other file is a real conflict.
/// </para>
/// </summary>
public static partial class PackageVersionMerge
{
    /// <summary>
    /// A package version that has been aligned.
    /// </summary>
    /// <param name="PackageId">The package identifier.</param>
    /// <param name="Ours">The version referenced by the branch that receives the merge.</param>
    /// <param name="Theirs">The version referenced by the merged commit.</param>
    /// <param name="Resolved">The version both sides now reference.</param>
    public readonly record struct AlignedVersion( string PackageId, SVersion Ours, SVersion Theirs, SVersion Resolved );

    [GeneratedRegex( """<(?<e>PackageReference|PackageVersion)\b(?<a>[^>]*)>""" )]
    private static partial Regex ElementRegex();

    [GeneratedRegex( """\b(?<n>Include|Version|VersionOverride)\s*=\s*(?<q>["'])(?<v>.*?)\k<q>""" )]
    private static partial Regex AttributeRegex();

    /// <summary>
    /// Creates the merge commit of <paramref name="theirs"/> into <paramref name="ours"/>, aligning the package
    /// versions they reference differently when the merge conflicts. This creates objects in the object database
    /// only: no branch moves, the working folder is not touched.
    /// <para>
    /// The <paramref name="getResolver"/> is called only when the merge conflicts and all the conflicts are in project
    /// files: a null resolver (that must have logged why) fails the merge.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="git">The repository.</param>
    /// <param name="ours">The commit that receives the merge (the tip of the target branch).</param>
    /// <param name="theirs">The commit to merge.</param>
    /// <param name="oursName">The name of the branch that receives the merge.</param>
    /// <param name="theirsName">What is merged, for the merge commit message "Merged {theirsName}." and the logs ("branch 'X'" or "commit 'sha message'").</param>
    /// <param name="getResolver">Provides the resolver of the conflicting versions.</param>
    /// <param name="aligned">Outputs the package versions that have been aligned (empty when the merge doesn't conflict).</param>
    /// <returns>The merge commit or null if the merge conflicts (beyond package versions) or on error.</returns>
    public static Commit? CreateMergeCommit( IActivityMonitor monitor,
                                             GitRepository git,
                                             Commit ours,
                                             Commit theirs,
                                             string oursName,
                                             string theirsName,
                                             Func<IPackageVersionResolver?> getResolver,
                                             out IReadOnlyList<AlignedVersion> aligned )
    {
        aligned = [];
        var db = git.Repository.ObjectDatabase;
        var merge = db.MergeCommits( ours, theirs, new MergeTreeOptions { SkipReuc = true } );
        if( merge.Status != MergeTreeStatus.Conflicts )
        {
            return db.CreateCommit( git.Author, git.Committer, $"Merged {theirsName}.", merge.Tree, [ours, theirs], prettifyMessage: true );
        }
        var conflicts = merge.Conflicts.ToList();
        var notProjects = conflicts.Where( c => c.Ours == null || c.Theirs == null || GetFileKind( c.Ours.Path ) == FileKind.None )
                                   .Select( c => (c.Ours ?? c.Theirs ?? c.Ancestor).Path )
                                   .ToList();
        if( notProjects.Count > 0 )
        {
            monitor.Error( $"""
                Failed merging {theirsName} into '{oursName}' in '{git.DisplayPath}'. Conflicts in:
                {notProjects.Concatenate( Environment.NewLine )}
                This must be fixed manually.
                """ );
            return null;
        }
        var resolver = getResolver();
        if( resolver == null )
        {
            monitor.Error( $"""
                Failed merging {theirsName} into '{oursName}' in '{git.DisplayPath}': the package versions that conflict cannot be resolved (see above).
                This must be fixed manually.
                """ );
            return null;
        }
        var oursTree = TreeDefinition.From( ours.Tree );
        var theirsTree = TreeDefinition.From( theirs.Tree );
        var alignedVersions = new List<AlignedVersion>();
        foreach( var c in conflicts )
        {
            var kind = GetFileKind( c.Ours.Path );
            var oursText = ReadText( git.Repository.Lookup<Blob>( c.Ours.Id ), out bool oursBom );
            var theirsText = ReadText( git.Repository.Lookup<Blob>( c.Theirs.Id ), out bool theirsBom );
            var oursVersions = ReadVersions( oursText, kind );
            var theirsVersions = ReadVersions( theirsText, kind );
            var target = new Dictionary<(string Id, string Attribute), string>( IdComparer.Instance );
            foreach( var (key, o) in oursVersions )
            {
                if( theirsVersions.TryGetValue( key, out var t )
                    && o != t
                    && SVersion.TryParse( o, out var vO )
                    && SVersion.TryParse( t, out var vT ) )
                {
                    var resolved = resolver.Resolve( key.Id, vO, vT );
                    target.Add( key, resolved.ToString() );
                    if( !alignedVersions.Any( a => a.PackageId.Equals( key.Id, StringComparison.OrdinalIgnoreCase ) ) )
                    {
                        alignedVersions.Add( new AlignedVersion( key.Id, vO, vT, resolved ) );
                    }
                }
            }
            if( target.Count == 0 ) continue;
            oursTree.Add( c.Ours.Path, CreateBlob( db, Rewrite( oursText, kind, target ), oursBom ), c.Ours.Mode );
            theirsTree.Add( c.Theirs.Path, CreateBlob( db, Rewrite( theirsText, kind, target ), theirsBom ), c.Theirs.Mode );
        }
        if( alignedVersions.Count > 0 )
        {
            // Each aligned side is a child of its original commit: the merge base stays the same.
            var oursAligned = db.CreateCommit( git.Committer, git.Committer, "Aligned package versions.", db.CreateTree( oursTree ), [ours], prettifyMessage: false );
            var theirsAligned = db.CreateCommit( git.Committer, git.Committer, "Aligned package versions.", db.CreateTree( theirsTree ), [theirs], prettifyMessage: false );
            merge = db.MergeCommits( oursAligned, theirsAligned, new MergeTreeOptions { SkipReuc = true } );
        }
        if( merge.Status == MergeTreeStatus.Conflicts )
        {
            monitor.Error( $"""
                Failed merging {theirsName} into '{oursName}' in '{git.DisplayPath}'. Beyond the package versions, conflicts in:
                {merge.Conflicts.Select( c => (c.Ours ?? c.Theirs ?? c.Ancestor).Path ).Concatenate( Environment.NewLine )}
                This must be fixed manually.
                """ );
            return null;
        }
        aligned = alignedVersions;
        return db.CreateCommit( git.Author, git.Committer, $"Merged {theirsName}.", merge.Tree, [ours, theirs], prettifyMessage: true );
    }

    enum FileKind
    {
        None,
        Project,
        PackagesProps
    }

    static FileKind GetFileKind( string path )
    {
        var name = Path.GetFileName( path.AsSpan() );
        if( name.Equals( "Directory.Packages.props", StringComparison.OrdinalIgnoreCase ) ) return FileKind.PackagesProps;
        if( name.Equals( "Directory.Build.props", StringComparison.OrdinalIgnoreCase )
            || Path.GetExtension( name ).EndsWith( "proj", StringComparison.OrdinalIgnoreCase ) )
        {
            return FileKind.Project;
        }
        return FileKind.None;
    }

    static string ReadText( Blob blob, out bool hasBom )
    {
        using var s = blob.GetContentStream();
        using var m = new MemoryStream();
        s.CopyTo( m );
        var bytes = m.GetBuffer().AsSpan( 0, (int)m.Length );
        hasBom = bytes.StartsWith( Encoding.UTF8.Preamble );
        return Encoding.UTF8.GetString( hasBom ? bytes.Slice( 3 ) : bytes );
    }

    static Blob CreateBlob( ObjectDatabase db, string text, bool hasBom )
    {
        var bytes = Encoding.UTF8.GetBytes( text );
        using var m = new MemoryStream();
        if( hasBom ) m.Write( Encoding.UTF8.Preamble );
        m.Write( bytes );
        m.Position = 0;
        return db.CreateBlob( m );
    }

    // A PackageVersion in a "Directory.Packages.props", a PackageReference everywhere else.
    static bool IsVersionElement( Match element, FileKind kind ) => element.Groups["e"].Value == (kind == FileKind.PackagesProps ? "PackageVersion" : "PackageReference");

    static Dictionary<(string Id, string Attribute), string> ReadVersions( string text, FileKind kind )
    {
        var result = new Dictionary<(string, string), string>( IdComparer.Instance );
        foreach( Match e in ElementRegex().Matches( text ) )
        {
            if( !IsVersionElement( e, kind ) ) continue;
            string? id = null;
            var versions = new List<(string Attribute, string Value)>();
            foreach( Match a in AttributeRegex().Matches( e.Groups["a"].Value ) )
            {
                var n = a.Groups["n"].Value;
                if( n == "Include" ) id = a.Groups["v"].Value;
                else versions.Add( (n, a.Groups["v"].Value) );
            }
            if( id == null ) continue;
            foreach( var (attribute, value) in versions )
            {
                // A repository references a package identifier in one version only: the first one wins.
                result.TryAdd( (id, attribute), value );
            }
        }
        return result;
    }

    static string Rewrite( string text, FileKind kind, Dictionary<(string Id, string Attribute), string> target )
    {
        return ElementRegex().Replace( text, e =>
        {
            if( !IsVersionElement( e, kind ) ) return e.Value;
            var attributes = e.Groups["a"];
            var id = AttributeRegex().Matches( attributes.Value ).FirstOrDefault( a => a.Groups["n"].Value == "Include" )?.Groups["v"].Value;
            if( id == null ) return e.Value;
            var newAttributes = AttributeRegex().Replace( attributes.Value, a =>
            {
                var v = a.Groups["v"];
                return a.Groups["n"].Value != "Include" && target.TryGetValue( (id, a.Groups["n"].Value), out var to )
                        ? string.Concat( a.Value.AsSpan( 0, v.Index - a.Index ), to, a.Value.AsSpan( v.Index - a.Index + v.Length ) )
                        : a.Value;
            } );
            return string.Concat( e.Value.AsSpan( 0, attributes.Index - e.Index ), newAttributes, e.Value.AsSpan( attributes.Index - e.Index + attributes.Length ) );
        } );
    }

    sealed class IdComparer : IEqualityComparer<(string Id, string Attribute)>
    {
        public static readonly IdComparer Instance = new();

        public bool Equals( (string Id, string Attribute) x, (string Id, string Attribute) y ) => StringComparer.OrdinalIgnoreCase.Equals( x.Id, y.Id ) && x.Attribute == y.Attribute;

        public int GetHashCode( (string Id, string Attribute) obj ) => HashCode.Combine( StringComparer.OrdinalIgnoreCase.GetHashCode( obj.Id ), obj.Attribute );
    }
}
