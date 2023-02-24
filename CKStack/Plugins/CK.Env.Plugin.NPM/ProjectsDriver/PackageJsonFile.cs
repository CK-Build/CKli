using CK.Core;
using CK.Build;

using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Specialized <see cref="JsonFileBase"/> for package.json file.
    /// </summary>
    public class PackageJsonFile : JsonFileBase
    {
        readonly List<NPMDep> _deps;
        readonly List<NPMDep> _unsavedDeps;
        internal PackageJsonFile( FileSystem fs, NormalizedPath directoryPath )
            : base( fs, directoryPath.AppendPart( "package.json" ) )
        {
            _deps = new List<NPMDep>();
            _unsavedDeps = new List<NPMDep>();
        }

        /// <summary>
        /// Gets or sets the name.
        /// Can be null: a non-published package does not require a name (nor a <see cref="Version"/>).
        /// </summary>
        public string? Name
        {
            get => (string?)Root?["name"];
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
            get => (bool?)Root?["private"] ?? false;
            set => Root["private"] = value;
        }

        /// <summary>
        /// Gets whether this package is published.
        /// It must not be private and have a name and a version.
        /// </summary>
        [MemberNotNullWhen( true, nameof( Name ), nameof( Version ) )]
        public bool IsPublished => !IsPrivate && Name != null && Version != null;

        /// <summary>
        /// Gets or sets the version.
        /// This should always be the <see cref="SVersion.ZeroVersion"/> except when the actual version is
        /// computed during a build/packaging.
        /// Can be null: a non-published package does not require any version (nor <see cref="Name"/>).
        /// </summary>
        public SVersion? Version
        {
            get
            {
                var v = (string?)Root["version"];
                return v != null ? SVersion.TryParse( v ) : null;
            }
            set => SetNonNullProperty( Root, "version", value?.ToNormalizedString() );
        }

        /// <summary>
        /// Gets the list of dependencies.
        /// </summary>
        public IReadOnlyList<NPMDep> Dependencies => _deps;

        /// <summary>
        /// Sets a minimum version for a dependency that must exist, regardless of its current status.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The dependency name that must appear in <see cref="Dependencies"/>.</param>
        /// <param name="v">The minimal version. Must not be null.</param>
        /// <returns>True if the version has been changed, false it was the same version.</returns>
        internal bool SetDependencyMinVersion( IActivityMonitor m, string name, SVersion v )
        {
            var idx = _deps.FindIndex( dep => dep.Name == name );
            if( idx < 0 ) throw new ArgumentException( $"Dependency '{name}' not found.", nameof( name ) );
            NPMDep dOrig = _deps[idx];
            if( dOrig.MinVersion != v )
            {
                NPMDep d;
                if( dOrig.Type == NPMVersionDependencyType.LocalFeedTarball )
                {
                    d = NPMDep.CreateNPMDepLocalFeedTarballFromRawDep( dOrig.RawDep, dOrig.Name, dOrig.Kind, v );
                }
                else
                {
                    d = NPMDep.CreateNPMDepMinVersion( dOrig.Name, dOrig.Kind, v );
                }
                Root[d.Kind.ToPackageJsonKey()][d.Name] = d.RawDep;
                m.Info( $"Updated {dOrig} to version {d.RawDep}." );
                _unsavedDeps.Add( d );
                _deps[idx] = d;
                return true;
            }
            return false;
        }

        public override bool Save( IActivityMonitor m, bool forceSave = false )
        {
            if( _unsavedDeps.Any() )
            {
                if( NpmInstall( m, _unsavedDeps.ToArray() ) )
                {
                    _unsavedDeps.Clear();
                }
                else
                {
                    m.Error( "Failed to npm install the dependencies. The package-lock.json will be wrong. This need manual fix." );
                }
            }
            return base.Save( m, forceSave );
        }

        bool NpmInstall( IActivityMonitor m, NPMDep[] packages )
        {
            return ProcessRunner.Run(
                m, FileSystem.GetFileInfo( FilePath.RemoveLastPart() ).PhysicalPath, "cmd.exe",
                "/C npm install " + string.Join( ' ', packages.Select( p => p.RawDep ).ToArray() ), 10 * 60 * 1000 );
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
            var s = FillDeps( m, ArtifactDependencyKind.Private );
            if( s == NPMProjectStatus.Valid ) s = FillDeps( m, ArtifactDependencyKind.Development );
            if( s == NPMProjectStatus.Valid ) s = FillDeps( m, ArtifactDependencyKind.Transitive );
            return s;
        }

        NPMProjectStatus FillDeps( IActivityMonitor m, ArtifactDependencyKind depKind )
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

        (SVersion?, NPMVersionDependencyType) GetVersionType( IActivityMonitor m, string depNameKind, string name, string value )
        {
            if( value.StartsWith( "file:" ) )
            {
                string path;
                if( value.EndsWith( ".tgz" ) )
                {
                    path = value.Substring( 5 );
                    path = Path.GetFileNameWithoutExtension( path );
                    path = path.Remove( 0, name.Length ); //remove the package name
                    SVersion version = SVersion.TryParse( path );
                    if( !version.IsValid )
                    {
                        m.Error( $"Error while parsing version of a local feed package" );
                        return (null, NPMVersionDependencyType.LocalFeedTarball);
                    }
                    return (version, NPMVersionDependencyType.LocalFeedTarball);
                }
                if( value.StartsWith( "file:.." ) )
                {
                    return (null, NPMVersionDependencyType.LocalPath);
                }
                m.Error( $"Dependency '{value}' for {depNameKind}/{name} must be relative and starts with 'file:..'." );
                return (null, NPMVersionDependencyType.None);
            }
            if( value.StartsWith( "workspace:" ) )
            {
                return (null, NPMVersionDependencyType.Workspace);
            }
            if( value.StartsWith( "portal:" ) )
            {
                return (null, NPMVersionDependencyType.Portal);
            }
            else if( value.IndexOf( "://" ) > 0 )
            {

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
