using CK.Core;
using CK.Build;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

#nullable enable

namespace CK.Env.MSBuildSln
{
    public class MSProject : Project
    {
        /// <summary>
        /// Definition of NuGet artifact type: its name is "NuGet", it is installable and its savors
        /// use ';' as a seprator to match the one used by csproj.
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
            /// Gets whether this structure hass been initialized.
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

            internal Dependencies(
                IReadOnlyList<DeclaredPackageDependency> packages,
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
        Dependencies _dependencies;

        internal MSProject(
                    SolutionFile solution,
                    KnownProjectType type,
                    string projectGuid,
                    string projectName,
                    NormalizedPath relativePath )
            : base( solution, projectGuid, type.ToGuid(), projectName, relativePath )
        {
            Debug.Assert( KnownType.IsVSProject() );
        }

        /// <summary>
        /// Gets the project file. This is loaded when the <see cref="Solution"/>
        /// is created. This is null if an error occurred while loading.
        /// </summary>
        public MSProjFile? ProjectFile => _primaryFile;

        internal override bool Initialize(
            FileSystem fs,
            IActivityMonitor m,
            Dictionary<NormalizedPath, MSProjFile> cache )
        {
            if( !base.Initialize( fs, m, cache ) ) return false;
            return ReloadProjectFile( fs, m, cache ) != null;
        }

        MSProjFile? ReloadProjectFile( FileSystem fs, IActivityMonitor m, Dictionary<NormalizedPath, MSProjFile> cache )
        {
            _primaryFile = MSProjFile.FindOrLoadProjectFile( fs, m, Path, cache );
            if( _primaryFile != null )
            {
                XElement f = _primaryFile.Document.Root
                                .Elements( "PropertyGroup" )
                                .Elements()
                                .Where( x => x.Name.LocalName == "TargetFramework" || x.Name.LocalName == "TargetFrameworks" )
                                .SingleOrDefault();
                if( f == null )
                {
                    m.Error( $"There must be one and only one TargetFramework or TargetFrameworks element in {Path}." );
                    _primaryFile = null;
                }
                else
                {
                    TargetFrameworks = Savors.FindOrCreate( f.Value );

                    LangVersion = _primaryFile.Document.Root.Elements( "PropertyGroup" ).Elements( "LangVersion" ).LastOrDefault()?.Value;
                    OutputType = _primaryFile.Document.Root.Elements( "PropertyGroup" ).Elements( "OutputType" ).LastOrDefault()?.Value;
                    IsPackable = (bool?)_primaryFile.Document.Root.Elements( "PropertyGroup" ).Elements( "IsPackable" ).LastOrDefault();

                    UseMicrosoftBuildCentralPackageVersions = _primaryFile.Document.Root.Elements( "Sdk" )
                                                                                    .Attributes( "Name" )
                                                                                    .Any( a => a.Value == "Microsoft.Build.CentralPackageVersions" );
                    if( UseMicrosoftBuildCentralPackageVersions )
                    {
                        m.Debug( "Microsoft.Build.CentralPackageVersions is used." );
                        NormalizedPath packageFile;
                        var definer = _primaryFile.AllFiles.Select( file => file.Document.Root )
                                             .SelectMany( root => root.Elements( "PropertyGroup" ) )
                                             .Elements()
                                             .FirstOrDefault( e => e.Name.LocalName == "CentralPackagesFile" );
                        if( definer != null )
                        {
                            m.Info( $"Found Property '{definer}' that defines CentralPackagesFile." );
                            var fileDefiner = _primaryFile.AllFiles.Single( file => file.Document == definer.Document );
                            packageFile = definer.Value.Replace( "$(MSBuildThisFileDirectory)", fileDefiner.Path.RemoveLastPart() + '/' );
                        }
                        else
                        {
                            m.Info( $"No CentralPackagesFile property found: looking for Packages.props in the Solution folder." );
                            packageFile = Solution.SolutionFolderPath.AppendPart( "Packages.props" );
                        }
                        _centralPackagesFile = MSProjFile.FindOrLoadProjectFile( fs, m, packageFile, cache );
                        if( _centralPackagesFile == null )
                        {
                            // Emits an error: reading the missing Version attribute will fail.
                            m.Error( $"Failed to read '{packageFile}' central package file." );
                        }
                    }
                    DoInitializeDependencies( m );
                    if( !_dependencies.IsInitialized ) _primaryFile = null;
                }
            }
            if( _primaryFile == null )
            {
                TargetFrameworks = Savors.EmptyTrait;
            }
            return _primaryFile;
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
        /// Gets the target frameforks (from the <see cref="Savors"/> context).
        /// Null if the project can not be read.
        /// </summary>
        public CKTrait? TargetFrameworks { get; private set; }

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
        /// Gets whether this csproj file or one of its depdendencies have changed.
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
        /// The dependencies are analysed and new <see cref="Dependencies.UselessDependencies"/> may appear.
        /// </summary>
        /// <param name="m">The activity monitor to use.</param>
        /// <param name="frameworks">The framework(s) to set.</param>
        /// <returns>True if the change has been made. False if the frameworks are the same as the current one.</returns>
        public bool SetTargetFrameworks( IActivityMonitor m, CKTrait frameworks )
        {
            if( frameworks?.IsEmpty ?? true ) throw new ArgumentException( "Must not be null or empty.", nameof( frameworks ) );
            if( frameworks.Context != Savors ) throw new ArgumentException( "Must be from MSProject.Savors context.", nameof( frameworks ) );
            if( _primaryFile == null ) throw new InvalidOperationException( "Invalid project file." );
            if( TargetFrameworks == frameworks ) return false;
            XElement f = _primaryFile.Document.Root
                            .Elements( "PropertyGroup" )
                            .Elements()
                            .Where( x => x.Name.LocalName == "TargetFramework" || x.Name.LocalName == "TargetFrameworks" )
                            .SingleOrDefault();
            f.ReplaceWith( new XElement( frameworks.IsAtomic ? "TargetFramework" : "TargetFrameworks", frameworks.ToString() ) );
            m.Trace( $"Replacing TargetFrameworks='{TargetFrameworks}' with '{frameworks}' in {ToString()}." );
            TargetFrameworks = frameworks;
            OnChange( m );
            return true;
        }

        /// <summary>
        /// Sets a package reference and returns the number of changes.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="frameworks">
        /// Frameworks that applies to the reference. Must not be empty.
        /// Can be this project's <see cref="TargetFrameworks"/> to update the package reference regardless of the framework.
        /// </param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The new version to set.</param>
        /// <param name="addIfNotExists">True to add the reference. By default, it is only updated.</param>
        /// <param name="preserveExisting">True to keep any existing version.</param>
        /// <param name="throwProjectDependendencies">False to not challenge ProjectReferences.</param>
        /// <returns>The number of changes.</returns>
        public int SetPackageReferenceVersion(
            IActivityMonitor m,
            CKTrait frameworks,
            string packageId,
            SVersion version,
            bool addIfNotExists = false,
            bool preserveExisting = false,
            bool throwProjectDependendencies = true )
        {
            if( !_dependencies.IsInitialized ) throw new InvalidOperationException( "Invalid Project." );
            if( frameworks.IsEmpty ) throw new ArgumentException( "Must not be empty.", nameof( frameworks ) );

            Debug.Assert( TargetFrameworks != null && ProjectFile != null );
            var actualFrameworks = TargetFrameworks.Intersect( frameworks );
            if( actualFrameworks.IsEmpty ) throw new ArgumentException( $"No {frameworks} in {TargetFrameworks}.", nameof( frameworks ) );
            if( throwProjectDependendencies && _dependencies.Projects.Any( p => p.TargetProject.ProjectName == packageId ) )
            {
                throw new ArgumentException( $"Package {packageId} is already a ProjectReference.", nameof( packageId ) );
            }
            var sV = version.ToString();
            int changeCount = 0;
            CKTrait? pFrameworks = null;
            foreach( var p in _dependencies.Packages.Where( p => p.PackageId == packageId
                                                                 && !(pFrameworks = p.Frameworks.Intersect( actualFrameworks )).IsEmpty ) )
            {
                if( !p.Version.Satisfy( version ) )
                {
                    m.Warn( $"The version '{version}' is not compatible with the {packageId} version range that is '{p.Version}'. You need to change it manually." );
                    continue;
                }
                actualFrameworks = actualFrameworks.Except( pFrameworks );
                var currentVersion = p.Version.Base;
                if( currentVersion != version )
                {
                    if( !preserveExisting )
                    {
                        var e = p.FinalVersionElement;
                        if( e != null )
                        {
                            // <PackageReference Update="CK.Core" Version="13.0.1" /> centrally managed
                            // package or <CKCoreVersion>13.0.1</CKCoreVersion>.
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
                            e = p.OriginElement;
                            e.Attribute( p.IsVersionOverride ? "VersionOverride" : "Version" ).SetValue( sV );
                        }
                        ++changeCount;
                        m.Trace( $"Update in {ToString()}: {packageId} from {currentVersion} to {sV}." );
                    }
                    else
                    {
                        m.Trace( $"Preserving existing version {currentVersion} for {packageId} in {ToString()} (skipped version is {sV})." );
                    }
                }
            }
            // Handle creation if needed.
            if( !actualFrameworks.IsEmpty && addIfNotExists )
            {
                var firstPropertyGroup = ProjectFile.Document.Root.Element( "PropertyGroup" );
                var pRef = new XElement( "ItemGroup",
                                new XElement( "PackageReference",
                                    new XAttribute( "Include", packageId ),
                                    new XAttribute( "Version", sV ) ) );
                if( TargetFrameworks == actualFrameworks )
                {
                    ++changeCount;
                    firstPropertyGroup.AddAfterSelf( pRef );
                    m.Trace( $"Added unconditional package reference {packageId} -> {sV} for {ToString()}." );
                }
                else
                {
                    foreach( var f in actualFrameworks.AtomicTraits )
                    {
                        ++changeCount;
                        var withCond = new XElement( pRef );
                        withCond.SetAttributeValue( "Condition", $"'(TargetFrameWork)' == '{f}' " );
                        firstPropertyGroup.AddAfterSelf( withCond );
                        m.Trace( $"Added conditional {f} package reference {packageId} -> {sV} for {ToString()}." );
                    }
                }
            }
            if( changeCount > 0 ) OnChange( m );
            return changeCount;
        }

        void DoSetSimpleProperty( IActivityMonitor m, string elementName, string? value )
        {
            Debug.Assert( _primaryFile != null );
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
        public bool SetLangVersion( IActivityMonitor m, string langVersion )
        {
            if( LangVersion != langVersion )
            {
                DoSetSimpleProperty( m, "LangVersion", langVersion );
                LangVersion = langVersion;
            }
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
            if( String.IsNullOrWhiteSpace( packageId ) ) throw new ArgumentNullException( nameof( packageId ) );
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
        /// <param name="toRemove">Set of dependenies to remove.</param>
        /// <returns>The number of changes.</returns>
        public int RemoveDependencies( IActivityMonitor m, IReadOnlyList<DeclaredPackageDependency> toRemove )
        {
            if( !_dependencies.IsInitialized ) throw new InvalidOperationException( "Invalid Project." );
            if( toRemove == null ) throw new ArgumentException( "Empty dependency to remove.", nameof( toRemove ) );
            if( toRemove.Count == 0 ) return 0;
            var extra = toRemove.FirstOrDefault( r => !_dependencies.Packages.Contains( r ) );
            if( extra != null ) throw new ArgumentException( $"Dependency not contained: {extra}.", nameof( toRemove ) );
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
                        var vO = (string)p.Origin.Attribute( "VersionOverride" );
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
                            if( !versionBound.IsValid ) return;
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
            if( v.IsValid )
            {
                if( v.FourthPartLost ) m.Warn( $"4th part (or more) ingored in version range: '{r}'." );
                if( v.IsApproximated ) m.Warn( $"Version range '{r}' has been approximated to version bound '{v.Result}'." );
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
