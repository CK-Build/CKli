using CK.Core;
using CK.Env.Plugins;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    /// <summary>
    /// Specialized <see cref="JsonFileBase"/> for package.json file.
    /// </summary>
    public class PackageJsonFile : JsonFileBase
    {
        readonly List<NPMDep> _deps;

        internal PackageJsonFile( NPMProject p )
            : base( p.FileSystem, p.FullPath.AppendPart( "package.json" ) )
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
        /// <param name="name">The dependency name that must appear in <see cref="Dependencies"/>.</param>
        /// <param name="v">The minimal version. Must not be null.</param>
        public void SetDependencyMinVersion( string name, SVersion v )
        {
            var idx = _deps.FindIndex( dep => dep.Name == name );
            if( idx < 0 ) throw new ArgumentException( $"Dependency '{name}' not found.", nameof( name ) );
            var dOrig = _deps[idx];
            var d = new NPMDep( dOrig.Name, dOrig.Kind, v );
            Root[d.Kind.ToJsonKey()][d.Name] = d.RawDep;
            _deps[idx] = d;
        }

        /// <summary>
        /// Ensures that a dependency exist with a given minimal version.
        /// </summary>
        /// <param name="name">The dependency name.</param>
        /// <param name="v">The minimal version. Must not be null.</param>
        /// <param name="kind">The kind of the dependency.</param>
        public void EnsureDependency( string name, SVersion v, DependencyKind kind )
        {
            var d = new NPMDep( name, kind, v );
            Root[d.Kind.ToJsonKey()][d.Name] = d.RawDep;
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
            var s = FillDeps( m, DependencyKind.Normal );
            if( s == NPMProjectStatus.Valid ) s = FillDeps( m, DependencyKind.Dev );
            if( s == NPMProjectStatus.Valid ) s = FillDeps( m, DependencyKind.Peer );
            return s;
        }

        NPMProjectStatus FillDeps( IActivityMonitor m, DependencyKind depKind )
        {
            string depNameKind = depKind.ToJsonKey();
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
                    if( type == VersionDependencyType.None ) return NPMProjectStatus.ErrorInvalidDependencyVersion;
                    _deps.Add( new NPMDep( dP.Name, type, depKind, rawDep, v ) );
                }
            }
            return NPMProjectStatus.Valid;
        }

        (SVersion, VersionDependencyType) GetVersionType( IActivityMonitor m, string depNameKind, string name, string value )
        {
            if( value.IndexOf("://") > 0 )
            {
                if( value.StartsWith( "file:" ) )
                {
                    if( !value.StartsWith( "file:.." ) )
                    {
                        m.Error( $"Dependency '{value}' for {depNameKind}/{name} must be relative and starts with 'file:..'." );
                        return (null, VersionDependencyType.None);
                    }
                    else return (null, VersionDependencyType.LocalPath);
                }
                if( value.StartsWith( "http://" ) || value.StartsWith( "https://" ) ) return (null, VersionDependencyType.UrlTar);
                if( value.StartsWith( "git://" )
                    || value.StartsWith( "git+ssh://" )
                    || value.StartsWith( "git+http://" )
                    || value.StartsWith( "git+https://" )
                    || value.StartsWith( "git+file://" ) ) return (null, VersionDependencyType.UrlGit);
                m.Error( $"Unable to handle what seems to be a url dependency '{value}' for {depNameKind}/{name}." );
                return (null, VersionDependencyType.None);
            }
            if( value.Length == 0 || value == "*" ) return (null, VersionDependencyType.AllVersions);
            if( value.IndexOf( " - " ) >= 0
                || value.IndexOf( "||" ) >= 0
                || value.IndexOf( '<' ) >= 0
                || value.IndexOf( '^' ) >= 0
                || value.IndexOf( '~' ) >= 0
                || SVersion.TryParse( value ).IsValid )
            {
                return (null, VersionDependencyType.OtherVersionSpec);
            }
            if( value.StartsWith( ">=" ) )
            {
                var v = SVersion.TryParse( value );
                if( !v.IsValid )
                {
                    m.Error( $"Invalid version for {depNameKind}/{name} '{value}': {v.ErrorMessage}" );
                    return (null, VersionDependencyType.None);
                }
                return (v, VersionDependencyType.MinVersion);
            }
            if( Regex.IsMatch( value, "\\w+/\\w+" ) ) return (null, VersionDependencyType.GitHub);
            if( Regex.IsMatch( value, "\\w+" ) ) return (null, VersionDependencyType.Tag);
            return (null, VersionDependencyType.OtherVersionSpec);
        }
    }
}
