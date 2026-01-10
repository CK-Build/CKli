using CK.Core;
using System;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// A world's repository. Encapsulates a <see cref="GitRepository"/>.
/// </summary>
public sealed class Repo
{
    readonly World _world;
    // World.Dispose() disposes the Git repository.
    internal readonly GitRepository _git;
    // For PrimaryPluginContext.GetConfigurationFor( Repo ).
    internal readonly XElement _configuration;
    readonly int _index;
    readonly RandomId _repoId;
    internal readonly Repo? _nextRepo;
    GitRepository.SimpleStatusInfo _status;

    internal Repo( World world, GitRepository git, XElement configuration, int index, RandomId repoId, Repo? nextRepo )
    {
        _world = world;
        _git = git;
        _configuration = configuration;
        _index = index;
        _repoId = repoId;
        _nextRepo = nextRepo;
    }

    /// <summary>
    /// Gets the World that contains this repository.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets the 'origin' remote repository url.
    /// </summary>
    public Uri OriginUrl => _git.RepositoryKey.OriginUrl;

    /// <summary>
    /// Gets this repo's working folder.
    /// </summary>
    public NormalizedPath WorkingFolder => _git.WorkingFolder;

    /// <summary>
    /// Gets this short path to display for this repository.
    /// </summary>
    public NormalizedPath DisplayPath => _git.DisplayPath;

    /// <summary>
    /// Gets the git status.
    /// This status is computed on demand and cached.
    /// </summary>
    public GitRepository.SimpleStatusInfo GitStatus
    {
        get
        {
            if( _status.IsDefault )
            {
                _status = _git.GetSimpleStatusInfo();
            }
            return _status;
        }
    }

    /// <summary>
    /// Gets the wrapped <see cref="GitRepository"/>. This should be used when the simplified API that
    /// this wrapper offers is not enough.
    /// <para>
    /// This object MUST NOT be disposed.
    /// </para>
    /// </summary>
    public GitRepository GitRepository => _git;

    /// <summary>
    /// Gets the index of this Repo in the World according to <see cref="WorldDefinitionFile.RepoOrder"/>.
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// Gets a unique identifier (randomly generated) for this Repo.
    /// <para>
    /// This is stored in the git repository in the "ckli-repo" annotated tag: this identifier is independent
    /// of any names (origin url, name of the stack, etc.): it should be used as a the identifier of a Repo
    /// when data must be externally associated to it.
    /// </para>
    /// <para>
    /// The "ckli-repo" tag also contains the <see cref="StackRepository.OriginUrl"/> of the stack that contains
    /// this Repo. The tag is updated if the Stack url changes (this is currently only for information). 
    /// </para>
    /// </summary>
    public RandomId CKliRepoId => _repoId;

    /// <summary>
    /// Returns the <see cref="DisplayPath"/> (with its link to <see cref="WorkingFolder"/>) as a <see cref="ContentBox"/>
    /// or a <see cref="HorizontalContent"/> with it and:
    /// <list type="number">
    ///     <item>A box with its current branch name.</item>
    ///     <item>A Box with the commit remotes ↑0↓0 differences indicator.</item>
    ///     <item>A Box with the <see cref="Repo.OriginUrl"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="withBranchName">True to add a box with the current branch name.</param>
    /// <param name="withRemoteDiffCount">True to add a box with the commit remotes ↑0↓0 differences indicator.</param>
    /// <param name="withOriginUrl">True to add a box with the <see cref="OriginUrl"/>.</param>
    /// <returns>The renderable.</returns>
    public IRenderable ToRenderable( ScreenType screenType, bool withBranchName = false, bool withRemoteDiffCount = false, bool withOriginUrl = false )
    {
        var status = GitStatus;
        var folderStyle = new TextStyle( status.IsDirty ? ConsoleColor.DarkRed : ConsoleColor.DarkGreen, ConsoleColor.Black );

        // First Box.
        IRenderable folder = screenType.Text( DisplayPath ).HyperLink( new Uri( $"file:///{WorkingFolder}" ) );
        folder = status.IsDirty
                    ? folder.Box( paddingRight: 1 ).AddLeft( screenType.Text( "✱" ).Box( paddingRight: 1 ) )
                    : folder.Box( paddingLeft: 2, paddingRight: 1 );
        folder = folder.Box( style: folderStyle );

        if( withBranchName )
        {
            folder = folder.AddRight( screenType.Text( status.CurrentBranchName ).Box( marginRight: 1 ) );
        }
        if( withRemoteDiffCount )
        {
            if( status.IsTracked )
            {
                var diff = CommitDiff( screenType, '↑', status.CommitAhead.Value )
                           .AddRight( CommitDiff( screenType, '↓', status.CommitBehind.Value ) );
                folder = folder.AddRight( diff.Box( marginRight: 1 ) );
            }
            else
            {
                folder = folder.AddRight( screenType.Text( "<local>" ).Box( marginRight: 1 ) );
            }
        }
        if( withOriginUrl )
        {
            folder = folder.AddRight( screenType.Text( OriginUrl.ToString() ).HyperLink( OriginUrl ).Box( marginRight: 1 ) );
        }
        return folder;

        static IRenderable CommitDiff( ScreenType screenType, char aheadOrBehind, int count )
        {
            return screenType.Text( $"{aheadOrBehind}{count}",
                                    count != 0
                                        ? new TextStyle( new Color( System.ConsoleColor.Red, System.ConsoleColor.Black ), TextEffect.Bold )
                                        : TextStyle.None );
        }
    }

    /// <summary>
    /// Overridden to return the <see cref="DisplayPath"/>.
    /// </summary>
    /// <returns>This display path.</returns>
    public override string ToString() => _git.DisplayPath;
}
