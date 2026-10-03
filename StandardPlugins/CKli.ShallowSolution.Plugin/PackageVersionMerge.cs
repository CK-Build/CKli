using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LogLevel = CK.Core.LogLevel;

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
/// The versions are aligned before merging rather than repaired afterwards, across the whole solution: a
/// solution references a package identifier in one version, so each side has one version per package. Every
/// package whose version differs between the two sides is set to its resolved version in all the project files
/// of the solution of both sides (a text edit of the attribute value: nothing else in the files changes). These
/// are the projects of the ".slnx" and their "Directory.*.props" (CommonSolution.LoadAllProjectFiles): a project
/// file that is not in the solution, a template for instance, is neither read nor rewritten. The two aligned trees
/// are then merged by git, renames included: the version lines have been changed the same way on both sides and
/// don't conflict anymore, so any conflict that remains is a real one. The merge commit has the aligned merge's
/// tree and the two original commits as its parents.
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
    /// <param name="failureLevel">
    /// The level of the log that explains why the merge failed. A caller that only probes whether the merge is possible
    /// uses a level that doesn't reach the screen.
    /// </param>
    /// <returns>The merge commit or null if the merge conflicts (beyond package versions) or on error.</returns>
    public static Commit? CreateMergeCommit( IActivityMonitor monitor,
                                             GitRepository git,
                                             Commit ours,
                                             Commit theirs,
                                             string oursName,
                                             string theirsName,
                                             Func<IPackageVersionResolver?> getResolver,
                                             out IReadOnlyList<AlignedVersion> aligned,
                                             LogLevel failureLevel = LogLevel.Error )
    {
        aligned = [];
        var db = git.Repository.ObjectDatabase;
        var merge = db.MergeCommits( ours, theirs, new MergeTreeOptions { SkipReuc = true } );
        if( merge.Status != MergeTreeStatus.Conflicts )
        {
            return db.CreateCommit( git.Author, git.Committer, $"Merged {theirsName}.", merge.Tree, [ours, theirs], prettifyMessage: true );
        }
        // A renamed file appears with its old and its new path: every path of a conflict must be a project file.
        var notProjects = merge.Conflicts.SelectMany( c => new[] { c.Ancestor, c.Ours, c.Theirs } )
                                         .Where( e => e != null && GetFileKind( e.Path ) == FileKind.None )
                                         .Select( e => e.Path )
                                         .Distinct()
                                         .ToList();
        if( notProjects.Count > 0 )
        {
            monitor.Log( failureLevel, $"""
                Failed merging {theirsName} into '{oursName}' in '{git.DisplayPath}'. Conflicts in:
                {notProjects.Concatenate( Environment.NewLine )}
                This must be fixed manually.
                """ );
            return null;
        }
        var resolver = getResolver();
        if( resolver == null )
        {
            monitor.Log( failureLevel, $"""
                Failed merging {theirsName} into '{oursName}' in '{git.DisplayPath}': the package versions that conflict cannot be resolved (see above).
                This must be fixed manually.
                """ );
            return null;
        }
        var alignedVersions = AlignSides( monitor, git, ours, theirs, resolver, out var oursAligned, out var theirsAligned );
        if( alignedVersions.Count > 0 )
        {
            merge = db.MergeCommits( oursAligned, theirsAligned, new MergeTreeOptions { SkipReuc = true } );
        }
        if( merge.Status == MergeTreeStatus.Conflicts )
        {
            monitor.Log( failureLevel, $"""
                Failed merging {theirsName} into '{oursName}' in '{git.DisplayPath}'. Beyond the package versions, conflicts in:
                {merge.Conflicts.SelectMany( c => new[] { c.Ancestor, c.Ours, c.Theirs } ).Where( e => e != null ).Select( e => e.Path ).Distinct().Concatenate( Environment.NewLine )}
                This must be fixed manually.
                """ );
            return null;
        }
        aligned = alignedVersions;
        return db.CreateCommit( git.Author, git.Committer, $"Merged {theirsName}.", merge.Tree, [ours, theirs], prettifyMessage: true );
    }

    /// <summary>
    /// Leaves the merge of <paramref name="theirs"/> into the <paramref name="target"/> branch in progress in the
    /// working folder, for the conflicts that remain once the package versions are aligned (see
    /// <see cref="CreateMergeCommit"/>): a "merge in progress" that a person resolves and commits with any Git tool.
    /// <para>
    /// The <paramref name="target"/> is checked out with its versions aligned (detached) and the aligned
    /// <paramref name="theirs"/> is merged without committing: the working folder and the index hold the merge, with
    /// conflict markers only where the conflicts are real. The HEAD is then the <paramref name="target"/> branch again
    /// (the index and the working folder are not touched) and MERGE_HEAD is the original <paramref name="theirs"/>:
    /// the merge commit that the person creates has the two original commits as parents. Aborting the merge
    /// ("git merge --abort") restores the <paramref name="target"/> branch as it was.
    /// </para>
    /// <para>
    /// When <paramref name="getResolver"/> provides no resolver, nothing is aligned: the package versions conflict too.
    /// The working folder must be clean.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="git">The repository.</param>
    /// <param name="target">The branch that receives the merge.</param>
    /// <param name="theirs">The commit to merge.</param>
    /// <param name="theirsName">What is merged, for the merge message "Merged {theirsName}." ("branch 'X'" or "commit 'sha message'").</param>
    /// <param name="getResolver">Provides the resolver of the conflicting versions.</param>
    /// <param name="conflicts">Outputs the paths that are in conflict in the working folder.</param>
    /// <returns>True when the merge is in progress, false on error.</returns>
    public static bool PrepareMerge( IActivityMonitor monitor,
                                     GitRepository git,
                                     Branch target,
                                     Commit theirs,
                                     string theirsName,
                                     Func<IPackageVersionResolver?> getResolver,
                                     out IReadOnlyList<string> conflicts )
    {
        conflicts = [];
        if( !git.CheckCleanCommit( monitor ) ) return false;
        var repository = git.Repository;
        var ours = target.Tip;
        Commit oursAligned = ours;
        Commit theirsAligned = theirs;
        var resolver = getResolver();
        if( resolver == null )
        {
            monitor.Warn( $"The package versions of '{git.DisplayPath}' cannot be aligned (see above): they conflict too." );
        }
        else
        {
            AlignSides( monitor, git, ours, theirs, resolver, out oursAligned, out theirsAligned );
        }
        try
        {
            Commands.Checkout( repository, oursAligned );
            var result = repository.Merge( theirsAligned,
                                           git.Committer,
                                           new MergeOptions { CommitOnSuccess = false, FastForwardStrategy = FastForwardStrategy.NoFastForward } );
            if( result.Status != MergeStatus.Conflicts )
            {
                monitor.Error( $"Unexpected merge status '{result.Status}' while preparing the merge of {theirsName} into '{target.FriendlyName}' in '{git.DisplayPath}'." );
                Commands.Checkout( repository, target, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force } );
                return false;
            }
            // HEAD is the target branch again: only the reference changes, the index and the working folder
            // hold the merge. MERGE_HEAD, MERGE_MSG and ORIG_HEAD are the ones of a merge of the original commits.
            repository.Refs.UpdateTarget( repository.Refs.Head, repository.Refs[target.CanonicalName] );
            var gitFolder = repository.Info.Path;
            File.WriteAllText( Path.Combine( gitFolder, "MERGE_HEAD" ), theirs.Sha + "\n" );
            File.WriteAllText( Path.Combine( gitFolder, "ORIG_HEAD" ), ours.Sha + "\n" );
            File.WriteAllText( Path.Combine( gitFolder, "MERGE_MSG" ), $"Merged {theirsName}.\n" );
            conflicts = repository.Index.Conflicts.Select( c => (c.Ours ?? c.Theirs ?? c.Ancestor).Path ).Distinct().ToList();
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While preparing the merge of {theirsName} into '{target.FriendlyName}' in '{git.DisplayPath}'.", ex );
            return false;
        }
    }

    // Aligns the package versions that differ between the two sides: the aligned commits are children of their original
    // commit (the merge base stays the same), or the original commits themselves when nothing differs.
    static List<AlignedVersion> AlignSides( IActivityMonitor monitor,
                                            GitRepository git,
                                            Commit ours,
                                            Commit theirs,
                                            IPackageVersionResolver resolver,
                                            out Commit oursAligned,
                                            out Commit theirsAligned )
    {
        var db = git.Repository.ObjectDatabase;
        // The project files of each side are the ones of its solution: a repository can contain project files
        // that are not built (a template, for instance) and whose references mean nothing.
        var oursFiles = ReadSolutionFiles( monitor, git, ours );
        var theirsFiles = ReadSolutionFiles( monitor, git, theirs );
        var oursVersions = ReadVersions( oursFiles );
        var theirsVersions = ReadVersions( theirsFiles );
        var target = new Dictionary<(string Id, string Attribute), string>( IdComparer.Instance );
        var alignedVersions = new List<AlignedVersion>();
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
        if( alignedVersions.Count == 0 )
        {
            oursAligned = ours;
            theirsAligned = theirs;
        }
        else
        {
            oursAligned = db.CreateCommit( git.Committer, git.Committer, "Aligned package versions.", Align( db, ours.Tree, oursFiles, target ), [ours], prettifyMessage: false );
            theirsAligned = db.CreateCommit( git.Committer, git.Committer, "Aligned package versions.", Align( db, theirs.Tree, theirsFiles, target ), [theirs], prettifyMessage: false );
        }
        return alignedVersions;
    }

    sealed record ProjectFile( string Path, FileKind Kind, string Text, bool HasBom, Mode Mode );

    // The project files of the commit's solution (see CommonSolution.LoadAllProjectFiles): none when the commit has no
    // solution or when it cannot be read.
    static List<ProjectFile> ReadSolutionFiles( IActivityMonitor monitor, GitRepository git, Commit commit )
    {
        var result = new List<ProjectFile>();
        var files = INormalizedFileProvider.GetFiles( commit, useWorkingFolder: false );
        var solution = files.GetFileInfo( git.DisplayPath.LastPart + ".slnx" );
        if( solution == null ) return result;
        XDocument doc;
        using( var stream = solution.CreateReadStream() )
        {
            doc = XDocument.Load( stream );
        }
        if( doc.Root == null ) return result;
        CommonSolution.LoadAllProjectFiles( monitor, files, doc.Root, LoadOptions.None, ( _, path, _ ) =>
        {
            var kind = GetFileKind( path.Path );
            if( kind != FileKind.None && commit[path.Path] is { TargetType: TreeEntryTargetType.Blob } e )
            {
                var text = ReadText( (Blob)e.Target, out bool hasBom );
                result.Add( new ProjectFile( path.Path, kind, text, hasBom, e.Mode ) );
            }
            return true;
        } );
        return result;
    }

    // The tree with the target versions applied to its project files.
    static Tree Align( ObjectDatabase db, Tree tree, List<ProjectFile> files, Dictionary<(string Id, string Attribute), string> target )
    {
        var definition = TreeDefinition.From( tree );
        foreach( var f in files )
        {
            var text = Rewrite( f.Text, f.Kind, target );
            if( text != f.Text )
            {
                definition.Add( f.Path, CreateBlob( db, text, f.HasBom ), f.Mode );
            }
        }
        return db.CreateTree( definition );
    }

    // The versions referenced by the project files of a side: one per package (the first one met).
    static Dictionary<(string Id, string Attribute), string> ReadVersions( List<ProjectFile> files )
    {
        var result = new Dictionary<(string, string), string>( IdComparer.Instance );
        foreach( var f in files )
        {
            foreach( var (key, version) in ReadVersions( f.Text, f.Kind ) )
            {
                result.TryAdd( key, version );
            }
        }
        return result;
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
