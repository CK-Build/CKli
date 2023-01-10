using CK.Build;
using CK.Core;
using CSemVer;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using Microsoft.Extensions.FileProviders;
using System.Threading;
using System.Xml.Linq;
using Newtonsoft.Json;
using YamlDotNet.Core.Tokens;
using System.Diagnostics.CodeAnalysis;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Specialized <see cref="JsonFileBase"/> for package.json file.
    /// </summary>
    public partial class PackageJsonFile
    {
        readonly List<NodeProjectDependency> _deps;
        readonly JObject _o;
        readonly NodeProjectBase _project;
        string? _name;
        SVersion _version;
        bool _private;
        bool _isDirty;
        readonly NormalizedPath[] _workspaces;

        PackageJsonFile( NormalizedPath filePath,
                         JObject o,
                         NodeProjectBase project,
                         bool isPrivate,
                         string? name,
                         SVersion version,
                         NormalizedPath[]? workspaces )
        {
            _deps = new List<NodeProjectDependency>();
            FilePath = filePath;
            _o = o;
            _project = project;
            _private = isPrivate;
            _name = name;
            _version = version;
            _workspaces = workspaces ?? Array.Empty<NormalizedPath>();
        }

        /// <summary>
        /// Gets whether this file needs to be saved.
        /// It is the <see cref="NodeProjectBase.Save(IActivityMonitor)"/> to which this manifest belongs
        /// that is in charge of saving this file.
        /// </summary>
        public bool IsDirty => _isDirty;

        internal bool Save( IActivityMonitor monitor )
        {
            return _project.Solution.FileSystem.CopyTo( monitor, _o.ToString( Formatting.Indented ), FilePath );
        }

        /// <summary>
        /// Gets or sets the name.
        /// Can be null: a non-published package does not require a name (nor a <see cref="Version"/>).
        /// <para>
        /// This can be set to null only if <see cref="IsPrivate"/> is true.
        /// </para>
        /// </summary>
        public string? Name
        {
            get => _name;
            set
            {
                value = value?.Trim();
                if( string.IsNullOrEmpty( value ) ) value = null;
                if( _name != value )
                {
                    if( value == null && !_private )
                    {
                        Throw.InvalidOperationException( "Name cannot be empty when \"private\" is true." );
                    }
                    _name = value;
                    _o["name"] = value;
                    _isDirty = true;
                    _project.SetDirty( false );
                }
            }
        }

        /// <summary>
        /// Gets the name.
        /// When there is no <see cref="Name"/>, the folder's name is returned: this SafeName always exists.
        /// </summary>
        public string SafeName => Name ?? FilePath.Parts[FilePath.Parts.Count - 2];

        /// <summary>
        /// Gets or sets whether this package is private: even if it has a <see cref="Name"/>
        /// and a <see cref="Version"/>, it must not be published.
        /// <para>
        /// This can be set to false only if a non null <see cref="Name"/> exists.
        /// </para>
        /// </summary>
        [MemberNotNullWhen( false, nameof( Name ) )]
        public bool IsPrivate
        {
            get => _private;
            set
            {
                if( _private != value )
                {
                    if( _name == null && !value )
                    {
                        Throw.InvalidOperationException( "\"private\" cannot be true without a non empty package \"name\"." );
                    }
                    _private = value;
                    _o["private"] = value;
                    _isDirty = true;
                    _project.SetDirty( false );
                }
            }
        }

        /// <summary>
        /// Gets or sets the version.
        /// This should always be the <see cref="SVersion.ZeroVersion"/> except when the actual version is
        /// computed during a build/packaging.
        /// </summary>
        public SVersion Version
        {
            get => _version;
            set
            {
                Throw.CheckArgument( value.IsValid );
                if( _version != value )
                {
                    _version = value;
                    _o["version"] = value.ToNormalizedString();
                    _isDirty = true;
                    _project.SetDirty( false );
                }
            }
        }

        /// <summary>
        /// Gets the subordinated paths of the "workspaces" property.
        /// </summary>
        public IReadOnlyList<NormalizedPath> Workspaces => _workspaces;

        /// <summary>
        /// Gets the list of dependencies.
        /// </summary>
        public IReadOnlyList<NodeProjectDependency> Dependencies => _deps;

        /// <summary>
        /// Gets this file path.
        /// </summary>
        public NormalizedPath FilePath { get; }

        /// <summary>
        /// Sets a minimum version for a dependency that must exist, regardless of its current status.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageId">The dependency name that must appear in <see cref="Dependencies"/>.</param>
        /// <param name="version">
        /// The minimal version: will be the <see cref="SVersionBound.Base"/> with none <see cref="SVersionBound.Lock"/>
        /// and <see cref="SVersionBound.MinQuality"/>.
        /// </param>
        /// <returns>True if the version has been changed, false if no change occurred.</returns>
        public bool SetPackageReferenceVersion( IActivityMonitor monitor,
                                                string packageId,
                                                SVersion version,
                                                bool ignoreCurrentBound = true )
        {
            var idx = _deps.FindIndex( dep => dep.Name == packageId );
            if( idx < 0 )
            {
                monitor.Warn( $"Dependency '{packageId}' not found." );
                return false;
            }
            NodeProjectDependency dOrig = _deps[idx];
            var newOne = new SVersionBound( version );
            bool inCurrentBound = dOrig.Version.Contains( newOne );
            if( inCurrentBound )
            {
                if( dOrig.Version == newOne )
                {
                    return false;
                }
            }
            else
            {
                if( !ignoreCurrentBound )
                {
                    monitor.Warn( $"Dependency '{packageId}' current version is '{dOrig.Version}'. Cannot set non satisfying version '{version}'." );
                    return false;
                }
                monitor.Warn( $"Dependency '{packageId}' current version is '{dOrig.Version}'. New version '{version}' overrides its bound." );
            }
            NodeProjectDependency d;
            if( dOrig.Type == NodeProjectDependencyType.LocalFeedTarball )
            {
                d = NodeProjectDependency.CreateLocalFeedTarballFromRawDep( dOrig.RawDep, dOrig.Name, dOrig.Kind, version );
            }
            else
            {
                d = NodeProjectDependency.CreateFromVersion( dOrig.Name, dOrig.Kind, version );
            }
            var s = _o.Property( d.Kind.ToPackageJsonKey() );
            Debug.Assert( s != null );
            s[d.Name] = d.RawDep;
            monitor.Info( $"Updated '{dOrig}' to version '{d.RawDep}'." );
            _deps[idx] = d;
            _isDirty = true;
            _project.SetDirty( true );
            return true;
        }

        /// <summary>
        /// Overridden to return '<see cref="SafeName"/>' or '<see cref="SafeName"/> (private)'.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => IsPrivate ? $"{SafeName} (private)" : SafeName;

        internal bool Initialize( IActivityMonitor m )
        {
            return FillDeps( m, ArtifactDependencyKind.Private )
                   && FillDeps( m, ArtifactDependencyKind.Development )
                   && FillDeps( m, ArtifactDependencyKind.Transitive );

            bool FillDeps( IActivityMonitor m, ArtifactDependencyKind depKind )
            {
                var depNameKind = depKind.ToPackageJsonKey();
                var oDeps = _o[depNameKind];
                if( oDeps != null )
                {
                    foreach( var d in oDeps )
                    {
                        string? rawDep;
                        if( d is not JProperty dP
                            || String.IsNullOrWhiteSpace( dP.Name )
                            || dP.Value.Type != JTokenType.String
                            || string.IsNullOrWhiteSpace( rawDep = (string?)dP.Value ) )
                        {
                            m.Error( $"Invalid dependency in {depNameKind}: {d}." );
                            return false;
                        }
                        var (v, type) = GetVersionType( m, depNameKind, dP.Name, rawDep );
                        if( type == NodeProjectDependencyType.None ) return false;
                        _deps.Add( new NodeProjectDependency( dP.Name, type, depKind, rawDep, v ) );
                    }
                }
                return true;

                static (SVersionBound, NodeProjectDependencyType) GetVersionType( IActivityMonitor m, string depNameKind, string name, string value )
                {
                    if( value.StartsWith( "file:" ) )
                    {
                        string path;
                        if( value.EndsWith( ".tgz" ) )
                        {
                            path = value.Substring( 5 );
                            path = Path.GetFileNameWithoutExtension( path );
                            // Remove the package name.
                            path = path.Remove( 0, name.Length );
                            var r = SVersionBound.NpmTryParse( path );
                            if( !r.IsValid )
                            {
                                m.Error( $"Error while parsing version of a local feed package: {r.Error}" );
                                return (SVersionBound.None, NodeProjectDependencyType.None);
                            }
                            return (r.Result, NodeProjectDependencyType.LocalFeedTarball);
                        }
                        if( value.StartsWith( "file:.." ) )
                        {
                            return (SVersionBound.None, NodeProjectDependencyType.LocalPath);
                        }
                        m.Error( $"Dependency '{value}' for {depNameKind}/{name} must be relative and starts with 'file:..'." );
                        return (SVersionBound.None, NodeProjectDependencyType.None);
                    }
                    if( value.StartsWith( "workspace:" ) )
                    {
                        return (SVersionBound.None, NodeProjectDependencyType.Workspace);
                    }
                    if( value.StartsWith( "portal:" ) )
                    {
                        return (SVersionBound.None, NodeProjectDependencyType.Portal);
                    }
                    else if( value.IndexOf( "://" ) > 0 )
                    {
                        if( value.StartsWith( "http://" ) || value.StartsWith( "https://" ) )
                        {
                            return (SVersionBound.None, NodeProjectDependencyType.UrlTar);
                        }
                        if( value.StartsWith( "git://" )
                            || value.StartsWith( "git+ssh://" )
                            || value.StartsWith( "git+http://" )
                            || value.StartsWith( "git+https://" )
                            || value.StartsWith( "git+file://" ) )
                        {
                            return (SVersionBound.None, NodeProjectDependencyType.UrlGit);
                        }
                        m.Error( $"Unable to handle what seems to be a url dependency '{value}' for {depNameKind}/{name}." );
                        return (SVersionBound.None, NodeProjectDependencyType.None);
                    }
                    var vR = SVersionBound.NpmTryParse( value );
                    if( vR.IsValid )
                    {
                        return (vR.Result, NodeProjectDependencyType.VersionBound);
                    }
                    if( Regex.IsMatch( value, "\\w+/\\w+" ) ) return (SVersionBound.None, NodeProjectDependencyType.GitHub);
                    if( Regex.IsMatch( value, "\\w+" ) ) return (SVersionBound.None, NodeProjectDependencyType.Tag);
                    m.Error( $"Invalid version for {depNameKind}/{name} '{value}': {vR.Error}" );
                    return (SVersionBound.None, NodeProjectDependencyType.None);
                }
            }
        }


    }
}


