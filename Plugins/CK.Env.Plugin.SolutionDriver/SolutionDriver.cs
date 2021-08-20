using CK.Core;
using CK.Build;
using CK.Env.DependencyModel;
using CK.Env.Diff;
using CK.Env.MSBuildSln;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class SolutionDriver : GitBranchPluginBase, ISolutionDriver, IDisposable, ICommandMethodsProvider
    {
        public static readonly ArtifactType NuGetType = NuGet.NuGetClient.NuGetType;
        public static readonly ArtifactType CKSetupType = ArtifactType.Register( "CKSetup", false );
        /// <summary>
        /// This is shared here: more than one plugin needs this.
        /// </summary>
        public const string CODECAKEBUILDER_SECRET_KEY = "CODECAKEBUILDER_SECRET_KEY";

        readonly ISolutionDriverWorld _world;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly ArtifactCenter _artifactCenter;
        readonly SolutionSpec _solutionSpec;
        readonly SolutionContext _solutionContext;

        Solution _solution;
        SolutionFile _sln;
        IReadOnlyList<(string SecretKeyName, string Secret)> _buildSecrets;
        bool _isSolutionValid;

        public SolutionDriver(
                ISolutionDriverWorld w,
                GitFolder f,
                ArtifactCenter artifactCenter,
                NormalizedPath branchPath,
                SolutionSpec spec,
                IEnvLocalFeedProvider localFeedProvider )
            : base( f, branchPath )
        {
            _solutionContext = w.Register( this );
            _world = w;
            _artifactCenter = artifactCenter;
            _solutionSpec = spec;
            _localFeedProvider = localFeedProvider;
            f.Reset += OnReset;
            f.RunProcessStarting += OnRunProcessStarting;
        }

        void OnRunProcessStarting( object sender, RunCommandEventArgs e )
        {
            e.StartInfo.EnvironmentVariables.Add( "CKLI_CURRENT_WORLD_FULLNAME", GitFolder.World.FullName );
            e.StartInfo.EnvironmentVariables.Add( "CKLI_CURRENT_WORLD_NAME", GitFolder.World.Name );
            ISolution s = GetSolution( e.Monitor, true );
            if( s != null )
            {
                e.StartInfo.EnvironmentVariables.Add( "CKLI_CURRENT_SOLUTION_NAME", s.Name );
            }
        }

        private void OnReset( IActivityMonitor m )
        {
            SetSolutionDirty( m );
        }

        void IDisposable.Dispose()
        {
            _world.Unregister( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( "SolutionDriver" );

        IGitRepository ISolutionDriver.GitRepository => GitFolder;

        string ISolutionDriver.BranchName => BranchPath.LastPart;

        /// <summary>
        /// Gets whether this plugin is able to work.
        /// It provides services only on local or develop and if the <see cref="GitFolder.StandardGitStatus"/>
        /// is the same as <see cref="GitBranchPluginBase.StandardPluginBranch"/>.
        /// </summary>
        bool IsActive => GitFolder.StandardGitStatus == StandardPluginBranch
                         && (StandardPluginBranch == StandardGitStatus.Local || StandardPluginBranch == StandardGitStatus.Develop);

        /// <summary>
        /// Gets the solution driver of the <see cref="IGitRepository.CurrentBranchName"/>.
        /// </summary>
        /// <returns>This solution driver or the one of the current branch.</returns>
        public ISolutionDriver GetCurrentBranchDriver()
        {
            return GitFolder.StandardGitStatus == StandardPluginBranch
                ? this
                : GitFolder.PluginManager.BranchPlugins[GitFolder.CurrentBranchName].GetPlugin<SolutionDriver>();
        }

        /// <summary>
        /// Fires whenever a solution has been loaded so that any other
        /// plugins can participate to its configuration.
        /// </summary>
        public event EventHandler<SolutionConfigurationEventArgs> OnSolutionConfiguration;

        /// <summary>
        /// Gets whether the solution has been correctly read and configured.
        /// Nothing should be done with the solution when this is false, except fix operations.
        /// </summary>
        public bool IsSolutionValid => _isSolutionValid;

        /// <summary>
        /// Gets the secrets required to build the solution.
        /// This not null as soon as the solution has been successfuly read (this is available even
        /// if <see cref="IsSolutionValid"/> is false).
        /// </summary>
        public IReadOnlyList<(string SecretKeyName, string Secret)> BuildRequiredSecrets => _buildSecrets;

        /// <summary>
        /// Forces the solution to be reloaded next time <see cref="GetSolution"/> is called.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void SetSolutionDirty( IActivityMonitor m )
        {
            if( _sln != null )
            {
                _sln.Saved -= OnSolutionSaved;
                m.Info( $"Solution '{GitFolder.SubPath}' must be reloaded." );
                _isSolutionValid = false;
                _sln = null;
            }
        }

        /// <summary>
        /// Gets the Solution that this driver handles.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reloadSolution">True to force a lod (like having called <see cref="SetSolutionDirty(IActivityMonitor)"/>).</param>
        /// <param name="allowInvalidSolution">
        /// True to allow <see cref="IsSolutionValid"/> to be false: the instance is returned as long as the <see cref="ISolution"/> instance has
        /// successfully been built even if some required cheks have failed.
        /// </param>
        /// <returns>The solution or null if unable to load the solution.</returns>
        public ISolution GetSolution( IActivityMonitor monitor, bool allowInvalidSolution, bool reloadSolution = false )
        {
            if( !_isSolutionValid || reloadSolution )
            {
                using( monitor.OpenInfo( $"Loading solution '{GitFolder.SubPath}'." ) )
                {
                    DoLoadSolution( monitor );
                }
            }
            return _isSolutionValid || allowInvalidSolution ? _solution : null;
        }

        void DoLoadSolution( IActivityMonitor m )
        {
            _isSolutionValid = false;
            var expectedSolutionName = GitFolder.SubPath.LastPart + ".sln";
            _buildSecrets = null;
            _sln = SolutionFile.Read( GitFolder.FileSystem, m, BranchPath.AppendPart( expectedSolutionName ) );
            if( _sln == null ) return;

            _sln.Saved += OnSolutionSaved;
            bool newSolution = false;
            if( _solution == null )
            {
                newSolution = true;
                _solution = _solutionContext.AddSolution( BranchPath, expectedSolutionName );
                foreach( var targetName in _solutionSpec.ArtifactTargets )
                {
                    var r = _artifactCenter.Repositories.FirstOrDefault( repo => repo.UniqueRepositoryName == targetName );
                    if( r == null ) m.Error( $"Unable to find the repository named '{targetName}' (available repositories: {_artifactCenter.Repositories.Select( repo => repo.UniqueRepositoryName ).Concatenate()})." );
                    else _solution.AddArtifactTarget( r );
                }
                foreach( var sourceName in _solutionSpec.ArtifactSources )
                {
                    var f = _artifactCenter.Feeds.FirstOrDefault( feed => feed.TypedName == sourceName );
                    if( f == null )
                    {
                        m.Error( $"Unable to find the feed named '{sourceName}' (available sources: {_artifactCenter.Feeds.Select( feed => feed.TypedName ).Concatenate()})." );
                        continue;
                    }
                    _solution.AddArtifactSource( f );
                }
            }
            _solution.Tag( _sln );
            var projectsToRemove = new HashSet<DependencyModel.Project>( _solution.Projects );
            var orderedProjects = new DependencyModel.Project[_sln.MSProjects.Count];
            int i = 0;
            bool badPack = false;
            foreach( var p in _sln.MSProjects )
            {
                if( p.ProjectName != p.SolutionRelativeFolderPath.LastPart )
                {
                    m.Warn( $"Project named {p.ProjectName} should be in folder of the same name, not in {p.SolutionRelativeFolderPath.LastPart}." );
                }
                Debug.Assert( p.ProjectFile != null );

                bool doPack = p.IsPackable ?? true;
                if( doPack == true )
                {
                    if( _solutionSpec.NotPublishedProjects.Contains( p.Path ) )
                    {
                        m.Error( $"Project {p.ProjectName} that must not be published have the Element IsPackable not set to false." );
                        badPack = true;
                    }
                    else if( !_solutionSpec.TestProjectsArePublished && (p.Path.Parts.Contains( "Tests" ) || p.ProjectName.EndsWith( ".Tests" )) )
                    {
                        m.Error( $"Tests Project {p.ProjectName} does not have IsPackable set to false." );
                        badPack = true;
                    }
                    else if( p.ProjectName.Equals( "CodeCakeBuilder", StringComparison.OrdinalIgnoreCase ) )
                    {
                        m.Error( $"CodeCakeBuilder Project does not have IsPackable set to false." );
                        badPack = true;
                    }
                    else
                    {
                        m.Trace( $"Project {p.ProjectName} will be published." );
                    }
                }
                else
                {
                    m.Trace( $"Project {p.ProjectName} is set to not be packaged: this project won't be published." );
                }
                var (project, isNewProject) = _solution.AddOrFindProject( p.SolutionRelativeFolderPath, ".Net", p.ProjectName );
                project.Tag( p );
                if( isNewProject )
                {
                    project.TransformSavors( _ => p.TargetFrameworks );
                    ConfigureFromSpec( m, project, _solutionSpec );
                }
                else
                {
                    var previous = project.Savors;
                    if( previous != p.TargetFrameworks )
                    {
                        var removed = previous.Except( p.TargetFrameworks );
                        m.Trace( $"TargetFramework changed from {previous} to {p.TargetFrameworks}." );
                        project.TransformSavors( t =>
                        {
                            var c = t.Except( previous );
                            if( c.AtomicTraits.Count == t.AtomicTraits.Count - previous.AtomicTraits.Count )
                            {
                                // The trait contained all the previous ones: we replace all of them with the new one.
                                return c.Union( p.TargetFrameworks );
                            }
                            // The trait doesn't contain all the previous ones: we must not blindly add the new project's trait,
                            // we only remove the ones that have been removed.
                            return t.Except( removed );
                        } );
                    }
                }
                SynchronizePackageReferences( m, project );
                projectsToRemove.Remove( project );
                orderedProjects[i++] = project;
            }

            SynchronizeSolutionPackageReferences( _sln, _solution );

            foreach( var project in _solution.Projects.Where( p => p.Tag<MSProject>() != null ) )
            {
                SynchronizeProjectReferences( m, project, msProj => orderedProjects[msProj.MSProjIndex] );
            }
            foreach( var noMore in projectsToRemove ) _solution.RemoveProject( noMore );

            var buildSecrets = new List<(string SecretKeyName, string Secret)>();
            _isSolutionValid = !badPack;
            var h = OnSolutionConfiguration;
            if( h != null )
            {
                var e = new SolutionConfigurationEventArgs( m, _solution, newSolution, _solutionSpec, buildSecrets );
                h( this, e );
                if( e.ConfigurationFailed )
                {
                    m.Error( "Solution initialization failed: " + e.FailureMessage );
                    _isSolutionValid = false;
                }
            }
            foreach( var sc in _artifactCenter.ResolveSecrets( m, _solution.ArtifactTargets, false ) )
            {
                if( buildSecrets.IndexOf( s => s.SecretKeyName == sc.SecretKeyName ) < 0 )
                {
                    buildSecrets.Add( sc );
                }
            }
            _buildSecrets = buildSecrets;
        }

        void OnSolutionSaved( object sender, EventMonitoredArgs e )
        {
            SetSolutionDirty( e.Monitor );
        }

        static void ConfigureFromSpec( IActivityMonitor m, DependencyModel.Project project, SolutionSpec spec )
        {
            var msProject = project.Tag<MSProject>();
            if( project.SimpleProjectName == "CodeCakeBuilder" )
            {
                project.IsBuildProject = true;
            }
            else
            {
                project.IsTestProject = project.SimpleProjectName.EndsWith( ".Tests" );
                bool mustPublish;
                if( spec.PublishedProjects.Count > 0 )
                {
                    mustPublish = spec.PublishedProjects.Contains( project.SolutionRelativeFolderPath );
                }
                else
                {
                    bool notPublished = !spec.NotPublishedProjects.Contains( project.SolutionRelativeFolderPath );
                    bool notRootDirectory = project.SolutionRelativeFolderPath.Parts.Count == 1;



                    bool ignoreNotRoot = spec.PublishProjectInDirectories && !project.IsTestProject;
                    // We bindly follow the <IsPackable> element. Only if it's not defined (ie. it's null) we must "think".
                    mustPublish = msProject.IsPackable
                                    ?? (notPublished &&
                                            (notRootDirectory || ignoreNotRoot || (project.IsTestProject && spec.TestProjectsArePublished)));
                }
                if( mustPublish )
                {
                    project.AddGeneratedArtifacts( new Artifact( NuGetType, project.SimpleProjectName ) );
                }
                if( spec.CKSetupComponentProjects.Contains( project.SimpleProjectName ) )
                {
                    if( !mustPublish )
                    {
                        m.Error( $"Project {project} cannot be a CKSetupComponent since it is not published." );
                    }
                    foreach( var name in project.Tag<MSProject>()
                                                .TargetFrameworks.AtomicTraits
                                                .Select( t => new Artifact( CKSetupType, project.SimpleProjectName + '/' + t.ToString() ) ) )
                    {
                        project.AddGeneratedArtifacts( name );
                    }
                }
            }
        }

        static void SynchronizePackageReferences( IActivityMonitor m, DependencyModel.Project project )
        {
            var toRemove = new HashSet<Artifact>( project.PackageReferences.Select( r => r.Target.Artifact ) );
            var p = project.Tag<MSProject>();
            foreach( DeclaredPackageDependency dep in p.Deps.Packages )
            {
                var d = dep.BaseArtifactInstance;
                toRemove.Remove( d.Artifact );
                project.EnsurePackageReference( d,
                                                dep.PrivateAsset.Equals( "all", StringComparison.OrdinalIgnoreCase ) ? ArtifactDependencyKind.Private : ArtifactDependencyKind.Transitive,
                                                dep.Frameworks );
            }
            foreach( var noMore in toRemove ) project.RemovePackageReference( noMore );
        }

        static void SynchronizeSolutionPackageReferences( SolutionFile sln, Solution solutionModel )
        {
            HashSet<Artifact> solutionRefs = new HashSet<Artifact>( solutionModel.SolutionPackageReferences.Select( p => p.Target.Artifact ) );
            foreach( var p in sln.StandardDotnetToolConfigFile.Tools )
            {
                solutionRefs.Remove( p.Artifact );
                solutionModel.EnsureSolutionPackageReference( p );
            }
            foreach( var p in solutionRefs ) solutionModel.RemoveSolutionPackageReference( p );
        }

        static void SynchronizeProjectReferences( IActivityMonitor m, DependencyModel.Project project, Func<MSProject, DependencyModel.Project> depsFinder )
        {
            var p = project.Tag<MSProject>();
            var toRemove = new HashSet<IProject>( project.ProjectReferences.Select( r => r.Target ) );
            foreach( var dep in p.Deps.Projects )
            {
                if( dep.TargetProject is MSProject target )
                {
                    var mapped = depsFinder( target );
                    Debug.Assert( mapped != null );
                    project.EnsureProjectReference( mapped, ArtifactDependencyKind.Transitive );
                    toRemove.Remove( mapped );
                }
                else
                {
                    m.Warn( $"Project '{p}' references project {dep.TargetProject} of unhandled type. Reference is ignored." );
                }
            }
            foreach( var noMore in toRemove ) project.RemoveProjectReference( noMore );
        }

        /// <summary>
        /// Gets whether <see cref="IWorldState.WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/>
        /// and this plugin is on the active branch (<see cref="IsActive"/> is true).
        /// </summary>
        public bool CanPull => _world.WorkStatus == GlobalWorkStatus.Idle && IsActive;

        /// <summary>
        /// Pulls the current branch and reloads the solutions if needed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Pull( IActivityMonitor m )
        {
            var (Success, ReloadNeeded) = GitFolder.Pull( m );
            if( !Success ) return false;
            return !ReloadNeeded || GetSolution( m, true ) != null;
        }

        [CommandMethod]
        public bool DumpLogsBetweenDates( IActivityMonitor m, string beginning, string ending )
        {
            var solution = GetSolution( m, allowInvalidSolution: true );
            if( solution == null ) return false;
            if( !DateTimeOffset.TryParse( beginning, out DateTimeOffset beginningDate ) )
            {
                m.Error( $"{beginning} is not a valid date" );
                return false;
            }
            if( !DateTimeOffset.TryParse( ending, out DateTimeOffset endingDate ) )
            {
                m.Error( $"{ending} is not a valid date" );
                return false;
            }
            m.Info( $"Parsed date range: {beginningDate} => {endingDate}" );
            GitFolder.ShowLogsBetweenDates( m, beginningDate, endingDate, solution.Projects.Select( proj => new DiffRoot( solution.Name, proj.ProjectSources ) ) );
            return true;
        }

        [CommandMethod]
        public void ShowDetail( IActivityMonitor monitor )
        {
            var solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return;
            var depSolution = _solutionContext.GetDependencyAnalyser( monitor, false ).DefaultDependencyContext[solution];

            StringBuilder b = new StringBuilder();

            b.Append( depSolution.Index ).Append( " - [Rank:" ).Append( depSolution.Rank ).Append("] ").Append( solution.FullPath ).AppendLine();
            b.Append( "| ArtifactTargets: " ).AppendJoin( ", ", solution.ArtifactTargets.Select( t => $"{t.UniqueRepositoryName} (Filter: {t.QualityFilter})" ) ).AppendLine();
            b.Append( "| ArtifactSources: " ).AppendJoin( ", ", solution.ArtifactSources.Select( t => t.TypedName ) ).AppendLine();
            var publishedProjects = solution.Projects.Where( p => p.IsPublished );
            if( publishedProjects.Any() )
            {
                int count = publishedProjects.Count();
                b.Append( count > 1 ? $"|-> {count} published projects: " : $"|-> 1 published project:" ).AppendLine();
                foreach( var p in publishedProjects )
                {
                    DumpProject( b, p, true );
                }
            }
            var localProjects = solution.Projects.Where( p => !p.IsPublished && !p.IsBuildProject );
            if( localProjects.Any() )
            {
                int count = localProjects.Count();
                b.Append( count > 1 ? $"|-> {count} local projects: " : $"|-> 1 local project:" ).AppendLine();
                foreach( var p in localProjects )
                {
                    DumpProject( b, p, true );
                }
            }
            if( solution.BuildProject != null )
            {
                b.Append( "|-> BuildProject: " ).Append( solution.BuildProject.SimpleProjectName ).AppendLine();
                DumpProject( b, solution.BuildProject, false );
            }
            b.Append( "|-> Solution dependencies: " ).AppendLine();
            if( solution.SolutionPackageReferences.Count > 0 )
            {
                b.Append( "| Packages: " ).AppendJoin( ", ", solution.SolutionPackageReferences.Select( p => p.Target.ToString() ) ).AppendLine();
            }
            var min = depSolution.MinimalRequirements;
            var req = depSolution.Requirements;
            b.Append( "| MinimalRequirements: " ).AppendJoin( ", ", min.OrderBy( s => s.Index ).Select( s => s.Solution.Name ) ).AppendLine();
            if( req.Count != min.Count )
            {
                b.Append( "|        Requirements: " ).AppendJoin( ", ", req.OrderBy( s => s.Index ).Select( s => s.Solution.Name ) ).AppendLine();
            }
            var iMin = depSolution.MinimalImpacts;
            var iReq = depSolution.Impacts;
            b.Append( "| MinimalImpacts: " ).AppendJoin( ", ", iMin.OrderBy( s => s.Index ).Select( s => s.Solution.Name ) ).AppendLine();
            if( iReq.Count != iMin.Count )
            {
                b.Append( "|        Impacts: " ).AppendJoin( ", ", iReq.OrderBy( s => s.Index ).Select( s => s.Solution.Name ) ).AppendLine();
            }

            Console.Write( b.ToString() );
        }

        static void DumpProject( StringBuilder b, IProject p, bool withHeader )
        {
            if( withHeader )
            {
                b.Append( "|   " ).Append( p.SimpleProjectName ).Append( " [" ).Append( p.Type ).Append( "] " );
                if( p.IsTestProject ) b.Append( "[Test]" );
                if( p.Savors != null ) b.Append( " [" ).Append( p.Savors ).Append( "]" );
                b.AppendLine();
            }
            if( p.GeneratedArtifacts.Any() ) b.Append( "|     => " ).AppendJoin( ", ", p.GeneratedArtifacts ).AppendLine();
            b.Append( "|     PackageReferences: " ).AppendJoin( ", ", p.PackageReferences.Select( p => p.ToStringTarget() ) ).AppendLine();
            b.Append( "|     ProjectReferences: " ).AppendJoin( ", ", p.ProjectReferences.Select( p => p.ToStringTarget() ) ).AppendLine();
        }

        /// <summary>
        /// Fires whenever a package reference version must be upgraded.
        /// </summary>
        public event EventHandler<UpdatePackageDependencyEventArgs> OnUpdatePackageDependency;

        /// <summary>
        /// Updates projects dependencies and saves the solution and its updated projects.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageInfos">The packages to update.</param>
        /// <returns>True on success, false on error.</returns>
        public bool UpdatePackageDependencies( IActivityMonitor monitor, IReadOnlyCollection<UpdatePackageInfo> packageInfos )
        {
            var solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;
            Debug.Assert( packageInfos.All( p => p.Referer.Solution == solution ) );
            bool mustSave = false;
            foreach( var update in packageInfos )
            {
                if( update.Referer is IProject project )
                {
                    var p = project.Tag<MSProject>();
                    if( p != null )
                    {
                        int changes = p.SetPackageReferenceVersion( monitor, p.TargetFrameworks, update.PackageUpdate.Artifact.Name, update.PackageUpdate.Version );
                        mustSave |= changes != 0;
                    }
                }
                else
                {
                    mustSave |= _sln.StandardDotnetToolConfigFile.SetPackageReferenceVersion( monitor, update.PackageUpdate );
                }
            }
            bool error = false;
            using( monitor.OnError( () => error = true ) )
            {
                try
                {
                    var e = new UpdatePackageDependencyEventArgs( monitor, packageInfos );
                    OnUpdatePackageDependency?.Invoke( this, e );
                }
                catch( Exception ex )
                {
                    monitor.Error( "While updating dependencies.", ex );
                }
            }
            if( error ) return false;
            return mustSave ? _sln.Save( monitor ) : true;
        }


        /// <summary>
        /// Fires before and after <see cref="ZeroBuildProject"/> actually builds a
        /// project in ZeroVersion.
        /// </summary>
        public event EventHandler<ZeroBuildEventArgs> OnZeroBuildProject;

        /// <summary>
        /// Builds the given project (that must be handled by this driver otherwise an exception is thrown).
        /// This uses "dotnet pack" or "dotnet publish" depending on <see cref="ZeroBuildProjectInfo.MustPack"/>.
        /// No package updates are done by this method. Project is build as it is on the file system.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="info">The <see cref="ZeroBuildProjectInfo"/>.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ZeroBuildProject( IActivityMonitor monitor, ZeroBuildProjectInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );

            var solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;

            // This is required, otherwise the NuGet cache keeps the previous version (lost 4 hours of my life here).
            if( info.MustPack )
            {
                var packageName = info.Project.GeneratedArtifacts.Single( a => a.Artifact.Type == NuGetType ).Artifact.Name;
                _localFeedProvider.RemoveFromNuGetCache( monitor, packageName, SVersion.ZeroVersion );
            }
            var msP = info.Project.Tag<MSProject>();
            var msUpgrade = info.UpgradeZeroProjects.Select( p => p.Tag<MSProject>() ).ToList();
            var p2pRefToRemove = msP.Deps.Projects.Where( p2p => msUpgrade.Contains( p2p.TargetProject ) ).ToList();
            if( p2pRefToRemove.Count > 0 )
            {
                monitor.Info( $"Removing Project references: {p2pRefToRemove.Select( p2p => p2p.Element.ToString() ).Concatenate()}" );
                p2pRefToRemove.Select( p2p => p2p.Element ).Remove();
            }
            foreach( var z in msUpgrade )
            {
                msP.SetPackageReferenceVersion( monitor, msP.TargetFrameworks, z.ProjectName, SVersion.ZeroVersion, true, false, false );
            }
            if( !msP.Solution.Save( monitor ) ) return false;

            string commonArgs = $@" --no-dependencies";
            string versionArgs = $@" --configuration Release /p:Version=""{SVersion.ZeroVersion}"" /p:AssemblyVersion=""{InformationalVersion.ZeroAssemblyVersion}"" /p:FileVersion=""{InformationalVersion.ZeroFileVersion}"" /p:InformationalVersion=""{InformationalVersion.ZeroInformationalVersion}"" ";
            var args = info.MustPack
                        ? $@"pack --output ""{_localFeedProvider.ZeroBuild.PhysicalPath}"""
                        : $@"publish --output ""{_localFeedProvider.GetZeroVersionCodeCakeBuilderExecutablePath( solution.Name ).RemoveLastPart()}""";
            args += commonArgs + versionArgs;

            var path = GitFolder.FileSystem.GetFileInfo( msP.Path.RemoveLastPart() ).PhysicalPath;
            FileHelper.RawDeleteLocalDirectory( monitor, System.IO.Path.Combine( path, "bin" ) );
            FileHelper.RawDeleteLocalDirectory( monitor, System.IO.Path.Combine( path, "obj" ) );

            OnZeroBuildProject?.Invoke( this, new ZeroBuildEventArgs( monitor, true, info ) );
            try
            {
                // 23 dec. 2020: On CKSetup.Core change, the 0.0.0-0 ref to CK.ActivityMonitor was ignored (the resulting
                // nupkg had the previous CI versions). However breaking here and manually executing the dotnet pack
                // was okay...
                // This should be a (vicious) cache issue and may be a first "dotnet clean" helps.
                ProcessRunner.Run( monitor, path, "dotnet", "clean", 7000 );
                return ProcessRunner.Run( monitor, path, "dotnet", args, 120000 );
            }
            finally
            {
                GitFolder.ResetHard( monitor );
                OnZeroBuildProject?.Invoke( this, new ZeroBuildEventArgs( monitor, false, info ) );
            }
        }

        public bool IsUpgradeLocalPackagesEnabled => _world.WorkStatus == GlobalWorkStatus.Idle && IsActive;

        [CommandMethod]
        public bool UpgradeLocalPackages( IActivityMonitor monitor, bool upgradeBuildProjects )
        {
            if( !IsUpgradeLocalPackagesEnabled ) throw new InvalidOperationException( nameof( IsUpgradeLocalPackagesEnabled ) );

            var solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;

            var feed = StandardPluginBranch == StandardGitStatus.Local
                        ? _localFeedProvider.Local
                        : _localFeedProvider.CI;

            var toUpgrade = solution.Projects
                        .Where( p => upgradeBuildProjects || !p.IsBuildProject )
                        .SelectMany( p => p.PackageReferences )
                        .Select( dep => (Dep: dep, LocalVersion: feed.GetBestNuGetVersion( monitor, dep.Target.Artifact.Name )) )
                        .Where( pv => pv.LocalVersion != null )
                        .Select( pv => new UpdatePackageInfo( pv.Dep.Owner, new ArtifactInstance( NuGetType, pv.Dep.Target.Artifact.Name, pv.LocalVersion ) ) )
                        .ToList();

            if( !UpdatePackageDependencies( monitor, toUpgrade ) ) return false;
            return LocalCommit( monitor );
        }

        bool LocalCommit( IActivityMonitor m )
        {
            Debug.Assert( IsActive );
            bool amend = StandardPluginBranch == StandardGitStatus.Local || GitFolder.Head.Message == "Local build auto commit.";
            return GitFolder.Commit(
                m,
                "Local build auto commit.",
                amend ? CommitBehavior.AmendIfPossibleAndOverwritePreviousMessage : CommitBehavior.CreateNewCommit
            ) != CommittingResult.Error;
        }

        /// <summary>
        /// Fires before a build.
        /// </summary>
        public event EventHandler<BuildStartEventArgs> OnStartBuild;

        /// <summary>
        /// Fires after a build.
        /// </summary>
        public event EventHandler<BuildEndEventArgs> OnEndBuild;

        /// <summary>
        /// The solution must be valid (see <see cref="IsSolutionValid"/>) and all build secrets
        /// must be resolved.
        /// </summary>
        public bool IsBuildEnabled => _world.WorkStatus == GlobalWorkStatus.Idle
                                        && IsActive
                                        && _isSolutionValid
                                        && _buildSecrets.All( s => s.Secret != null );

        /// <summary>
        /// Builds the solution in 'local' branch or build in 'develop' without remotes, using the published Zero
        /// Version builder if it exists.
        /// This normally produces a CI build unless a version tag exists on the commit point.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="upgradeLocalDependencies">False to not upgrade the available local dependencies.</param>
        /// <param name="withUnitTest">False to skip unit tests.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Build( IActivityMonitor monitor, bool upgradeLocalDependencies = true, bool withUnitTest = true )
        {
            if( !IsBuildEnabled ) throw new InvalidOperationException( nameof( IsBuildEnabled ) );

            if( upgradeLocalDependencies )
            {
                if( !UpgradeLocalPackages( monitor, false ) ) return false;
            }
            else if( !LocalCommit( monitor ) )
            {
                return false;
            }

            return DoBuild( monitor, withUnitTest, null, false );
        }

        bool ISolutionDriver.Build( IActivityMonitor monitor, bool withUnitTest, bool? withZeroBuilder, bool withPushToRemote )
        {
            return DoBuild( monitor, withUnitTest, withZeroBuilder, withPushToRemote );
        }

        bool DoBuild( IActivityMonitor monitor, bool withUnitTest, bool? withZeroBuilder, bool withPushToRemote )
        {
            var solution = GetSolution( monitor, false );
            if( solution == null ) return false;

            // Version is always provided by the current commit point.
            var v = GitFolder.ReadVersionInfo( monitor )?.FinalBuildInfo.Version;
            if( v == null ) return false;

            BuildType buildType;

            IEnvLocalFeed feed;
            if( StandardPluginBranch == StandardGitStatus.Local )
            {
                if( !v.WithBuildMetaData( null ).NormalizedText.EndsWith( "-local" ) )
                {
                    monitor.Warn( $"Version {v} is not a -local version. It has already been built." );
                    return true;
                }
                feed = _localFeedProvider.Local;
                buildType = BuildType.Local;
            }
            else if( v.AsCSVersion == null )
            {
                // Not a CSemVer version: it is a CI build.
                feed = _localFeedProvider.CI;
                buildType = BuildType.CI;
            }
            else
            {
                feed = _localFeedProvider.Release;
                buildType = BuildType.Release;
            }

            monitor.Info( $"Version to build: '{v}'." );

            // Base time is to wait one second.
            // This will be increased below.
            int timeout = 1000;

            var expectedArtifacts = solution.GeneratedArtifacts.Select( g => g.Artifact.WithVersion( v ) );
            var missing = feed.GetMissing( monitor, expectedArtifacts );

            bool buildRequired = missing.Count > 0 || !expectedArtifacts.Any();
            if( !buildRequired )
            {
                monitor.Info( $"All artifacts are already available in {feed.PhysicalPath.LastPart} with version {v}: {solution.GeneratedArtifacts.Select( a => a.Artifact.ToString() ).Concatenate()}." );
                if( !withUnitTest )
                {
                    monitor.Info( $"No unit tests required. Build is skipped." );
                    return true;
                }
                if( _solutionSpec.NoDotNetUnitTests )
                {
                    monitor.Info( $"Solution settings: NoDotNetUnitTests is true. Build is skipped." );
                    return true;
                }
            }
            else if( missing.Count == 0 )
            {
                monitor.Info( $"No artifacts have to be generated. Build is required." );
            }
            timeout += _solutionSpec.BuildTimeoutMilliseconds;
            if( withUnitTest && !_solutionSpec.NoDotNetUnitTests )
            {
                buildType |= BuildType.WithUnitTests;
                timeout += _solutionSpec.RunTestTimeoutMilliseconds;
            }
            if( withPushToRemote )
            {
                if( buildType == BuildType.CI )
                {
                    buildType |= BuildType.WithPushToRemote;
                    timeout += _solutionSpec.RemotePushTimeoutMilliseconds;
                }
                else
                {
                    if( buildType == BuildType.Local )
                    {
                        throw new ArgumentException( "Remote push is not allowed for 'local' builds.", nameof( withPushToRemote ) );
                    }
                    // The version is a 'release'. When releasing with CK-Env, no push to remotes are done (artifacts are
                    // retained and pushes are deferred).
                    // ==> This could be an ArgumentException just as above for the 'local' case.
                    // BUT! We may be in "normal CI Build" case and the version is 'release' because the commit has been
                    // already released on this system or on another one.
                    // On this system and if the local feeds have not been emptied, we have handled this by the 'skip handling' above.
                    // If not (from a fresh check out for instance), the only way to handle this would be to challenge the existence of
                    // the artifacts in the remotes which is a PITA: the 'skip handling' above would be costly.
                    // ==> We just warn and ignores the push.
                    monitor.Warn( $"Version '{v}' is not a CI version. Push to remote is ignored since it has already been done or will be done when publishing the release." );
                }
            }

            string? ccbPath = _localFeedProvider.GetZeroVersionCodeCakeBuilderExecutablePath( solution.Name );

            if( System.IO.File.Exists( ccbPath ) )
            {
                if( withZeroBuilder != false )
                {
                    buildType |= BuildType.WithZeroBuilder;
                    monitor.Info( "Using available CodeCakeBuilder published Zero version." );
                }
            }
            else
            {
                if( withZeroBuilder == true )
                {
                    var msg = "CodeCakeBuilder Zero Version executable file not found";
                    monitor.Error( $"Invalid 'withZeroBuilder' constraint: {msg}. Zero Build versions must first be built." );
                    return false;
                }
                ccbPath = null;
            }
            if( (buildType & BuildType.WithZeroBuilder) != BuildType.WithZeroBuilder )
            {
                monitor.Info( "Using CodeCakeBuilder with source compilation (dotnet run)." );
                // Consider that 10 seconds to build the CodeCakeBuilder is enough.
                timeout += 10 * 1000;
            }
            var ev = new BuildStartEventArgs(
                            monitor,
                            buildRequired,
                            solution,
                            v,
                            buildType,
                            GitFolder.FullPhysicalPath,
                            ccbPath,
                            timeout );

            bool FireEvent( bool start, bool success )
            {
                using( ev.Monitor.OnError( () => success = false ) )
                {
                    try
                    {
                        if( start ) OnStartBuild?.Invoke( this, ev );
                        else OnEndBuild?.Invoke( this, new BuildEndEventArgs( ev, success ) );
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( ex );
                    }
                }
                return success;
            }

            bool ok = FireEvent( true, true );
            if( ok ) ok = DoBuild( ev );
            ok = FireEvent( false, ok );
            if( ev.IsUsingDirtyFolder ) GitFolder.ResetHard( ev.Monitor );
            return ok;
        }

        bool DoBuild( BuildStartEventArgs ev )
        {
            IActivityMonitor m = ev.Monitor;
            using( m.OpenInfo( $"Building {ev.Solution}, Target Version = {ev.Version}" ) )
            {
                try
                {
                    ev.EnvironmentVariables.AddRange( _buildSecrets.Where( s => s.Secret != null ) );

                    var args = ev.WithZeroBuilder
                                ? ev.CodeCakeBuilderExecutableFile + " SolutionDirectoryIsCurrentDirectory"
                                : "run --project CodeCakeBuilder";
                    args += " -autointeraction";
                    args += " -PushToRemote=" + (ev.WithPushToRemote ? 'Y' : 'N');
                    if( !ev.BuildIsRequired ) args += " -target=\"Unit-Testing\" -exclusiveOptional -IgnoreNoArtifactsToProduce=Y";
                    if( !ev.WithUnitTest ) args += " -RunUnitTests=N";
                    if( !ProcessRunner.Run( m, ev.SolutionPhysicalPath, "dotnet", args, ev.TimeoutMilliseconds, LogLevel.Warn, ev.EnvironmentVariables ) )
                    {
                        return false;
                    }
                }
                catch( Exception ex )
                {
                    m.Error( $"Build failed.", ex );
                    return false;
                }
                return true;
            }
        }
    }
}
