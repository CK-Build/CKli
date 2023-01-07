using CK.Core;
using CK.Build;
using CK.Env.DependencyModel;
using CK.Env.Diff;
using CK.Env.MSBuildSln;

using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Diagnostics.CodeAnalysis;

namespace CK.Env.Plugin
{
    public partial class SolutionDriver : GitBranchPluginBase, ISolutionDriver, IDisposable, ICommandMethodsProvider
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
        readonly List<ISolutionProvider> _solutionProviders;

        Solution? _solution;
        IReadOnlyList<(string SecretKeyName, string? Secret)>? _buildSecrets;
        bool _isSolutionValid;

        public SolutionDriver( ISolutionDriverWorld w,
                               GitRepository f,
                               ArtifactCenter artifactCenter,
                               NormalizedPath branchPath,
                               SolutionSpec spec,
                               IEnvLocalFeedProvider localFeedProvider )
            : base( f, branchPath )
        {
            _solutionProviders = new List<ISolutionProvider>();
            _world = w;
            _artifactCenter = artifactCenter;
            _solutionSpec = spec;
            _localFeedProvider = localFeedProvider;
            _solutionContext = w.Register( this );
            f.Reset += OnReset;
            f.RunProcessStarting += OnRunProcessStarting;
            // The MSBuildSolutionProvider is implemented here to avoid
            // yet another assembly/plugin.
            _solutionProviders.Add( new MSBuildSolutionProvider( this ) );
        }

        void OnRunProcessStarting( object? sender, RunCommandEventArgs e )
        {
            e.StartInfo.EnvironmentVariables.Add( "CKLI_CURRENT_WORLD_FULLNAME", GitFolder.World.FullName );
            e.StartInfo.EnvironmentVariables.Add( "CKLI_CURRENT_WORLD_NAME", GitFolder.World.Name );
            e.StartInfo.EnvironmentVariables.Add( "CKLI_CURRENT_SOLUTION_NAME", GitFolder.SubPath.LastPart );
        }

        void OnReset( IActivityMonitor m )
        {
            SetSolutionDirty( m );
        }

        void IDisposable.Dispose()
        {
            _world.Unregister( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( "SolutionDriver" );

        GitRepository ISolutionDriver.GitRepository => GitFolder;

        string ISolutionDriver.BranchName => BranchPath.LastPart;

        /// <summary>
        /// Gets whether this plugin is able to work.
        /// It provides services only on local or develop and if the <see cref="GitRepository.StandardGitStatus"/>
        /// is the same as <see cref="GitBranchPluginBase.StandardPluginBranch"/>.
        /// </summary>
        bool IsActive => GitFolder.StandardGitStatus == StandardPluginBranch
                         && (StandardPluginBranch == StandardGitStatus.Local || StandardPluginBranch == StandardGitStatus.Develop);

        /// <summary>
        /// Gets the solution driver of the <see cref="GitRepository.CurrentBranchName"/>.
        /// </summary>
        /// <returns>This solution driver or the one of the current branch.</returns>
        public ISolutionDriver GetCurrentBranchDriver()
        {
            return GitFolder.StandardGitStatus == StandardPluginBranch
                    ? this
                    : GitFolder.PluginManager.BranchPlugins[GitFolder.CurrentBranchName].GetPlugin<SolutionDriver>();
        }

        public void RegisterSolutionProvider( ISolutionProvider provider )
        {
            Throw.CheckNotNullArgument( provider );
            Throw.CheckState( !_solutionProviders.Contains( provider ) );
            _solutionProviders.Add( provider );
        }

        /// <summary>
        /// Fires whenever a solution has been loaded so that any other
        /// plugins can participate to its configuration.
        /// </summary>
        public event EventHandler<SolutionConfigurationEventArgs>? OnSolutionConfiguration;

        /// <summary>
        /// Gets whether the solution has been correctly read and configured.
        /// Nothing should be done with the solution when this is false, except fix operations.
        /// </summary>
        public bool IsSolutionValid => _isSolutionValid && _solutionProviders.All( p => !p.IsDirty );

        /// <summary>
        /// Gets the secrets required to build the solution.
        /// This is not null as soon as the solution has been successfully read (this is available even
        /// if <see cref="IsSolutionValid"/> is false).
        /// </summary>
        public IReadOnlyList<(string SecretKeyName, string? Secret)>? BuildRequiredSecrets => _buildSecrets;

        /// <summary>
        /// Forces the solution to be reloaded next time <see cref="GetSolution"/> is called by
        /// calling <see cref="ISolutionProvider.SetDirty(IActivityMonitor)"/> an all registered providers.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void SetSolutionDirty( IActivityMonitor m )
        {
            foreach( var p in _solutionProviders )
            {
                p.SetDirty( m );
            }
        }

        /// <inheritdoc />
        public ISolution? GetSolution( IActivityMonitor monitor, bool allowInvalidSolution, bool reloadSolution = false )
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

        /// <summary>
        /// Gets the <see cref="ISolution"/> and <see cref="SolutionFile"/> if the solution
        /// is valid.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="solution">The logical solution.</param>
        /// <param name="sln">The MSBuild solution.</param>
        /// <returns>True on success, false on error.</returns>
        public bool TryGetSolution( IActivityMonitor monitor, [NotNullWhen(true)] out ISolution? solution, [NotNullWhen( true )] out SolutionFile? sln )
        {
            sln = null;
            solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;
            sln = solution.Tag<SolutionFile>();
            if( sln == null ) return false;
            return true;
        }

        void DoLoadSolution( IActivityMonitor m )
        {
            _isSolutionValid = false;
            _buildSecrets = null;
            bool newSolution = false;
            if( _solution == null )
            {
                newSolution = true;
                var expectedSolutionName = GitFolder.SubPath.LastPart + ".solution";
                _solution = _solutionContext.AddSolution( BranchPath, expectedSolutionName );
            }
            var buildSecrets = new List<(string SecretKeyName, string? Secret)>();
            var e = new SolutionConfigurationEventArgs( m, _solution, newSolution, _solutionSpec, buildSecrets );
            foreach( var provider in _solutionProviders )
            {
                provider.ConfigureSolution( this, e );
            }
            if( !e.ConfigurationFailed )
            {
                SynchronizeSources( e );
                SynchronizeArtifactTargets( e );
                OnSolutionConfiguration?.Invoke( this, e );
            }

            Debug.Assert( !_isSolutionValid, "We have been pessimistic." );
            if( e.ConfigurationFailed )
            {
                m.Error( "Solution initialization failed: " + e.FailureMessage );
            }
            else
            {
                _isSolutionValid = true;
            }
            // Always tries to resolve the secrets of the artifact targets even on failure.
            var l = _artifactCenter.ResolveSecrets( m, _solution.ArtifactTargets, allMustBeResolved: false );
            Debug.Assert( l != null, "We allow unresolved secrets." );
            foreach( var sc in l )
            {
                if( buildSecrets.IndexOf( s => s.SecretKeyName == sc.SecretKeyName ) < 0 )
                {
                    buildSecrets.Add( sc );
                }
            }
            _buildSecrets = buildSecrets;

            void SynchronizeSources( SolutionConfigurationEventArgs e )
            {
                var requiredFeedTypes = new HashSet<ArtifactType>( _solution.AllPackageReferences.Select( r => r.Target.Artifact.Type! ) );
                foreach( var feed in _artifactCenter.Feeds )
                {
                    if( requiredFeedTypes.Contains( feed.ArtifactType ) )
                    {
                        if( _solution.AddArtifactSource( feed ) )
                        {
                            e.Monitor.Info( $"Added feed '{feed}' to {_solution}." );
                        }
                    }
                    else
                    {
                        if( _solution.RemoveArtifactSource( feed ) )
                        {
                            e.Monitor.Info( $"Removed feed '{feed}' from {_solution}." );
                        }
                    }
                }
            }

            void SynchronizeArtifactTargets( SolutionConfigurationEventArgs e )
            {
                var requiredRepostoryTypes = new HashSet<ArtifactType>( _solution.GeneratedArtifacts.Select( g => g.Artifact.Type! ) );
                foreach( var repository in _artifactCenter.Repositories )
                {
                    if( requiredRepostoryTypes.Any( t => repository.HandleArtifactType( t ) ) )
                    {
                        if( _solution.AddArtifactTarget( repository ) )
                        {
                            e.Monitor.Info( $"Added repository '{repository}' to {_solution}." );
                        }
                    }
                    else
                    {
                        if( _solution.RemoveArtifactTarget( repository ) )
                        {
                            e.Monitor.Info( $"Removed repository '{repository}' from {_solution}." );
                        }
                    }
                }
            }
        }

        void OnSolutionSaved( object? sender, EventMonitoredArgs e )
        {
            SetSolutionDirty( e.Monitor );
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
            var (success, reloadNeeded) = GitFolder.Pull( m );
            if( !success ) return false;
            return !reloadNeeded || GetSolution( m, true ) != null;
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
            var req = depSolution.DirectRequirements;
            b.Append( "| MinimalRequirements: " ).AppendJoin( ", ", min.OrderBy( s => s.Index ).Select( s => s.Solution.Name ) ).AppendLine();
            if( req.Count != min.Count )
            {
                b.Append( "|        Requirements: " ).AppendJoin( ", ", req.OrderBy( s => s.Index ).Select( s => s.Solution.Name ) ).AppendLine();
            }

            var iMin = depSolution.MinimalImpacts;
            b.Append( "|    Minimal impacts: " ).AppendJoin( ", ", iMin.OrderBy( s => s.Index ).Select( s => s.Solution.Name ) ).AppendLine();
            var iReq = depSolution.DirectImpacts;
            if( iReq.Count != iMin.Count )
            {
                b.Append( "|     Direct impacts: " ).AppendJoin( ", ", iReq.OrderBy( s => s.Index ).Select( s => s.Solution.Name ) ).AppendLine();
            }
            var iTransReq = depSolution.TransitiveImpacts;
            if( iTransReq.Count != iReq.Count )
            {
                b.Append( "| Transitive impacts: " ).AppendJoin( ", ", iTransReq.OrderBy( s => s.Index ).Select( s => s.Solution.Name ) ).AppendLine();
            }

            Console.Write( b.ToString() );

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
        }

        /// <summary>
        /// Fires whenever a package reference version must be upgraded.
        /// </summary>
        public event EventHandler<UpdatePackageDependencyEventArgs>? OnUpdatePackageDependency;

        /// <summary>
        /// A build project always update all its (single!) TargetFramework.
        /// </summary>
        /// <remarks>
        /// By default (null <paramref name="frameworkFilter"/>) all package references will be updated
        /// regardless of any framework conditions that are not "locked" (see <see cref="SVersionLock"/>:
        /// NuGet like "[14.2.1]" and Npm references like "=1.2.3" or simply "1.2.3" are locked).
        /// <para>
        /// Filter can be a ';' separated list of target frameworks that are eventually resolved into <see cref="MSProject.Savors"/>
        /// context.
        /// </para>
        /// <para>
        /// Use the special <see cref="ISolutionDriver.UsePrimaryTargetFramework"/> string to restrict the update to
        /// conditions that satisfy <see cref="SharedSolutionSpec.PrimaryTargetFramework"/>.
        /// </para>
        /// </remarks>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageInfos">The update infos.</param>
        /// <param name="frameworkFilter">See remarks.</param>
        /// <returns>True on success, false otherwise.</returns>
        public bool UpdatePackageDependencies( IActivityMonitor monitor,
                                               IReadOnlyCollection<UpdatePackageInfo> packageInfos,
                                               string? frameworkFilter = null )
        {
            if( !TryGetSolution( monitor, out var solution, out var sln ) ) return false;

            // Resolves frameworks after the load: MSProject.Savors is initialized.
            CKTrait? frameworks = null;
            if( frameworkFilter == ISolutionDriver.UsePrimaryTargetFramework )
            {
                frameworkFilter = _solutionSpec.PrimaryTargetFramework;
            }
            if( frameworkFilter != null )
            {
                frameworks = MSProject.Savors.FindOnlyExisting( frameworkFilter );
                if( frameworks == null )
                {
                    monitor.Error( $"Unable to resolve target frameworks '{frameworkFilter}'. Update canceled." );
                    return false;
                }
            }

            Debug.Assert( packageInfos.All( p => p.Referer.Solution == solution ) );
            bool mustSave = false;
            foreach( var update in packageInfos )
            {
                if( update.Referer is IProject project )
                {
                    var p = project.Tag<MSProject>();
                    if( p != null )
                    {
                        Debug.Assert( p.TargetFrameworks != null );
                        // Build project always update all its (single!) TargetFramework.
                        CKTrait? f = project.IsBuildProject ? p.TargetFrameworks : frameworks ?? p.TargetFrameworks;
                        int changes = p.SetPackageReferenceVersion( monitor, f, update.PackageUpdate.Artifact.Name, update.PackageUpdate.Version );
                        mustSave |= changes != 0;
                    }
                }
                else
                {
                    mustSave |= sln.StandardDotnetToolConfigFile.SetPackageReferenceVersion( monitor, update.PackageUpdate );
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
            return mustSave ? sln.Save( monitor ) : true;
        }

        // This should not be here: only CKli (the console) is concerned here.
        [CommandMethod]
        public bool DumpLogsBetweenDates( IActivityMonitor m, string beginning = "01-01-2021", string ending = "31-12-2021" )
        {
            using var auto = m.TemporarilySetInteractiveUserFilter( new LogClamper( LogFilter.Off, true ) );
            var solution = GetSolution( m, allowInvalidSolution: true );
            if( solution == null ) return false;
            if( !ParseDates( m, beginning, ending, out var beginningDate, out var endingDate ) ) return false;
            StringBuilder b = new StringBuilder();
            foreach( var message in GitFolder.GetCommitMessagesBetween( m, beginningDate, endingDate ) )
            {
                message.ToString( b );
            }
            Console.WriteLine( b.ToString() );
            return true;
        }

        private static bool ParseDates( IActivityMonitor m, string beginning, string ending, out DateTimeOffset beginningDate, out DateTimeOffset endingDate )
        {
            endingDate = default;
            if( !DateTimeOffset.TryParse( beginning, out beginningDate ) )
            {
                m.Error( $"'{beginning}' is not a valid date" );
                return false;
            }
            if( !DateTimeOffset.TryParse( ending, out endingDate ) )
            {
                m.Error( $"'{ending}' is not a valid date" );
                return false;
            }
            m.Info( $"Parsed date range: {beginningDate} => {endingDate}" );
            return true;
        }

        // This should not be here: only CKli (the console) is concerned here.
        [CommandMethod]
        public bool DiffBetweenDates( IActivityMonitor m, string beginning = "01-01-2022", string ending = "31-12-2022", bool showDiffs = false )
        {
            using var auto = m.TemporarilySetInteractiveUserFilter( new LogClamper( LogFilter.Off, true ) );
            var solution = GetSolution( m, allowInvalidSolution: true );
            if( solution == null ) return false;
            if( !ParseDates( m, beginning, ending, out var beginningDate, out var endingDate ) ) return false;
            m.Info( $"Parsed date range: {beginningDate} => {endingDate}" );
            var diff = GitFolder.GetDiff( m, beginningDate, endingDate, solution.Projects.Select( proj => new DiffRoot( proj.Name, proj.ProjectSources ) ), true );
            if( diff != null )
            {
                Console.WriteLine( diff.ToString( true, showDiffs ) );
            }
            return true;
        }

        public bool StopUsingPropertyVersionAndOldCentralPackageManagement( IActivityMonitor monitor )
        {
            if( !TryGetSolution( monitor, out var _, out var sln ) ) return false;
            var msProjects = sln.MSProjects;
            foreach( var p in msProjects )
            {
                p.StopUsingPropertyVersionAndOldCentralPackageManagement( monitor );
            }
            sln.Save( monitor );
            return true;
        }

        [CommandMethod]
        public bool ChangeSingleTargetFramework( IActivityMonitor monitor, string oldOne, string newOne )
        {
            Throw.CheckNotNullArgument( oldOne );
            Throw.CheckNotNullArgument( newOne );

            var solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;
            var o = MSProject.Savors.FindOnlyExisting( oldOne );
            if( o == null || o.IsEmpty )
            {
                monitor.Info( $"'{oldOne}' is not an existing target framework. There is nothing to replace." );
                return true;
            }
            if( o.AtomicTraits.Count != 1 )
            {
                monitor.Error( $"Target framework '{oldOne}' must be a single framework." );
                return false;
            }
            var n = MSProject.Savors.FindOrCreate( newOne );
            if( n.AtomicTraits.Count != 1 )
            {
                monitor.Error( $"Target framework to replace '{newOne}' must be a single framework." );
                return false;
            }
            return ChangeSingleTargetFramework( monitor, o, n );
        }

        /// <summary>
        /// Replaces a target framework with another one in all projects that are not the <see cref="IProject.IsBuildProject"/>
        /// only if it is the single target framework defined: projects that use multi targeting are skipped.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="oldOne">The target framework to replace.</param>
        /// <param name="newOne">The new target framework.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ChangeSingleTargetFramework( IActivityMonitor monitor, CKTrait oldOne, CKTrait newOne )
        {
            Throw.CheckNotNullArgument( oldOne );
            Throw.CheckNotNullArgument( newOne );

            if( !TryGetSolution( monitor, out var solution, out var sln ) ) return false;

            bool mustSave = false;
            foreach( var p in solution.Projects )
            {
                if( p.IsBuildProject ) continue;
                MSProject? project = p.Tag<MSProject>();
                if( project != null && project.TargetFrameworks != null )
                {
                    if( project.TargetFrameworks.AtomicTraits.Count == 1 )
                    {
                        if( project.TargetFrameworks == oldOne )
                        {
                            mustSave |= project.SetTargetFrameworks( monitor, newOne );  
                        }
                        else
                        {
                            monitor.Debug( $"Project {project.ProjectName} skipped since it targets: {project.TargetFrameworks}." );
                        }
                    }
                    else
                    {
                        monitor.Info( $"Project {project.ProjectName} skipped since it has multiple targets: {project.TargetFrameworks}." );
                    }
                }
            }
            return mustSave ? sln.Save( monitor ) : true;
        }



        /// <summary>
        /// Fires before and after <see cref="ZeroBuildProject"/> actually builds a
        /// project in ZeroVersion.
        /// </summary>
        public event EventHandler<ZeroBuildEventArgs>? OnZeroBuildProject;

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
            Throw.CheckNotNullArgument( info );

            var solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;

            // This is required, otherwise the NuGet cache keeps the previous version (lost 4 hours of my life here).
            if( info.MustPack )
            {
                var packageName = info.Project.GeneratedArtifacts.Single( a => a.Artifact.Type == NuGetType ).Artifact.Name;
                _localFeedProvider.RemoveFromNuGetCache( monitor, packageName, SVersion.ZeroVersion );
            }
            var msP = info.Project.Tag<MSProject>();
            if( _world.WorldName.ParallelName == null )
            {
                using( monitor.OpenInfo( $"Analyzing dependencies and updates them if needed since we are on the default stack '{_world.WorldName.FullName}'." ) )
                {
                    var msUpgrade = info.UpgradeZeroProjects.Select( p => p.Tag<MSProject>() ).ToList();
                    var p2pRefToRemove = msP.Deps.Projects.Where( p2p => msUpgrade.Contains( p2p.TargetProject ) ).ToList();
                    if( p2pRefToRemove.Count > 0 )
                    {
                        monitor.Info( $"Removing Project references: {p2pRefToRemove.Select( p2p => p2p.Element.ToString() ).Concatenate()}" );
                        p2pRefToRemove.Select( p2p => p2p.Element ).Remove();
                    }
                    foreach( var z in msUpgrade )
                    {
                        msP.SetPackageReferenceVersion( monitor,
                                                        msP.TargetFrameworks,
                                                        z.ProjectName,
                                                        SVersion.ZeroVersion,
                                                        addIfNotExists: true,
                                                        preserveExisting: false,
                                                        throwOnProjectDependendencies: false );
                    }
                    if( !msP.Solution.Save( monitor ) ) return false;
                }
            }
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
                ProcessRunner.Run( monitor, path, "dotnet", "clean", 10_000 );
                return ProcessRunner.Run( monitor, path, "dotnet", args, 120_000 );
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

            if( !UpdatePackageDependencies( monitor, toUpgrade, ISolutionDriver.UsePrimaryTargetFramework ) ) return false;

            return LocalCommit( monitor );
        }

        bool LocalCommit( IActivityMonitor m )
        {
            Debug.Assert( IsActive );
            bool amend = StandardPluginBranch == StandardGitStatus.Local || GitFolder.Head.Message == "Local build auto commit.";
            return GitFolder.Commit( m,
                                     "Local build auto commit.",
                                     amend ? CommitBehavior.AmendIfPossibleAndOverwritePreviousMessage : CommitBehavior.CreateNewCommit ) != CommittingResult.Error;
        }

        /// <summary>
        /// Fires before a build.
        /// </summary>
        public event EventHandler<BuildStartEventArgs>? OnStartBuild;

        /// <summary>
        /// Fires after a build.
        /// </summary>
        public event EventHandler<BuildEndEventArgs>? OnEndBuild;

        /// <summary>
        /// The solution must be valid (see <see cref="IsSolutionValid"/>) and all build secrets
        /// must be resolved.
        /// </summary>
        public bool IsBuildEnabled => _world.WorkStatus == GlobalWorkStatus.Idle
                                        && IsActive
                                        && _isSolutionValid
                                        && _buildSecrets != null
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
            var v = GitFolder.ReadVersionInfo( monitor )?.Commit.FinalBuildInfo.Version;
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
                // Consider that 15 seconds to build the CodeCakeBuilder is enough.
                timeout += 15 * 1000;
            }
            var ev = new BuildStartEventArgs( monitor,
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
