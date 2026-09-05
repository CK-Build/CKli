using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CKli.Publish.Plugin;

// CK.Packaging.Abstractions.PublishedProfile is spelled out here: this namespace declares its own
// PublishedProfile (what a publication offers, built by PublishedProfileBuilder), which hides it.
// A using alias cannot fix that - a namespace member conflicting with an alias is CS0576.
/// <summary>
/// Mutable set of <see cref="CK.Packaging.Abstractions.PublishedProfile"/> stored as Json files in a
/// folder and identified by their <see cref="CK.Packaging.Abstractions.PublishedProfile.Version"/>.
/// <para>
/// A profile is stored in a "v{Version}.json" file that is in the <see cref="RootPath"/> for stable
/// versions (and their CI builds) and in a <see cref="SVersion.BranchName"/> subordinated folder for
/// prereleases ("alpha" to "zulu") and exploratory versions ("explo/{name}").
/// </para>
/// <para>
/// Files are lazily read and any modification is in-memory only until <see cref="Save"/> is called.
/// </para>
/// </summary>
public sealed class PublishedFolder
{
    readonly string _rootPath;
    readonly Dictionary<SVersion, FileCacheInfo> _profiles;
    bool _allLoaded;

    sealed class FileCacheInfo
    {
        // The version is the key.
        readonly SVersion _version;
        // The full path to the json profile file (GetProfileFilePath method).
        readonly string _jsonFilePath;
        // The profile that is currently in the file. Null when the file doesn't exist
        // or cannot be read (_loadError is then not null).
        CK.Packaging.Abstractions.PublishedProfile? _saved;
        // The exception when read if any (_saved is obviously null).
        Exception? _loadError;
        // Current profile (initially _saved).
        CK.Packaging.Abstractions.PublishedProfile? _current;

        FileCacheInfo( SVersion version,
                       string jsonFilePath,
                       CK.Packaging.Abstractions.PublishedProfile? saved,
                       Exception? loadError )
        {
            _version = version;
            _jsonFilePath = jsonFilePath;
            _saved = saved;
            _loadError = loadError;
            _current = saved;
        }

        public SVersion Version => _version;

        public string JsonFilePath => _jsonFilePath;

        public Exception? LoadError => _loadError;

        public CK.Packaging.Abstractions.PublishedProfile? Current { get => _current; set => _current = value; }

        /// <summary>
        /// Gets whether the file must be written (or deleted when <see cref="Current"/> is null).
        /// </summary>
        public bool IsDirty => !ReferenceEquals( _saved, _current );

        internal static FileCacheInfo NoFile( SVersion version, string jsonFilePath )
        {
            return new FileCacheInfo( version, jsonFilePath, null, null );
        }

        internal static FileCacheInfo Read( SVersion version, string jsonFilePath )
        {
            try
            {
                var profile = CK.Packaging.Abstractions.PublishedProfile.Parse( File.ReadAllBytes( jsonFilePath ) );
                if( profile.Version != version )
                {
                    throw new JsonException( $"File '{jsonFilePath}' contains the version '{profile.Version}'." );
                }
                return new FileCacheInfo( version, jsonFilePath, profile, null );
            }
            catch( Exception ex )
            {
                return new FileCacheInfo( version, jsonFilePath, null, ex );
            }
        }

        internal void OnSaved()
        {
            _saved = _current;
            _loadError = null;
        }
    }

    /// <summary>
    /// Initializes a new folder.
    /// </summary>
    /// <param name="rootPath">The directory that contains the profile files.</param>
    /// <param name="createIfMissing">True to create the directory when it doesn't exist.</param>
    public PublishedFolder( string rootPath, bool createIfMissing = false )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( rootPath );
        rootPath = Path.GetFullPath( rootPath );
        if( !Directory.Exists( rootPath ) )
        {
            if( !createIfMissing )
            {
                throw new ArgumentException( $"Published folder directory must exist: '{rootPath}'.",
                                             nameof( rootPath ) );
            }
            Directory.CreateDirectory( rootPath );
        }
        // Path.GetFullPath normalized the separators: only a root path (like "C:\") can
        // already end with the directory separator.
        _rootPath = Path.EndsInDirectorySeparator( rootPath )
                        ? rootPath
                        : rootPath + Path.DirectorySeparatorChar;
        _profiles = new Dictionary<SVersion, FileCacheInfo>();
    }

    /// <summary>
    /// Gets the root path. Ends with a <see cref="Path.DirectorySeparatorChar"/>.
    /// </summary>
    public string RootPath => _rootPath;

    /// <summary>
    /// Gets whether at least one profile has been added, removed or updated
    /// since the last <see cref="Save"/>.
    /// </summary>
    public bool IsDirty => _profiles.Values.Any( i => i.IsDirty );

    /// <summary>
    /// Gets the full path of the file that contains (or would contain) the profile of a version.
    /// </summary>
    /// <param name="version">The version. Must be a Conformant SVersion.</param>
    /// <returns>The full path of the Json file.</returns>
    public string GetProfileFilePath( SVersion version )
    {
        ArgumentNullException.ThrowIfNull( version );
        var branchName = version.BranchName;
        if( branchName == null )
        {
            throw new ArgumentException( $"Version '{version}' must be a Conformant SVersion.", nameof( version ) );
        }
        // BranchName is the empty string for stable versions (and their CI builds) and can
        // contain a '/' for exploratory versions ("explo/{name}").
        return branchName.Length == 0
                ? $"{_rootPath}v{version}.json"
                : $"{_rootPath}{branchName.Replace( '/', Path.DirectorySeparatorChar )}"
                  + $"{Path.DirectorySeparatorChar}v{version}.json";
    }

    /// <summary>
    /// Gets the profile of a version. The file is read on demand: when this is null,
    /// <see cref="GetLoadError(SVersion)"/> tells whether the file was missing or unreadable.
    /// </summary>
    /// <param name="version">The version to find.</param>
    /// <returns>The profile or null.</returns>
    public CK.Packaging.Abstractions.PublishedProfile? Find( SVersion version ) => LoadInfo( version ).Current;

    /// <summary>
    /// Gets the error that occurred while reading the file of a version. The file is read on demand.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>The error or null.</returns>
    public Exception? GetLoadError( SVersion version ) => LoadInfo( version ).LoadError;

    /// <summary>
    /// Gets all the profiles, ordered by descending
    /// <see cref="CK.Packaging.Abstractions.PublishedProfile.Version"/> (the latest first).
    /// All the files are read.
    /// </summary>
    public IEnumerable<CK.Packaging.Abstractions.PublishedProfile> Profiles
    {
        get
        {
            LoadAll();
            return _profiles.Values.Where( i => i.Current != null )
                                   .OrderByDescending( i => i.Version )
                                   .Select( i => i.Current! );
        }
    }

    /// <summary>
    /// Gets the files that cannot be read, ordered by descending version. All the files are read.
    /// </summary>
    public IEnumerable<(SVersion Version, Exception Error)> LoadErrors
    {
        get
        {
            LoadAll();
            return _profiles.Values.Where( i => i.LoadError != null )
                                   .OrderByDescending( i => i.Version )
                                   .Select( i => (i.Version, i.LoadError!) );
        }
    }

    /// <summary>
    /// Adds a profile. Throw if a profile with the same
    /// <see cref="CK.Packaging.Abstractions.PublishedProfile.Version"/> already exists.
    /// <para>
    /// An existing file that cannot be read (see <see cref="GetLoadError(SVersion)"/>) doesn't prevent
    /// the add: <see cref="Save"/> replaces the invalid file.
    /// </para>
    /// </summary>
    /// <param name="profile">The profile to add.</param>
    public void Add( CK.Packaging.Abstractions.PublishedProfile profile )
    {
        ArgumentNullException.ThrowIfNull( profile );
        var info = LoadInfo( profile.Version );
        if( info.Current != null )
        {
            throw new InvalidOperationException( $"Profile '{profile.Version}' already exists." );
        }
        info.Current = profile;
    }

    /// <summary>
    /// Removes a profile.
    /// </summary>
    /// <param name="version">The profile's version to remove.</param>
    /// <returns>True if the profile has been found and removed, false otherwise.</returns>
    public bool Remove( SVersion version )
    {
        var info = LoadInfo( version );
        if( info.Current == null ) return false;
        info.Current = null;
        return true;
    }

    /// <summary>
    /// Deprecates a profile. This is idempotent and doesn't require the profile to exist.
    /// </summary>
    /// <param name="version">The version to deprecate.</param>
    /// <returns>True if the profile has been found and actually deprecated. False otherwise.</returns>
    public bool Deprecate( SVersion version )
    {
        var info = LoadInfo( version );
        var p = info.Current;
        if( p == null || p.IsDeprecated ) return false;
        info.Current = p.Deprecate();
        return true;
    }

    /// <summary>
    /// Deprecates all the profiles that contains the provided package. All the files are read.
    /// </summary>
    /// <param name="packageId">The deprecated package identifier.</param>
    /// <param name="version">The deprecated package version.</param>
    /// <returns>True if at least one profile has been deprecated. False otherwise.</returns>
    public bool OnDeprecatedPackage( string packageId, SVersion version )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( packageId );
        ArgumentNullException.ThrowIfNull( version );
        bool found = false;
        LoadAll();
        foreach( var info in _profiles.Values )
        {
            var p = info.Current;
            if( p != null )
            {
                var n = p.OnDeprecatedPackage( packageId, version );
                if( p != n )
                {
                    info.Current = n;
                    found = true;
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Writes the added and updated profiles and deletes the files of the removed ones.
    /// </summary>
    /// <returns>The number of created, updated or deleted files.</returns>
    public int Save()
    {
        int count = 0;
        foreach( var info in _profiles.Values )
        {
            if( !info.IsDirty ) continue;
            var p = info.Current;
            if( p == null )
            {
                File.Delete( info.JsonFilePath );
            }
            else
            {
                var dirPath = Path.GetDirectoryName( info.JsonFilePath );
                if( dirPath != null ) Directory.CreateDirectory( dirPath );
                File.WriteAllBytes( info.JsonFilePath, p.ToUtf8Bytes() );
            }
            info.OnSaved();
            ++count;
        }
        return count;
    }

    /// <summary>
    /// Clears the cache: any pending modification is lost and the files are read again on demand.
    /// </summary>
    public void Reload()
    {
        _profiles.Clear();
        _allLoaded = false;
    }

    FileCacheInfo LoadInfo( SVersion version )
    {
        if( _profiles.TryGetValue( version, out var info ) )
        {
            return info;
        }
        var path = GetProfileFilePath( version );
        info = File.Exists( path )
                ? FileCacheInfo.Read( version, path )
                : FileCacheInfo.NoFile( version, path );
        _profiles.Add( version, info );
        return info;
    }

    void LoadAll()
    {
        if( _allLoaded ) return;
        foreach( var f in Directory.EnumerateFiles( _rootPath, "*.json", SearchOption.AllDirectories ) )
        {
            var fName = Path.GetFileNameWithoutExtension( f.AsSpan() );
            // The whole file name must be the version: SVersion.TryMatch handles the
            // optional leading 'v' and forwards the head on success.
            if( !SVersion.TryMatch( ref fName, out var version, mustBeCSVersion: true ) || fName.Length != 0 )
            {
                continue;
            }
            // Skip any duplicate. Already loaded, bad +metadata...
            if( _profiles.ContainsKey( version ) ) continue;
            // Skip any file that is not at its canonical path: a file in the wrong branch folder
            // is not a profile of this folder.
            var path = GetProfileFilePath( version );
            if( !path.Equals( f, StringComparison.OrdinalIgnoreCase ) ) continue;
            _profiles.Add( version, FileCacheInfo.Read( version, path ) );
        }
        _allLoaded = true;
    }
}
