using CK.Core;
using CK.Packaging.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CKli.Publish.Plugin;

/// <summary>
/// Mutable set of <see cref="PublishedProfile"/> stored as Json files in a folder and identified
/// by their <see cref="PublishedProfile.Version"/>.
/// <para>
/// A profile is stored in a "v{Version}.json" file at its <see cref="PublishedProfile.GetProfilePath"/>:
/// in the <see cref="RootPath"/> for stable versions (and their CI builds) and in a
/// <see cref="SVersion.BranchName"/> subordinated folder for prereleases ("alpha" to "zulu") and
/// exploratory versions ("explo/{name}").
/// </para>
/// <para>
/// Files are lazily read and any modification is in-memory only until <see cref="Save"/> is called.
/// </para>
/// <para>
/// <see cref="Save"/> also refreshes the <see cref="IndexFileName"/> file at the <see cref="RootPath"/>:
/// a projection of the profile files for whoever reads this folder from the outside. This folder never
/// reads it back - it is a reflection, never a source.
/// </para>
/// </summary>
public sealed partial class PublishedFolder
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
        PublishedProfile? _saved;
        // The exception when read if any (_saved is obviously null).
        Exception? _loadError;
        // Current profile (initially _saved).
        PublishedProfile? _current;

        FileCacheInfo( SVersion version,
                       string jsonFilePath,
                       PublishedProfile? saved,
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

        public PublishedProfile? Current { get => _current; set => _current = value; }

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
                var profile = PublishedProfile.Parse( File.ReadAllBytes( jsonFilePath ) );
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
                throw new ArgumentException( $"Published folder directory must exist: '{rootPath}'.", nameof( rootPath ) );
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
        // RootPath ends with the directory separator: it is the prefix.
        return PublishedProfile.GetProfilePath( version, _rootPath, ".json", Path.DirectorySeparatorChar );
    }

    /// <summary>
    /// Creates a free, time based version for a new profile: the <see cref="SVersion.Major"/> is the year,
    /// the <see cref="SVersion.Minor"/> is the day in the year and the <see cref="SVersion.Patch"/> starts at
    /// 0 and is incremented until no profile of this folder uses the resulting version.
    /// <para>
    /// The <paramref name="branchKind"/>, <paramref name="exploratoryName"/> and <paramref name="isCIBuild"/>
    /// describe the build this profile comes from: they place the version - and hence its file - on the branch
    /// that produced it.
    /// </para>
    /// <para>
    /// A version whose file exists but cannot be read (see <see cref="GetLoadError(SVersion)"/>) counts as used:
    /// <see cref="Save"/> would replace that file.
    /// </para>
    /// </summary>
    /// <param name="branchKind">
    /// The kind of the built branch: <see cref="CSVersionKind.Stable"/>, <see cref="CSVersionKind.Exploratory"/>
    /// or one of the <see cref="CSVersionKind.Alpha"/> to <see cref="CSVersionKind.Zulu"/> prereleases.
    /// </param>
    /// <param name="exploratoryName">
    /// The exploratory name. Required when <paramref name="branchKind"/> is
    /// <see cref="CSVersionKind.Exploratory"/>, ignored otherwise.
    /// </param>
    /// <param name="isCIBuild">True when the build is a CI build: the version is a CI one.</param>
    /// <param name="utcNow">Optional time to use instead of <see cref="DateTime.UtcNow"/>.</param>
    /// <returns>A Conformant SVersion that no profile of this folder uses.</returns>
    public SVersion CreateNewProfileVersion( CSVersionKind branchKind,
                                             ReadOnlySpan<char> exploratoryName = default,
                                             bool isCIBuild = false,
                                             DateTime? utcNow = null )
    {
        var now = utcNow ?? DateTime.UtcNow;
        var v = SVersion.Create( now.Year, now.DayOfYear, 0, mustBeCSVersion: true );
        if( branchKind is CSVersionKind.Exploratory )
        {
            if( exploratoryName.Length == 0 )
            {
                throw new ArgumentException( "An exploratory branch requires its name.", nameof( exploratoryName ) );
            }
            v = v.SetExploratoryName( new string( exploratoryName ) );
        }
        else if( branchKind is >= CSVersionKind.Alpha and <= CSVersionKind.Zulu )
        {
            v = v.SetBranchName( branchKind );
        }
        else if( branchKind is not CSVersionKind.Stable )
        {
            throw new ArgumentException( $"Invalid branch kind '{branchKind}'.", nameof( branchKind ) );
        }
        if( isCIBuild )
        {
            // These numbers are minted, they don't follow a released version: unlike a regular CI build, the
            // "--ci" form must not shift the Patch number that the conflict resolution below owns.
            v = v.SetCINumber( 0, impactStablePatchNumber: false );
        }
        return FindFreePatch( v );
    }

    /// <summary>
    /// Creates the version of the profile that supersedes <paramref name="origin"/>: the same
    /// <see cref="SVersion.Major"/>, <see cref="SVersion.Minor"/> and branch - as if it had been built the
    /// same day - with the next free <see cref="SVersion.Patch"/>.
    /// </summary>
    /// <param name="origin">The version of the profile being superseded. Must be a Conformant SVersion.</param>
    /// <returns>A Conformant SVersion that no profile of this folder uses.</returns>
    public SVersion CreateSupersedingProfileVersion( SVersion origin )
    {
        ArgumentNullException.ThrowIfNull( origin );
        if( origin.BranchName == null )
        {
            throw new ArgumentException( $"Version '{origin}' must be a Conformant SVersion.", nameof( origin ) );
        }
        return FindFreePatch( origin.SetVersionNumbers( origin.Major, origin.Minor, origin.Patch + 1 ) );
    }

    // Increments the Patch until the version is free. A file that exists but cannot be read counts as
    // used: Save would replace it.
    SVersion FindFreePatch( SVersion v )
    {
        for(; ; )
        {
            var info = LoadInfo( v );
            if( info.Current == null && info.LoadError == null ) return v;
            v = v.SetVersionNumbers( v.Major, v.Minor, v.Patch + 1 );
        }
    }

    /// <summary>
    /// Gets the profile of a version. The file is read on demand: when this is null,
    /// <see cref="GetLoadError(SVersion)"/> tells whether the file was missing or unreadable.
    /// </summary>
    /// <param name="version">The version to find.</param>
    /// <returns>The profile or null.</returns>
    public PublishedProfile? Find( SVersion version ) => LoadInfo( version ).Current;

    /// <summary>
    /// Gets the error that occurred while reading the file of a version. The file is read on demand.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>The error or null.</returns>
    public Exception? GetLoadError( SVersion version ) => LoadInfo( version ).LoadError;

    /// <summary>
    /// Gets all the profiles, ordered by descending
    /// <see cref="PublishedProfile.Version"/> (the latest first).
    /// All the files are read.
    /// </summary>
    public IEnumerable<PublishedProfile> Profiles
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
    /// <see cref="PublishedProfile.Version"/> already exists.
    /// <para>
    /// An existing file that cannot be read (see <see cref="GetLoadError(SVersion)"/>) doesn't prevent
    /// the add: <see cref="Save"/> replaces the invalid file.
    /// </para>
    /// </summary>
    /// <param name="profile">The profile to add.</param>
    public void Add( PublishedProfile profile )
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
    /// Removes every profile that offers the provided package: the counterpart of
    /// <see cref="OnDeprecatedPackage(string, SVersion)"/> for a deprecation that has expired. All the
    /// files are read.
    /// <para>
    /// An expired deprecation removes the version tag and unlists the packages from the feeds: a profile
    /// that offers one of them describes something that no longer exists, so it is deleted rather than
    /// deprecated.
    /// </para>
    /// </summary>
    /// <param name="packageId">The expired package identifier.</param>
    /// <param name="version">The expired package version.</param>
    /// <returns>True if at least one profile has been removed. False otherwise.</returns>
    public bool OnExpiredPackage( string packageId, SVersion version )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( packageId );
        ArgumentNullException.ThrowIfNull( version );
        bool found = false;
        LoadAll();
        // Only Current is set to null here: this doesn't touch the _profiles dictionary, so iterating
        // its values while removing is safe.
        foreach( var info in _profiles.Values )
        {
            var p = info.Current;
            if( p != null && p.Packages.TryGetValue( packageId, out var offered ) && offered.Version == version )
            {
                info.Current = null;
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// Adds a superseding profile for every profile that offers one of the fixed packages: the same offer
    /// with the fixed versions replaced, at a <see cref="CreateSupersedingProfileVersion"/> version. All the
    /// files are read.
    /// <para>
    /// The superseded profiles are left untouched: a profile records what was published, and a fix does not
    /// change the past. A <see cref="PublishedProfile.IsDeprecated"/> profile is <c>locked</c> and gets no
    /// successor at all.
    /// </para>
    /// <para>
    /// This is idempotent: an offer that some profile already carries is not created a second time, so
    /// retrying an interrupted fix publication adds nothing.
    /// </para>
    /// </summary>
    /// <param name="fixedPackages">
    /// Maps each fixed <see cref="PackageInstance"/> - the package identifier in the version that was fixed -
    /// to the version that now supersedes it.
    /// </param>
    /// <returns>The created profiles, in ascending version order. Empty when nothing was superseded.</returns>
    public ImmutableArray<PublishedProfile> OnFixedPackages( IReadOnlyDictionary<PackageInstance, SVersion> fixedPackages )
    {
        ArgumentNullException.ThrowIfNull( fixedPackages );
        if( fixedPackages.Count == 0 ) return ImmutableArray<PublishedProfile>.Empty;
        LoadAll();
        // Snapshotting is required twice over: Add mutates the dictionary being enumerated, and a profile
        // created here already offers the fixed versions - it must not be superseded in its turn.
        var origins = _profiles.Values.Where( i => i.Current != null && !i.Current.IsDeprecated )
                                      .Select( i => i.Current! )
                                      .OrderBy( p => p.Version )
                                      .ToList();
        var offers = _profiles.Values.Where( i => i.Current != null )
                                     .Select( i => i.Current! )
                                     .ToList();
        var created = ImmutableArray.CreateBuilder<PublishedProfile>();
        foreach( var origin in origins )
        {
            var updated = CreateSupersedingProfile( origin, fixedPackages );
            if( updated == null || offers.Any( o => SameOffer( o, updated ) ) ) continue;
            Add( updated );
            offers.Add( updated );
            created.Add( updated );
        }
        return created.DrainToImmutable();

        // Two profiles carry the same offer when they offer the same versions of the same packages: the
        // superseding profile differs from its origin by nothing else, so this is what makes a retry a no-op.
        static bool SameOffer( PublishedProfile p1, PublishedProfile p2 )
        {
            if( p1.Packages.Count != p2.Packages.Count ) return false;
            foreach( var (packageId, instance) in p1.Packages )
            {
                if( !p2.Packages.TryGetValue( packageId, out var other ) || other.Version != instance.Version )
                {
                    return false;
                }
            }
            return true;
        }
    }

    // Returns null when the profile offers none of the fixed packages: only the repositories and the
    // packages that actually change are rebuilt, the others are shared with the origin.
    PublishedProfile? CreateSupersedingProfile( PublishedProfile origin, IReadOnlyDictionary<PackageInstance, SVersion> fixedPackages )
    {
        ImmutableArray<Repository>.Builder? repositories = null;
        for( int i = 0; i < origin.Repositories.Length; ++i )
        {
            var r = origin.Repositories[i];
            ImmutableArray<PackageInstance>.Builder? packages = null;
            for( int j = 0; j < r.Packages.Length; ++j )
            {
                if( fixedPackages.TryGetValue( r.Packages[j], out var superseding ) )
                {
                    packages ??= r.Packages.ToBuilder();
                    packages[j] = new PackageInstance( r.Packages[j].PackageId, superseding );
                }
            }
            if( packages != null )
            {
                repositories ??= origin.Repositories.ToBuilder();
                repositories[i] = new Repository( r.Key, packages.DrainToImmutable() );
            }
        }
        return repositories == null
                ? null
                : new PublishedProfile( origin.StackUrl,
                                        origin.World,
                                        CreateSupersedingProfileVersion( origin.Version ),
                                        repositories.DrainToImmutable() );
    }

    /// <summary>
    /// Writes the added and updated profiles, deletes the files of the removed ones and, when at least one
    /// of them changed, refreshes the <see cref="IndexFileName"/> file (see <see cref="CreateIndexUtf8Bytes"/>).
    /// </summary>
    /// <returns>The number of created, updated or deleted profile files. The index doesn't count.</returns>
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
        // The index reflects the profile files: it is written last, and only when they moved.
        if( count > 0 ) WriteIndex();
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
            // This is also what excludes the IndexFileName: "index" is not a version, so the index this
            // folder writes can never be read back as one of its own profiles.
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
