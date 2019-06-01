using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.Plugin;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Specialized <see cref="JsonFileBase"/> for package.json file.
    /// </summary>
    public class PackageJsonFile : JsonFileBase
    {
        readonly List<NPMDep> _deps;

        internal PackageJsonFile( NPMProject p )
            : base( p.Driver.GitFolder.FileSystem, p.FullPath.AppendPart( "package.json" ) )
        {
            _deps = new List<NPMDep>();
        }

        /// <summary>
        /// Gets or sets the name.
        /// Can be null: a non-published package does not require a name (nor a <see cref="Version"/>).
        /// </summary>
        public string Name
        {
            get => (string)Root["name"];
            set => SetNonNullProperty( Root, "name", value );
        }

        /// <summary>
        /// Gets the name.
        /// When there is no <see cref="Name"/>, the folder's name is returned: this SafeName always exists.
        /// </summary>
        public string SafeName => Name ?? FilePath.Parts[FilePath.Parts.Count - 2];

        /// <summary>
        /// Gets or sets whether this package is private: even if it has a <see cref="Name"/>
        /// and a <see cref="Version"/>, it must not be published.
        /// </summary>
        public bool IsPrivate
        {
            get => (bool?)Root["private"] ?? false;
            set => Root["private"] = value;
        }

        /// <summary>
        /// Gets whether this package is published.
        /// It must not be private and have a name and a version.
        /// </summary>
        public bool IsPublished => !IsPrivate && Name != null && Version != null;

        /// <summary>
        /// Gets or sets the version.
        /// This should always be the <see cref="SVersion.ZeroVersion"/> except when the actual version is
        /// computed during a build/packaging.
        /// Can be null: a non-published package does not require any version (nor <see cref="Name"/>).
        /// </summary>
        public SVersion Version
        {
            get
            {
                var v = (string)Root["version"];
                return v != null ? SVersion.TryParse( v ) : null;
            }
            set => SetNonNullProperty( Root, "version", value?.ToNuGetPackageString() );
        }

        /// <summary>
        /// Gets the list of dependencies.
        /// </summary>
        public IReadOnlyList<NPMDep> Dependencies => _deps;

        /// <summary>
        /// Sets a minimum version for a dependency that must exist, regardless of it current status.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The dependency name that must appear in <see cref="Dependencies"/>.</param>
        /// <param name="v">The minimal version. Must not be null.</param>
        /// <returns>True if the version has been chenged, false it was the same version.</returns>
        internal bool SetDependencyMinVersion( IActivityMonitor m, string name, SVersion v )
        {
            var idx = _deps.FindIndex( dep => dep.Name == name );
            if( idx < 0 ) throw new ArgumentException( $"Dependency '{name}' not found.", nameof( name ) );
            var dOrig = _deps[idx];
            if( dOrig.MinVersion != v )
            {
                m.Info( $"Updated dependency {dOrig.ToString()} to version {v}." );
                var d = new NPMDep( dOrig.Name, dOrig.Kind, v );
                Root[d.Kind.ToPackageJsonKey()][d.Name] = d.RawDep;
                _deps[idx] = d;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Ensures that a dependency exist with a given minimal version.
        /// </summary>
        /// <param name="name">The dependency name.</param>
        /// <param name="v">The minimal version. Must not be null.</param>
        /// <param name="kind">The kind of the dependency.</param>
        public void EnsureDependency( string name, SVersion v, ProjectDependencyKind kind )
        {
            var d = new NPMDep( name, kind, v );
            Root[d.Kind.ToPackageJsonKey()][d.Name] = d.RawDep;
            var idx = _deps.FindIndex( dep => dep.Name == name );
            if( idx >= 0 ) _deps[idx] = d;
            else _deps.Add( d );
        }

        /// <summary>
        /// Overridden to return the name of the package or the '<see cref="SafeName"/> (unpublished)'
        /// when <see cref="IsPublished"/> is false.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => IsPublished ? Name : $"{SafeName} (unpublished)";

        internal NPMProjectStatus Refresh( IActivityMonitor m )
        {
            _deps.Clear();
            var s = FillDeps( m, ProjectDependencyKind.Private );
            if( s == NPMProjectStatus.Valid ) s = FillDeps( m, ProjectDependencyKind.Development );
            if( s == NPMProjectStatus.Valid ) s = FillDeps( m, ProjectDependencyKind.Transitive );
            return s;
        }

        NPMProjectStatus FillDeps( IActivityMonitor m, ProjectDependencyKind depKind )
        {
            string depNameKind = depKind.ToPackageJsonKey();
            var oDeps = Root[depNameKind];
            if( oDeps != null )
            {
                foreach( var d in oDeps )
                {
                    if( !(d is JProperty dP)
                        || String.IsNullOrWhiteSpace( dP.Name )
                        || dP.Value.Type != JTokenType.String )
                    {
                        m.Error( $"Invalid dependency in {depNameKind}: {d}" );
                        return NPMProjectStatus.ErrorInvalidDependencyRecord;
                    }
                    var rawDep = (string)dP.Value;
                    var (v, type) = GetVersionType( m, depNameKind, dP.Name, rawDep );
                    if( type == NPMVersionDependencyType.None ) return NPMProjectStatus.ErrorInvalidDependencyVersion;
                    _deps.Add( new NPMDep( dP.Name, type, depKind, rawDep, v ) );
                }
            }
            return NPMProjectStatus.Valid;
        }

        (SVersion, NPMVersionDependencyType) GetVersionType( IActivityMonitor m, string depNameKind, string name, string value )
        {
            if( value.IndexOf("://") > 0 )
            {
                if( value.StartsWith( "file:" ) )
                {
                    if( !value.StartsWith( "file:.." ) )
                    {
                        m.Error( $"Dependency '{value}' for {depNameKind}/{name} must be relative and starts with 'file:..'." );
                        return (null, NPMVersionDependencyType.None);
                    }
                    else return (null, NPMVersionDependencyType.LocalPath);
                }
                if( value.StartsWith( "http://" ) || value.StartsWith( "https://" ) ) return (null, NPMVersionDependencyType.UrlTar);
                if( value.StartsWith( "git://" )
                    || value.StartsWith( "git+ssh://" )
                    || value.StartsWith( "git+http://" )
                    || value.StartsWith( "git+https://" )
                    || value.StartsWith( "git+file://" ) ) return (null, NPMVersionDependencyType.UrlGit);
                m.Error( $"Unable to handle what seems to be a url dependency '{value}' for {depNameKind}/{name}." );
                return (null, NPMVersionDependencyType.None);
            }
            if( value.Length == 0 || value == "*" ) return (SVersion.ZeroVersion, NPMVersionDependencyType.AllVersions);
            if( value.IndexOf( " - " ) >= 0
                || value.IndexOf( "||" ) >= 0
                || value.IndexOf( '<' ) >= 0 )
            {
                return (null, NPMVersionDependencyType.OtherVersionSpec);
            }
            string cleaned = value;
            bool mustBeVersion = false;
            if( cleaned.StartsWith( ">=" ) )
            {
                cleaned = cleaned.Substring( 2 );
                mustBeVersion = true;
            }
            else if( cleaned[0] == '^' )
            {
                cleaned = cleaned.Substring( 1 );
                mustBeVersion = true;
            }
            else if( cleaned[0] == '~' )
            {
                cleaned = cleaned.Substring( 1 );
                mustBeVersion = true;
            }
            var v = SVersion.TryParse( cleaned );
            if( v.IsValid )
            {
                return (v, NPMVersionDependencyType.MinVersion);
            }
            if( mustBeVersion )
            {
                m.Error( $"Invalid version for {depNameKind}/{name} '{value}': {v.ErrorMessage}" );
                return (null, NPMVersionDependencyType.None);
            }
            if( Regex.IsMatch( value, "\\w+/\\w+" ) ) return (null, NPMVersionDependencyType.GitHub);
            if( Regex.IsMatch( value, "\\w+" ) ) return (null, NPMVersionDependencyType.Tag);
            return (null, NPMVersionDependencyType.OtherVersionSpec);
        }
    }
}
