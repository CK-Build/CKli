using CK.Core;
using CK.Testing;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace CKli;

/// <summary>
/// Models a repository working folder in a <see cref="FakeBuildWorld"/>.
/// </summary>
public sealed partial class FakeBuildRepo
{
    readonly FakeBuildWorld _world;
    readonly IMonitorTestHelper _helper;
    readonly NormalizedPath _path;
    readonly NormalizedPath _displayPath;
    readonly string _defaultProjectName;
    readonly SVersion? _initialVersion;
    readonly ImmutableArray<PackageInstance> _initialConsumed;
    readonly string? _initialVersionTagName;
    ImmutableArray<PackageInstance> _transitivePackages;
    CKliEnv? _repoRoot;

    internal FakeBuildRepo( FakeBuildWorld world,
                            IMonitorTestHelper helper,
                            NormalizedPath path,
                            SVersion? initialVersion,
                            IReadOnlyList<FakeBuildRepo> references )
    {
        Throw.DebugAssert( path.StartsWith( world.WorldRoot.CurrentDirectory ) );
        _world = world;
        _helper = helper;
        _path = path;
        _initialVersion = initialVersion;
        _defaultProjectName = _path.LastPart.Replace( '-', '.' );
        _displayPath = path.RemoveFirstPart( world.WorldRoot.CurrentDirectory.Parts.Count );
        // The references are written in the project BEFORE the initial commit and appear in the initial
        // version tag's consumed packages: the created repository is then up to date with regard to its
        // version (nothing to build), exactly like a repository of a published stack.
        var consumed = references.Select( r => new PackageInstance( r.DefaultProjectName, r.RequiredInitialVersion ) )
                                 .Order()
                                 .ToImmutableArray();
        _initialConsumed = consumed;
        var refLines = string.Concat( consumed.Select( c => $"{Environment.NewLine}        <PackageReference Include=\"{c.PackageId}\" Version=\"{c.Version}\" />" ) );
        using( var e = CreateEditor() )
        {
            var projectPath = path.AppendPart( DefaultProjectName );
            Directory.CreateDirectory( projectPath );
            File.WriteAllText( path.AppendPart( SolutionFileName ), $"""
                <Solution>
                    <Project Path="{DefaultProjectName}/{DefaultProjectName}.csproj" />
                </Solution>
                """ );
            File.WriteAllText( projectPath.AppendPart( $"{DefaultProjectName}.csproj" ), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                    <ItemGroup>{refLines}
                    </ItemGroup>
                </Project>
                """ );
            e.GitRepository.Commit( helper.Monitor, $"Created '{SolutionFileName}' and its default project." );
            if( initialVersion != null )
            {
                e.GitRepository.Repository.Tags.Remove( "v0.0.0+fake" );
                var content = new BuildContentInfo( consumed, produced: [_defaultProjectName], assetFileNames: [] );
                // The name is kept rather than recomputed: RewriteInitialVersionTag must find this very tag.
                _initialVersionTagName = e.GitRepository.Repository.Tags
                                          .Add( $"{initialVersion.ParsedPrefix}/v{initialVersion}",
                                                e.GitRepository.Repository.Head.Tip,
                                                e.GitRepository.Committer,
                                                content.ToString() )
                                          .FriendlyName;
            }
        }
        // The fake build function is static and installed globally: this is how it finds back the
        // declarations of the repository it builds. See CKliBuildPluginTestHelperExtensions.
        CKliBuildPluginTestHelperExtensions.RegisterFakeBuildRepo( world.Stack.Remotes.GetUriFor( RepositoryName,
                                                                                                  mustExist: false ),
                                                                   this );
    }

    /// <summary>
    /// Gets the initial version tag created with this repository. Null if none has been created.
    /// </summary>
    public SVersion? InitialVersion => _initialVersion;

    /// <summary>
    /// Gets the <see cref="InitialVersion"/> or throws if this repository has been created without one:
    /// a repository can only be referenced if it produces a version.
    /// </summary>
    internal SVersion RequiredInitialVersion
    {
        get
        {
            Throw.CheckState( $"Repository '{RepositoryName}' has no initial version: it cannot be referenced.",
                              _initialVersion != null );
            return _initialVersion;
        }
    }

    /// <summary>
    /// Gets or sets the transitive packages that a fake build of this repository records in its
    /// <see cref="BuildContentInfo.Transitive"/>: what a restore would bring beyond the
    /// &lt;PackageReference&gt; of its projects.
    /// <para>
    /// The fake build restores nothing, so declaring them here is the only way a transitive set can exist in
    /// this harness. The default is <c>default</c> - "not recorded", <see cref="BuildContentInfo.HasTransitive"/>
    /// being false - which is NOT the empty array ("recorded, and a restore brings nothing"): the two states
    /// differ and both are reachable from here.
    /// </para>
    /// <para>
    /// Setting this also rewrites the annotation of the <see cref="InitialVersion"/> tag when there is one, so
    /// the declaration covers the build this repository already carries - which is what a publication that does
    /// not rebuild it reads. The tag is replaced on its own commit: no commit is added, so no "--ci.N" moves.
    /// </para>
    /// </summary>
    public ImmutableArray<PackageInstance> TransitivePackages
    {
        get => _transitivePackages;
        set
        {
            if( !value.IsDefault )
            {
                // BuildContentInfo requires a strictly sorted array: a test declares a set, not an order.
                value = value.Sort();
                for( int i = 1; i < value.Length; ++i )
                {
                    Throw.CheckArgument( $"Duplicate transitive package '{value[i]}'.", value[i - 1] != value[i] );
                }
            }
            _transitivePackages = value;
            RewriteInitialVersionTag();
        }
    }

    // The initial version tag is written by the constructor, before a test can declare anything: its annotation
    // is refreshed here rather than making the declaration a CreateRepoAsync parameter, which could not then be
    // changed between two builds.
    void RewriteInitialVersionTag()
    {
        if( _initialVersionTagName == null ) return;
        using var e = CreateEditor();
        var tags = e.GitRepository.Repository.Tags;
        var tag = tags[_initialVersionTagName];
        Throw.CheckState( $"Initial version tag '{_initialVersionTagName}' not found in '{DisplayPath}'.",
                          tag != null );
        var target = tag.Target;
        var content = new BuildContentInfo( _initialConsumed,
                                            produced: [_defaultProjectName],
                                            assetFileNames: [],
                                            _transitivePackages );
        tags.Remove( tag );
        tags.Add( _initialVersionTagName, target, e.GitRepository.Committer, content.ToString() );
    }

    /// <summary>
    /// Gets the world to which this Repo belongs.
    /// </summary>
    public FakeBuildWorld World => _world;

    /// <summary>
    /// Gets the Repo working folder full path.
    /// </summary>
    public NormalizedPath WorkingFolderPath => _path;

    /// <summary>
    /// Gets the relative path of this repo in the <see cref="World"/>.
    /// </summary>
    public NormalizedPath DisplayPath => _displayPath;

    /// <summary>
    /// Gets the repository name.
    /// </summary>
    public string RepositoryName => _path.LastPart;

    /// <summary>
    /// Gets the default project name based on the <see cref="RepositoryName"/>.
    /// </summary>
    public string DefaultProjectName => _defaultProjectName;

    /// <summary>
    /// Gets the .slnx file name.
    /// </summary>
    public string SolutionFileName => RepositoryName + ".slnx";

    /// <summary>
    /// Gets a context for this repository.
    /// </summary>
    public CKliEnv Root => _repoRoot ??= _world.WorldRoot.ChangeDirectory( _path );

    /// <summary>
    /// Creates a temporary editor for the repository.
    /// </summary>
    /// <returns>A temporary editor.</returns>
    public Editor CreateEditor() => new Editor( this, _helper );
}
