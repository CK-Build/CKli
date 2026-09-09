using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin : PrimaryRepoPlugin<BranchModelInfo>
{
    readonly BranchNamespace _namespace;
    internal readonly ShallowSolutionPlugin _shallowSolution;
    readonly bool _autoFixUselessBranch;
    ITagCommitProvider? _commitProvider;

    /// <summary>
    /// Reads the <see cref="BranchNamespace"/> from the <see cref="PrimaryPluginContext.Configuration"/>.
    /// </summary>
    /// <param name="primaryContext">The CKli plugin context.</param>
    /// <param name="shallowSolution">The shallow solution plugin.</param>
    public BranchModelPlugin( PrimaryPluginContext primaryContext, ShallowSolutionPlugin shallowSolution )
        : base( primaryContext )
    {
        var configElement = primaryContext.Configuration.XElement;
        _namespace = new BranchNamespace( World.Name.LTSName,
                                          configElement.Attribute( XNames.MainLine )?.Value,
                                          configElement.Elements( XNames.Explo ) );
        _autoFixUselessBranch = (bool?)configElement.Attribute( XNames.AutoFixUselessBranch ) ?? true;
        World.Events.Issue += IssueRequested;
        World.Events.RepoAdded.Sync += OnRepoAdded;
        _shallowSolution = shallowSolution;
    }

    void OnRepoAdded( IActivityMonitor monitor, RepoAddedEventArgs e )
    {
        // Reproduce MissingRootBranchIssue.
        var git = e.GitRepository;
        var root = git.GetBranch( monitor, _namespace.Root.Name );
        if( root == null )
        {
            // Use "dev/stable" if it exists.
            var prevRoot = git.GetBranch( monitor, _namespace.Root.DevName, missingLocalAndRemote: LogLevel.Trace )
                            ?? _namespace.GetPreviousRootBranch( monitor, git );
            if( prevRoot == null )
            {
                monitor.Warn( _namespace.GetNoPreviousRootBranchFoundMessage() );
            }
            else
            {
                monitor.Info( $"Creating root branch '{_namespace.Root.Name}' from branch '{prevRoot.FriendlyName}'." );
                root = BranchLink.CreateAheadBranch( git, prevRoot.Tip, _namespace.Root.Name, withEmptyInitializationCommit: true );
            }
        }
        if( root != null )
        {
            var devRoot = BranchLink.CreateAheadBranch( git, root.Tip, _namespace.Root.DevName, withEmptyInitializationCommit: false );
            git.Checkout( monitor, devRoot );
        }
    }

    void IssueRequested( IssueEventArgs e )
    {
        var monitor = e.Monitor;
        bool hasSevereIssue = false;
        bool forgetUselessBranches = PrimaryPluginContext.Command is CKliRepoAdd or CKliRepoCreate;
        foreach( var r in e.Repos )
        {
            var info = Get( monitor, r );
            info.CollectIssues( monitor, e.ScreenType, e.Add, forgetUselessBranches, out hasSevereIssue );
        }
        if( !hasSevereIssue )
        {
            if( ContentIssue != null )
            {
                using( monitor.OpenInfo( "Raising ContentIssue event." ) )
                {
                    foreach( var r in e.Repos )
                    {
                        var info = Get( monitor, r );
                        var issueBuilder = new ContentIssueBuilder( info, RaiseContentIssue );
                        if( !issueBuilder.CreateIssue( monitor, e.Context, e.Add ) )
                        {
                            monitor.CloseGroup( $"ContentIssue event handling failed." );
                            // Stop on the first error.
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Updates the configuration with an updated <paramref name="ns"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="ns">The new namespace to consider.</param>
    /// <returns>True on success, false on error.</returns>
    bool SaveBranchNamespace( IActivityMonitor monitor, BranchNamespace ns )
    {
        return PrimaryPluginContext.Configuration.Edit( monitor, ( monitor, e ) =>
        {
            e.SetAttributeValue( XNames.MainLine, ns.GetMainLine() );
            e.Elements( XNames.Explo ).Remove();
            e.Add( ns.GetExplo() );
        } );
    }

    bool RaiseContentIssue( IActivityMonitor monitor, ContentIssueEventArgs e )
    {
        Throw.DebugAssert( ContentIssue != null );
        bool success = true;
        using( monitor.OnError( () => success = false ) )
        {
            ContentIssue( e );
        }
        return success;
    }

    /// <summary>
    /// Gets the branch model.
    /// </summary>
    public BranchNamespace BranchNamespace => _namespace;

    /// <summary>
    /// Raised when repository content issues must be detected in the hot zone.
    /// <para>
    /// Any <see cref="LogLevel.Error"/> or <see cref="LogLevel.Fatal"/> emitted in <see cref="EventMonitoredArgs.Monitor">ContentIssueEvent.Monitor</see>
    /// is detected as an error that fails the issue command.
    /// </para>
    /// </summary>
    public event Action<ContentIssueEventArgs>? ContentIssue;

    /// <summary>
    /// Sets the <see cref="ITagCommitProvider"/> required to support <see cref="HotBranch.Synchronize(IActivityMonitor, BranchLinkType)"/>
    /// with <see cref="BranchLinkType.Release"/> and <see cref="BranchLinkType.CI"/>.
    /// </summary>
    /// <param name="commitProvider">The commit provider.</param>
    /// <remarks>
    /// This is obviously not elegant but it is easier than making the plugin DI support abstraction injection.
    /// </remarks>
    public void SetTagCommitProvider( ITagCommitProvider commitProvider )
    {
        _commitProvider = commitProvider;
    }

    /// <summary>
    /// Exposes this plugin info.
    /// </summary>
    public PluginInfo PluginInfo => PrimaryPluginContext.PluginInfo;

    internal ITagCommitProvider? TagCommitProvider => _commitProvider;

    /// <summary>
    /// <see cref="BranchModelInfo"/> factory.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository to consider.</param>
    /// <returns>The branch information for the repository.</returns>
    protected override BranchModelInfo Create( IActivityMonitor monitor, Repo repo ) => Create( monitor, repo, _namespace, _autoFixUselessBranch );

    BranchModelInfo Create( IActivityMonitor monitor, Repo repo, BranchNamespace ns, bool autoFixUselessBranch )
    {
        bool isCKliIssueCommand = PrimaryPluginContext.Command is CKliIssue;
        bool isCKliRepoAddOrCreate = PrimaryPluginContext.Command is CKliRepoCreate or CKliRepoAdd;
        // We don't want to auto fix when executing "ckli issue" and we don't want to remove the "dev/stable"
        // that have been created by the repo create or add.
        autoFixUselessBranch &= !isCKliIssueCommand && !isCKliRepoAddOrCreate;
        var info = new BranchModelInfo( repo, ns, this );
        var git = repo.GitRepository.Repository;

        var root = HotBranch.Create( monitor, info, repo.GitRepository, ns.Root );
        if( root.GitBranch == null )
        {
            if( !isCKliIssueCommand )
            {
                monitor.Warn( $"Missing '{root.BranchName}' branch in '{repo.DisplayPath}'. Use 'ckli issue' for details." );
            }
            // The worst issue: no root "stable" branch. This has to be resolved before doing anything else.
            info.Initialize( [root], hasIssue: true );
            return info;
        }
        // We have our hot root "stable" branch.
        bool hasIssue = root.HasIssue( monitor, autoFixUselessBranch );
        var hotBranches = new HotBranch[ns.Branches.Length];
        hotBranches[0] = root;
        for( int i = 1; i < hotBranches.Length; ++i )
        {
            var branchName = ns.Branches[i];
            var b = HotBranch.Create( monitor, info, repo.GitRepository, branchName );
            hasIssue |= b.HasIssue( monitor, autoFixUselessBranch );
            hotBranches[i] = b;
        }
        info.Initialize( ImmutableCollectionsMarshal.AsImmutableArray( hotBranches ), hasIssue );
        return info;
    }

    /// <summary>
    /// Tries to parse "fix/v<paramref name="major"/>.<paramref name="minor"/>".
    /// </summary>
    /// <param name="s">The name to parse.</param>
    /// <param name="major">The major version to fix.</param>
    /// <param name="minor">The minor version to fix.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParseBranchFixName( ReadOnlySpan<char> s, out int major, out int minor )
    {
        major = 0;
        minor = 0;
        return s.TryMatch( "fix/" )
               && s.TryMatch( 'v' )
               && s.TryMatchInteger( out major )
               && major >= 0
               && s.TryMatch( '.' )
               && s.TryMatchInteger( out minor )
               && minor >= 0
               && s.Length == 0;
    }

    bool GetReposAndBranch( IActivityMonitor monitor,
                            CKliEnv context,
                            bool all,
                            string branchName,
                            [NotNullWhen( true )] out IReadOnlyList<Repo>? repos,
                            [NotNullWhen( true )] out BranchName? branch,
                            out bool isDevName )
    {
        isDevName = branchName.StartsWith( "dev/" );
        repos = all
                ? World.GetAllDefinedRepo( monitor )
                : World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        if( repos == null )
        {
            branch = null;
            return false;
        }
        if( isDevName ) branchName = branchName.Substring( 4 );
        branch = _namespace.FindRequired( monitor, branchName );
        return branch != null;
    }
}

