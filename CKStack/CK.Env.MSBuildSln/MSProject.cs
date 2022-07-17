using CK.Core;
using CK.Build;

using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.MSBuildSln
{
    public class MSProject : Project
    {
        /// <summary>
        /// Definition of NuGet artifact type: its name is "NuGet", it is installable and its savors
        /// use ';' as a separator to match the one used by csproj.
        /// <see cref="Savors"/> exposes them directly.
        /// </summary>
        public static readonly ArtifactType NuGetArtifactType = ArtifactType.Register( "NuGet", true, ';' );

        /// <summary>
        /// Traits are used to manage framework names: these are the savors of <see cref="NuGetArtifactType"/>.
        /// The <see cref="CKTraitContext.Separator"/> is the ';' to match the one used by csproj (parsing and
        /// string representation becomes straightforward).
        /// </summary>
        public static readonly CKTraitContext Savors = NuGetArtifactType.ContextSavors!;

        /// <summary>
        /// Captures <see cref="DeclaredPackageDependency"/> and <see cref="ProjectToProjectDependency"/>.
        /// </summary>
        public readonly struct Dependencies
        {
            /// <summary>
            /// Gets all the PackageReference.
            /// </summary>
            public readonly IReadOnlyList<DeclaredPackageDependency> Packages;

            /// <summary>
            /// Gets all ProjectReference.
            /// </summary>
            public readonly IReadOnlyList<ProjectToProjectDependency> Projects;

            /// <summary>
            /// Gets the useless dependencies.
            /// </summary>
            public readonly IReadOnlyList<XElement> UselessDependencies;

            /// <summary>
            /// Gets whether this structure has been initialized.
            /// </summary>
            public bool IsInitialized => Packages != null;

            /// <summary>
            /// Gets the <see cref="DeclaredPackageDependency"/> that applies to a given set of frameworks.
            /// </summary>
            /// <param name="frameworks">Frameworks to consider. When empty, all dependencies apply.</param>
            /// <returns>The filtered set of dependencies.</returns>
            public IEnumerable<DeclaredPackageDependency> GetFilteredPackageReferences( CKTrait frameworks )
            {
                return frameworks.IsEmpty
                        ? Packages
                        : Packages.Where( p => p.Frameworks.Intersect( frameworks ) == frameworks );
            }

            /// <summary>
            /// Gets the <see cref="ProjectToProjectDependency"/> that applies to a given set of frameworks.
            /// </summary>
            /// <param name="frameworks">Frameworks to consider. When empty, all dependencies apply.</param>
            /// <returns>The filtered set of dependencies.</returns>
            public IEnumerable<ProjectToProjectDependency> GetFilteredProjectReferences( CKTrait frameworks )
            {
                return frameworks.IsEmpty
                        ? Projects
                        : Projects.Where( p => p.Frameworks.Intersect( frameworks ) == frameworks );
            }

            internal Dependencies( IReadOnlyList<DeclaredPackageDependency> packages,
                                   IReadOnlyList<ProjectToProjectDependency> projects,
                                   IReadOnlyList<XElement> uselessDeps )
            {
                Packages = packages;
                Projects = projects;
                UselessDependencies = uselessDeps;
            }

        }

        MSProjFile? _primaryFile;
        // Packages.props or Common/CentralPackages.props or whatever has been
        // successfully read from <CentralPackagesFile> property.
        // See https://github.com/microsoft/MSBuildSdks/tree/master/src/CentralPackageVersions#extensibility
        MSProjFile? _centralPackagesFile;
        MSProjFile? _directoryBuildPropsFile;
        MSProjFile? _directoryPackagesPropsFile;
        Dependencies _dependencies;

        internal MSProject( SolutionFile solution,
                            KnownProjectType type,
                            string projectGuid,
                            string projectName,
                            NormalizedPath relativePath )
            : base( solution, projectGuid, type.ToGuid(), projectName, relativePath )
        {
            Debug.Assert( KnownType.IsVSProject() );
            TargetFrameworks = Savors.EmptyTrait;
        }

        /// <summary>
        /// Gets the project file. This is loaded when the <see cref="Solution"/>
        /// is created. This is null if an error occurred while loading.
        /// </summary>
        public MSProjFile? ProjectFile => _primaryFile;

        internal override bool Initialize( FileSystem fs,
                                           IActivityMonitor m,
                                           Dictionary<NormalizedPath, MSProjFile> cache )
        {
            if( !base.Initialize( fs, m, cache ) ) return false;
            return ReloadProjectFile( fs, m, cache ) != null;
        }

        MSProjFile? ReloadProjectFile( FileSystem fs, IActivityMonitor monitor, Dictionary<NormalizedPath, MSProjFile> cache )
        {
            _primaryFile = MSProjFile.FindOrLoadProjectFile( fs, monitor, Path, cache );
            if( _primaryFile != null )
            {
                _directoryBuildPropsFile = FindMSProjFileAbove( fs, monitor, "Directory.Build.props", cache );
                if( _directoryBuildPropsFile != null )
                {
                    _primaryFile.AddImplicitImport( _directoryBuildPropsFile );
                    monitor.Trace( $"Implicitly importing '{_directoryBuildPropsFile.Path.RemovePrefix( Solution.SolutionFolderPath )}'." );
                }
                _directoryPackagesPropsFile = FindMSProjFileAbove( fs, monitor, "Directory.Packages.props", cache );
                if( _directoryPackagesPropsFile != null )
                {
                    _primaryFile.AddImplicitImport( _directoryPackagesPropsFile );
                    monitor.Trace( $"Implicitly importing '{_directoryPackagesPropsFile.Path.RemovePrefix( Solution.SolutionFolderPath )}'." );
                }

                var tE = _primaryFile.FindProperty( name => name.LocalName == "TargetFramework" || name.LocalName == "TargetFrameworks" );
                if( tE.Count != 1 )
                {
                    monitor.Error( $"There must be one and only one TargetFramework or TargetFrameworks element in '{Path}' or in its imports: {_primaryFile.AllFiles.Skip(1).Select( f => f.Path.Path ).Concatenate()}." );
                    _primaryFile = null;
                }
                else
                {
                    Debug.Assert( _primaryFile.Document.Root != null );
                    TargetFrameworks = Savors.FindOrCreate( tE[0].Value );

                    LangVersion = _primaryFile.FindProperty( "LangVersion" ).LastOrDefault()?.Value;
                    OutputType = _primaryFile.FindProperty( "OutputType" ).LastOrDefault()?.Value;
                    IsPackable = (bool?)_primaryFile.FindProperty( "IsPackable" ).LastOrDefault();

                    UseMicrosoftBuildCentralPackageVersions = _primaryFile.Document.Root.Elements( "Sdk" )
                                                                                    .Attributes( "Name" )
                                                                                    .Any( a => a.Value == "Microsoft.Build.CentralPackageVersions" );

                    if( UseMicrosoftBuildCentralPackageVersions )
                    {
                        monitor.Debug( "Microsoft.Build.CentralPackageVersions is used." );
                        NormalizedPath packageFile;
                        var definer = _primaryFile.AllFiles.Select( file => file.Document.Root )
                                             .SelectMany( root => root?.Elements( "PropertyGroup" ) ?? XElement.EmptySequence )
                                             .Elements()
                                             .FirstOrDefault( e => e.Name.LocalName == "CentralPackagesFile" );
                        if( definer != null )
                        {
                            monitor.Info( $"Found Property '{definer}' that defines CentralPackagesFile." );
                            var fileDefiner = _primaryFile.AllFiles.Single( file => file.Document == definer.Document );
                            packageFile = definer.Value.Replace( "$(MSBuildThisFileDirectory)", fileDefiner.Path.RemoveLastPart() + '/' );
                        }
                        else
                        {
                            monitor.Info( $"No CentralPackagesFile property found: looking for Packages.props in the Solution folder." );
                            packageFile = Solution.SolutionFolderPath.AppendPart( "Packages.props" );
                        }
                        _centralPackagesFile = MSProjFile.FindOrLoadProjectFile( fs, monitor, packageFile, cache );
                        if( _centralPackagesFile == null )
                        {
                            // Emits an error: reading the missing Version attribute will fail.
                            monitor.Error( $"Failed to read '{packageFile}' central package file." );
                        }
                    }
                    DoInitializeDependencies( monitor );
                    if( !_dependencies.IsInitialized ) _primaryFile = null;
                }
            }
            if( _primaryFile == null )
            {
                TargetFrameworks = Savors.EmptyTrait;
            }
            return _primaryFile;
        }

        MSProjFile? FindMSProjFileAbove( FileSystem fs, IActivityMonitor m, string fName, Dictionary<NormalizedPath, MSProjFile> cache )
        {
            if( !SolutionRelativeFolderPath.IsEmptyPath )
            {
                var p = SolutionRelativeFolderPath.RemoveLastPart();
                for(; ; )
                {
                    var f = MSProjFile.FindOrLoadProjectFile( fs, m, Solution.SolutionFolderPath.Combine( p ).AppendPart( fName ), cache, false );
                    if( f != null ) return f;
                    if( p.IsEmptyPath ) break;
                    p = p.RemoveLastPart();
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the &lt;IsPackable&gt; element value.
        /// Null if the project can not be read or if IsPackable is not defined.
        /// </summary>
        public bool? IsPackable { get; private set; }

        /// <summary>
        /// Gets the LangVersion value of the primary project file.
        /// Null if the project can not be read or if LangVersion is not defined.
        /// </summary>
        public string? LangVersion { get; private set; }

        /// <summary>
        /// Gets the OutputType element's value that is "Exe" for executable.
        /// Null if the project can not be read or if OutputType is not defined.
        /// </summary>
        public string? OutputType { get; private set; }

        /// <summary>
        /// Gets the target frameworks (from the <see cref="Savors"/> context).
        /// Empty if the project can not be read.
        /// </summary>
        public CKTrait TargetFrameworks { get; private set; }

        /// <summary>
        /// Gets whether Microsoft.Build.CentralPackageVersions is used thanks to:
        ///         &lt;Sdk Name="Microsoft.Build.CentralPackageVersions" Version="..." /&gt;
        /// </summary>
        public bool UseMicrosoftBuildCentralPackageVersions { get; private set; }

        /// <summary>
        /// Gets the .props file that contains the versions of all the packages.
        /// When <see cref="UseMicrosoftBuildCentralPackageVersions"/> is true, this SHOULD be not null.
        /// However, if the props file was not found, this is null (and this is an error that should be fixed by the user). 
        /// </summary>
        public MSProjFile? CentralPackageVersionsFile => _centralPackagesFile;

        /// <summary>
        /// Gets the dependencies.
        /// </summary>
        public Dependencies Deps => _dependencies;

        /// <summary>
        /// Gets the index of this project into the <see cref="SolutionFile.MSProjects"/> list.
        /// </summary>
        public int MSProjIndex => Solution.MSProjects.IndexOf( x => x == this );

        /// <summary>
        /// Gets whether this csproj file or one of its dependencies have changed.
        /// </summary>
        public bool IsDirty => _primaryFile != null && (_primaryFile.IsDirty || (_centralPackagesFile?.IsDirty ?? false));

        /// <summary>
        /// Saves all files that have been modified.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Save( IActivityMonitor m )
        {
            if( !_dependencies.IsInitialized ) throw new InvalidOperationException( "Invalid Project." );
            Debug.Assert( _primaryFile != null );
            return _primaryFile.Save( m, Solution.FileSystem )
                   && (_centralPackagesFile?.Save( m, Solution.FileSystem ) ?? true);
        }

        /// <summary>
        /// Sets the TargetFramework(s) element in the project file (from <see cref="MSProject.Savors"/> context).
        /// The dependencies are analyzed and new <see cref="Dependencies.UselessDependencies"/> may appear.
        /// </summary>
        /// <param name="m">The activity monitor to use.</param>
        /// <param name="frameworks">The framework(s) to set.</param>
        /// <returns>True if the change has been made. False if the frameworks are the same as the current one.</returns>
        public bool SetTargetFrameworks( IActivityMonitor m, CKTrait frameworks )
        {
            Throw.CheckNotNullOrEmptyArgument( frameworks );
            Throw.CheckArgument( "Must be from MSProject.Savors context.", frameworks.Context == Savors );
            Throw.CheckState( "Invalid project file.", _primaryFile != null );
            if( TargetFrameworks == frameworks ) return false;
            XElement? f = _primaryFile.Document.Root?
                            .Elements( "PropertyGroup" )
                            .Elements()
                            .Where( x => x.Name.LocalName == "TargetFramework" || x.Name.LocalName == "TargetFrameworks" )
                            .SingleOrDefault();
            Debug.Assert( f != null, "Otherwise we wouldn't have a _primaryFile." );
            f.ReplaceWith( new XElement( frameworks.IsAtomic ? "TargetFramework" : "TargetFrameworks", frameworks.ToString() ) );
            m.Trace( $"Replacing TargetFrameworks='{TargetFrameworks}' with '{frameworks}' in {ToString()}." );
            TargetFrameworks = frameworks;
            OnChange( m );
            return true;
        }

        /// <summary>
        /// Sets a package reference and returns the number of changes.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="frameworks">
        /// Frameworks that applies to the reference. Must not be empty.
        /// Can be this project's <see cref="TargetFrameworks"/> to update all the package reference regardless of the framework.
        /// </param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The new version to set.</param>
        /// <param name="addIfNotExists">True to add the reference. By default, it is only updated.</param>
        /// <param name="preserveExisting">True to keep any existing version.</param>
        /// <param name="throwOnProjectDependendencies">False to not challenge ProjectReferences.</param>
        /// <returns>The number of changes.</returns>
        public int SetPackageReferenceVersion( IActivityMonitor monitor,
                                               CKTrait frameworks,
                                               string packageId,
                                               SVersion version,
                                               bool addIfNotExists = false,
                                               bool preserveExisting = false,
                                               bool throwOnProjectDependendencies = true )
        {
            if( !_dependencies.IsInitialized ) throw new InvalidOperationException( "Invalid Project." );
            if( frameworks == null || frameworks.IsEmpty )
            {
                throw new ArgumentException( "Must not be empty.", nameof( frameworks ) );
            }
            Debug.Assert( TargetFrameworks != null && ProjectFile != null );
            if( throwOnProjectDependendencies && _dependencies.Projects.Any( p => p.TargetProject.ProjectName == packageId ) )
            {
                throw new ArgumentException( $"Package {packageId} is already a ProjectReference.", nameof( packageId ) );
            }
            var restrictedFrameworks = TargetFrameworks.Intersect( frameworks );
            if( restrictedFrameworks.IsEmpty )
            {
                monitor.Info( $"The applied frameworks '{frameworks}' don't appear in project's TargetFrameworks '{TargetFrameworks}'." );
                return 0;
            }
            var sV = version.ToString();
            int changeCount = 0;

            var depsToUpdate = _dependencies.Packages.Where( p => p.PackageId == packageId )
                                                     .Select( p => (P: p, F: p.Frameworks.Intersect( restrictedFrameworks )) )
                                                     .Where( p => !p.F.IsEmpty );
            CKTrait remainingFrameworksToUpdate = restrictedFrameworks;
            foreach( var dep in depsToUpdate )
            {
                remainingFrameworksToUpdate = restrictedFrameworks.Except( dep.F );

                if( dep.P.Version.Lock == SVersionLock.Lock )
                {
                    monitor.Warn( $"The version '{dep.P.Version}' of {packageId} is locked in '{dep.P.OriginElement}' for frameworks '{dep.F}'. Skipping update to '{version}'." );
                    continue;
                }
                if( !dep.P.Version.Satisfy( version ) )
                {
                    monitor.Info( $"Updating new version '{version}' for frameworks '{dep.F}' that is not compatible with the {packageId} previous version range '{dep.P.Version}': '{dep.P.OriginElement}'." );
                }

                var currentVersion = dep.P.Version.Base;
                if( currentVersion != version )
                {
                    if( !preserveExisting )
                    {
                        var e = dep.P.FinalVersionElement;
                        if( e != null )
                        {
                            // <PackageReference Update="CK.Core" Version="13.0.1" /> centrally managed package or
                            // <CKCoreVersion>13.0.1</CKCoreVersion>.
                            if( e.Name == "PackageReference" )
                            {
                                e.SetAttributeValue( "Version", sV );
                            }
                            else
                            {
                                e.Value = sV;
                            }
                        }
                        else
                        {
                            e = dep.P.OriginElement;
                            e.Attribute( dep.P.IsVersionOverride ? "VersionOverride" : "Version" )!.SetValue( sV );
                        }
                        ++changeCount;
                        monitor.Trace( $"Update in {ToString()}: '{packageId}' from '{currentVersion}' to '{sV}' for frameworks '{dep.F}'." );
                    }
                    else
                    {
                        monitor.Trace( $"Preserving existing version '{currentVersion}' for '{packageId}' in {ToString()} for frameworks '{dep.F}' (skipped version is '{sV}')." );
                    }
                }
            }
            // Handle creation if needed.
            if( !remainingFrameworksToUpdate.IsEmpty && addIfNotExists )
            {
                Debug.Assert( ProjectFile.Document.Root != null );
                var firstPropertyGroup = ProjectFile.Document.Root.Element( "PropertyGroup" );
                Debug.Assert( firstPropertyGroup != null, "There's necessarily at least one PropertyGroup." );
                var pRef = new XElement( "ItemGroup",
                                new XElement( "PackageReference",
                                    new XAttribute( "Include", packageId ),
                                    new XAttribute( "Version", sV ) ) );
                if( TargetFrameworks == remainingFrameworksToUpdate )
                {
                    ++changeCount;
                    firstPropertyGroup.AddAfterSelf( pRef );
                    monitor.Trace( $"Added unconditional package reference {packageId} -> {sV} for {ToString()}." );
                }
                else
                {
                    foreach( var f in remainingFrameworksToUpdate.AtomicTraits )
                    {
                        ++changeCount;
                        var withCond = new XElement( pRef );
                        withCond.SetAttributeValue( "Condition", $"'(TargetFrameWork)' == '{f}' " );
                        firstPropertyGroup.AddAfterSelf( withCond );
                        monitor.Trace( $"Added conditional '{f}' package reference '{packageId}' -> '{sV}' for {ToString()}." );
                    }
                }
            }
            if( changeCount > 0 ) OnChange( monitor );
            return changeCount;
        }

        void DoSetSimpleProperty( IActivityMonitor m, string elementName, string? value )
        {
            Debug.Assert( _primaryFile != null && _primaryFile.Document.Root != null );
            _primaryFile.Document.Root
                    .Elements( "PropertyGroup" )
                    .Elements( elementName ).Remove();
            if( value != null )
            {
                _primaryFile.Document.Root
                        .EnsureElement( "PropertyGroup" )
                        .SetElementValue( elementName, value );
            }
            OnChange( m );
        }

        /// <summary>
        /// Sets or removes the LangVersion element.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="langVersion">The new LangVersion or null to remove it.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SetLangVersion( IActivityMonitor m, string? langVersion )
        {
            if( LangVersion != langVersion )
            {
                DoSetSimpleProperty( m, "LangVersion", langVersion );
                LangVersion = langVersion;
            }
            return true;
        }

        /// <summary>
        /// Sets or removes the Nullable element.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="nullable">The new Nullable (enable, disable, warnings or annotations) or null to remove it.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SetNullable( IActivityMonitor m, string? nullable )
        {
            DoSetSimpleProperty( m, "Nullable", nullable );
            return true;
        }

        /// <summary>
        /// Sets the value or removes the OutputType element.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="outputType">The new output type or null to remove it.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SetOutputType( IActivityMonitor m, string outputType )
        {
            if( OutputType != outputType )
            {
                DoSetSimpleProperty( m, "OutputType", outputType );
                OutputType = outputType;
            }
            return true;
        }

        /// <summary>
        /// Sets the value or remove the IsPackable element.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="isPackable">The new IsPackable or <see langword="null"/> to remove it.</param>
        /// <returns></returns>
        public bool SetIsPackable( IActivityMonitor m, bool? isPackable )
        {
            if( isPackable != IsPackable )
            {
                DoSetSimpleProperty( m, "IsPackable", isPackable?.ToString() );
            }
            return true;
        }

        /// <summary>
        /// Removes the <see cref="Dependencies.UselessDependencies"/> and returns the number of changes.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <returns>The number of changes.</returns>
        public int RemoveUselessDependencies( IActivityMonitor m )
        {
            if( !_dependencies.IsInitialized ) throw new InvalidOperationException( "Invalid Project." );
            int changeCount = _dependencies.UselessDependencies.Count;
            if( changeCount > 0 )
            {
                var parents = _dependencies.UselessDependencies.Select( p => p.Parent ).Distinct().ToList();
                _dependencies.UselessDependencies.Remove();
                if( parents.Count > 0 )
                {
                    changeCount += parents.Where( e => !e.HasElements ).Count();
                    parents.Where( e => !e.HasElements ).Remove();
                }
                OnChange( m );
            }
            return changeCount;
        }

        /// <summary>
        /// Removes any version of a package reference.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">Package identifier.</param>
        /// <returns>The number of changes.</returns>
        public int RemoveDependency( IActivityMonitor m, string packageId )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
            var toRemove = _dependencies.Packages.Where( d => d.PackageId == packageId ).ToList();
            if( toRemove.Count > 0 )
            {
                return RemoveDependencies( m, toRemove );
            }
            return 0;
        }

        /// <summary>
        /// Removes a set of dependencies.
        /// </summary>
        /// <param name="toRemove">Set of dependencies to remove.</param>
        /// <returns>The number of changes.</returns>
        public int RemoveDependencies( IActivityMonitor m, IReadOnlyList<DeclaredPackageDependency> toRemove )
        {
            Throw.CheckState( _dependencies.IsInitialized );
            Throw.CheckNotNullArgument( toRemove );
            if( toRemove.Count == 0 ) return 0;
            var extra = toRemove.FirstOrDefault( r => !_dependencies.Packages.Contains( r ) );
            if( extra != null ) Throw.ArgumentException( nameof( toRemove ), $"Dependency not contained: {extra}." );
            int changeCount = toRemove.Count;
            var parents = toRemove.Select( p => p.OriginElement.Parent ).Distinct().ToList();
            changeCount += parents.Count;
            toRemove.Select( r =>
            {
                m.Trace( $"Remove in {ToString()}: {r.PackageId}." );
                return r.OriginElement;
            } ).Remove();
            // Removes empty <ItemGroup />.
            parents.Where( p => !p.HasElements ).Remove();
            OnChange( m );
            return changeCount;
        }

        public void StopUsingPropertyVersionAndOldCentralPackageManagement( IActivityMonitor monitor )
        {
            bool hasChanged = false;
            HashSet<XElement> oldProperties = new HashSet<XElement>();
            foreach( var d in _dependencies.Packages )
            {
                if( d.FinalVersionElement != null )
                {
                    hasChanged = true;
                    if( d.FinalVersionElement.Name.LocalName != "PackageReference" )
                    {
                        // This forgets the "$(XXXVersion)".
                        d.OriginElement.SetAttributeValue( "Version", d.FinalVersionElement.Value );
                        // Crappy! Here we remove the element and this should be done above (at the solution level).
                        // The first project to reference the version with detach the element. The other ones must not try to remove it.
                        // This works only because apply this on all the projects at the same time.
                        // Keep this as-is since this is temporary code.
                        if( d.FinalVersionElement.Parent != null ) oldProperties.Add( d.FinalVersionElement );
                    }
                    else
                    {
                        // Central Package Management.
                        if( d.IsVersionOverride )
                        {
                            d.OriginElement.SetAttributeValue( "Version", d.OriginElement.Attribute( "VersionOverride" )?.Value );
                            d.OriginElement.Attribute( "VersionOverride" )?.Remove();
                        }
                        else
                        {
                            d.OriginElement.SetAttributeValue( "Version", d.FinalVersionElement.Attribute( "Version" )?.Value );
                        }
                    }
                }
            }
            if( hasChanged )
            {
                // Removing "$(XXXVersion)" properties.
                monitor.Info( $"Removing now useless {oldProperties.Select( e => e.ToString() ).Concatenate()} elements." );
                var parents = oldProperties.Select( e => e.Parent ).Distinct().ToList();
                System.Xml.Linq.Extensions.Remove( oldProperties );
                parents.Where( p => !p.HasElements ).Remove();
                // Removes the Sdk import.
                _primaryFile.Document.Root.Elements( "Sdk" ).Where( e => e.Attribute( "Name" )?.Value == "Microsoft.Build.CentralPackageVersions" ).Remove();
                UseMicrosoftBuildCentralPackageVersions = false;
                _centralPackagesFile = null;
                OnChange( monitor );
            }
        }

        void OnChange( IActivityMonitor m )
        {
            DoInitializeDependencies( m );
            if( !_dependencies.IsInitialized )
            {
                throw new Exception( "Altering project files must produce valid dependencies." );
            }
            Solution.CheckDirtyProjectFiles( true );
        }

        void DoInitializeDependencies( IActivityMonitor m )
        {
            Debug.Assert( _primaryFile != null );
            var packageRefs = _primaryFile.AllFiles.Select( f => f.Document.Root )
                             .SelectMany( root => root.Elements( "ItemGroup" )
                                                        .Elements()
                                                        .Where( e => e.Name.LocalName == "PackageReference"
                                                                     || e.Name.LocalName == "ProjectReference" ) )
                             .Select( e => (Origin: e,
                                            PackageId: (string)e.Attribute( "Include" ),
                                            RawVersion: (string)e.Attribute( "Version" ) ?? (string)e.Element( "Version" ),
                                            PrivateAssets: (string)e.Attribute( "PrivateAssets" ) ?? (string)e.Element( "PrivateAssets" ) ?? ""
                                            ) );

            var conditionEvaluator = new PartialEvaluator();
            var deps = new List<DeclaredPackageDependency>();
            var uselessDeps = new List<XElement>();
            var projs = new List<ProjectToProjectDependency>();
            foreach( var p in packageRefs )
            {
                if( p.Origin.Name.LocalName == "PackageReference" )
                {
                    if( p.Origin.Attribute( "Include" ) == null )
                    {
                        m.Warn( $"Element {p.Origin} misses Include attribute. It is ignored." );
                        continue;
                    }
                        if( string.IsNullOrWhiteSpace( p.PackageId ) )
                        {
                            m.Error( $"Invalid Include attribute on element {p.Origin}." );
                            return;
                        }

                    XElement? propertyDef = null;
                    bool isVersionOverride = false;
                    // Since we play with flags, static analysis fails to detect that this is always initialized.
                    SVersionBound.ParseResult versionBound = new SVersionBound.ParseResult( "Never happen." );

                    if( p.RawVersion == null )
                    {
                        // No Version attribute nor element: we must use Central packages!
                        if( _centralPackagesFile == null )
                        {
                            if( UseMicrosoftBuildCentralPackageVersions )
                            {
                                m.Error( $"Missing Version attribute (or child element) on element {p.Origin} and Microsoft.Build.CentralPackageVersions is not operational: the props file (via CentralPackagesFile property or Packages.props in solution folder) has not been found." );
                            }
                            else
                            {
                                m.Warn( $"Missing Version attribute (or child element) on element {p.Origin} (and Microsoft.Build.CentralPackageVersions is not used). This is ignored." );
                            }
                            continue;
                        }
                        // We are using CentralPackageVersions: VersionOverride may be used!
                        var vO = (string?)p.Origin.Attribute( "VersionOverride" );
                        if( vO != null )
                        {
                            isVersionOverride = true;
                            versionBound = ParseNugetBound( m, vO );
                            if( !versionBound.IsValid )
                            {
                                m.Error( $"Unable to parse VersionOverride attribute on element {p.Origin}: {versionBound.Error}" );
                                return;
                            }
                            m.Warn( $"VersionOverride is used for package {p.PackageId}: {versionBound}." );
                        }
                        if( !isVersionOverride )
                        {
                            // We must find the <Package Update= ... version.
                            propertyDef = _centralPackagesFile.Document.Root.Elements().Where( e => e.Name.LocalName == "ItemGroup" )
                                                                            .Elements().Where( e => e.Name.LocalName == "PackageReference" )
                                                                            .FirstOrDefault( e => (string)e.Attribute( "Update" ) == p.PackageId );
                            if( propertyDef == null )
                            {
                                propertyDef = _centralPackagesFile.Document.Root.Elements().Where( e => e.Name.LocalName == "ItemGroup" )
                                                                                    .Elements().Where( e => e.Name.LocalName == "PackageReference" )
                                                                                    .FirstOrDefault( e => p.PackageId.Equals( (string)e.Attribute( "Update" ), StringComparison.OrdinalIgnoreCase ) );
                                if( propertyDef == null )
                                {
                                    m.Error( $"Unable to find a version for '{p.PackageId}' in central package file '{_centralPackagesFile.Path}'." );
                                    return;
                                }
                                m.Warn( $"Found a package version for '{p.PackageId}' in central package file '{_centralPackagesFile.Path}': '{propertyDef.Attribute( "Update" )}' case differ." );
                            }
                            if( !(versionBound = ParseNugetBound( m, (string)propertyDef.Attribute( "Version" ) )).IsValid )
                            {
                                m.Error( $"Unable to parse Version attribute on element {propertyDef} in central package file '{_centralPackagesFile.Path}': {versionBound.Error}" );
                                return;
                            }
                        }
                    }
                    else
                    {
                        // Check empty or whitespace.
                        if( string.IsNullOrWhiteSpace( p.RawVersion ) )
                        {
                            m.Error( $"Invalid Version attribute on element {p.Origin}." );
                            return;
                        }
                        bool isPropVersion = p.RawVersion.StartsWith( "$(" );
                        if( !isPropVersion )
                        {
                            versionBound = ParseNugetBound( m, p.RawVersion );
                            if( !versionBound.IsValid )
                            {
                                m.Error( $"Unable to parse Version attribute on element {p.Origin}: {versionBound.Error}" );
                                return;
                            }
                        }
                        else
                        {
                            versionBound = FollowRefPropertyVersion( m, p, out propertyDef );
                            if( !versionBound.IsValid )
                            {
                                m.Error( versionBound.Error );
                                return;
                            }
                        }
                    }


                    string[] defaultValues = new string[] { "compile", "runtime", "contentFiles", "build", "analyzers", "native" };

                    CKTrait? frameworks = ComputeFrameworks( m, p.Origin, conditionEvaluator );
                    if( frameworks == null ) return;
                    if( frameworks.IsEmpty )
                    {
                        m.Warn( $"Useless PackageReference (applies to undeclared frameworks): {p.Origin}." );
                        uselessDeps.Add( p.Origin );
                        continue;
                    }
                    deps.Add( new DeclaredPackageDependency( this, p.PackageId, versionBound.Result, p.Origin, propertyDef, frameworks, p.PrivateAssets, isVersionOverride ) );
                }
                else
                {
                    // This is a ProjectReference.
                    string projectName = new NormalizedPath( p.PackageId ).LastPart;
                    if( !projectName.EndsWith( ".csproj" ) )
                    {
                        m.Error( $"ProjectReference must Include a .csproj project: {p.Origin}." );
                        return;
                    }
                    projectName = projectName.Substring( 0, projectName.Length - 7 );
                    var target = Solution.MSProjects.FirstOrDefault( pRef => pRef.ProjectName == projectName );
                    if( target == null )
                    {
                        m.Warn( $"ProjectReference '{p.PackageId}' not found in the solution. Project name '{projectName}' should exist in the solution." );
                        uselessDeps.Add( p.Origin );
                        continue;
                    }
                    CKTrait? frameworks = ComputeFrameworks( m, p.Origin, conditionEvaluator );
                    if( frameworks == null ) return;
                    if( frameworks.IsEmpty )
                    {
                        m.Warn( $"Useless ProjectReference (applies to undeclared frameworks): {p.Origin}." );
                        uselessDeps.Add( p.Origin );
                        continue;
                    }
                    projs.Add( new ProjectToProjectDependency( this, target, frameworks, p.Origin ) );
                }
            }
            // Consider Solution level dependency as active for all TargetFrameworks.
            if( !EnumerateSolutionLevelProjectDependencies( m,
                    p => projs.Add( new ProjectToProjectDependency( this, p, TargetFrameworks, null ) ) ) )
            {
                return;
            }
            _dependencies = new Dependencies( deps, projs, uselessDeps );
        }

        static SVersionBound.ParseResult ParseNugetBound( IActivityMonitor m, string r )
        {
            SVersionBound.ParseResult v = SVersionBound.NugetTryParse( r );
            if( v.IsValid && v.IsApproximated )
            {
                m.Warn( $"Version range '{r}' has been approximated to version bound '{v.Result}'." );
            }
            return v;
        }

        CKTrait? ComputeFrameworks( IActivityMonitor m, XElement e, PartialEvaluator evaluator )
        {
            Debug.Assert( TargetFrameworks != null );
            CKTrait frameworks = TargetFrameworks;
            foreach( var framework in TargetFrameworks.AtomicTraits )
            {
                foreach( var (E, C) in e.AncestorsAndSelf()
                                        .Select( x => (E: x, C: (string)x.Attribute( "Condition" )) )
                                        .Where( x => x.C != null ) )
                {
                    bool? include = evaluator.EvalFinalResult( m, C, f => f == "$(TargetFramework)" ? framework.ToString() : null );
                    if( include == null )
                    {
                        m.Error( $"Unable to evaluate condition of {E}." );
                        return null;
                    }
                    if( include == false )
                    {
                        frameworks = frameworks.Except( framework );
                    }
                }
            }
            return frameworks;
        }

        SVersionBound.ParseResult FollowRefPropertyVersion( IActivityMonitor m, (XElement Origin, string PackageId, string RawVersion, string) p, out XElement? propertyDef )
        {
            propertyDef = null;
            Debug.Assert( _primaryFile != null );
            if( !p.RawVersion.EndsWith( "Version)" ) )
            {
                return new SVersionBound.ParseResult( $"Invalid $(PropertyVersion) on element {p.Origin}. Its name must end with 'Version'." );
            }
            // Lookup for the property.
            string propName = p.RawVersion.Substring( 2, p.RawVersion.Length - 3 );
            var candidates = _primaryFile.AllFiles.Select( f => f.Document.Root )
                                 .SelectMany( root => root.Elements( "PropertyGroup" ) )
                                 .Elements()
                                 .Where( e => e.Name.LocalName == propName ).ToList();
            if( candidates.Count == 0 )
            {
                return new SVersionBound.ParseResult( $"Unable to find $({propName}) version definition for element {p.Origin}." );
            }
            if( candidates.Count > 1 )
            {
                return new SVersionBound.ParseResult( $"Found more than one $({propName}) version definition for element {p.Origin}." );
            }
            var pDef = candidates[0];
            var v = ParseNugetBound( m, pDef.Value );
            if( !v.IsValid )
            {
                return v.AddError( $"Invalid $({propName}) version definition {p.Origin} in {pDef}." );
            }
            propertyDef = pDef;
            return v;
        }

    }
}
