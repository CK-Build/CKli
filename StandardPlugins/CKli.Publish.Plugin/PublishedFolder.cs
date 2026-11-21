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
/// <see cref="TimeVersion.BranchName"/> subordinated folder for prereleases ("alpha" to "zulu") and
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
/// <para>
/// The profiles published before the <see cref="TimeVersion"/> are in "v{Year.DayOfYear.Number}.json" files:
/// they are read as their <see cref="TimeVersion"/> (2026.325.1 is 2026.11.21.1) and <see cref="Save"/> moves
/// them to their "v{Year.Month.Day}.json" (or "v{Year.Month.Day.ClashNumber}.json") file.
/// </para>
/// </summary>
public sealed partial class PublishedFolder
{
    readonly string _rootPath;
    readonly Dictionary<TimeVersion, FileCacheInfo> _profiles;
    bool _allLoaded;

    sealed class FileCacheInfo
    {
        // The version is the key.
        readonly TimeVersion _version;
        // The full path to the json profile file (GetProfileFilePath method).
        readonly string _jsonFilePath;
        // The "v{Year.DayOfYear.Number}.json" file this profile has been read from: Save moves it
        // to the _jsonFilePath. Null when the profile is (or will be) in its _jsonFilePath.
        string? _legacyFilePath;
        // The profile that is currently in the file. Null when the file doesn't exist
        // or cannot be read (_loadError is then not null).
        PublishedProfile? _saved;
        // The exception when read if any (_saved is obviously null).
        Exception? _loadError;
        // Current profile (initially _saved).
        PublishedProfile? _current;

        FileCacheInfo( TimeVersion version,
                       string jsonFilePath,
                       string? legacyFilePath,
                       PublishedProfile? saved,
                       Exception? loadError )
        {
            _version = version;
            _jsonFilePath = jsonFilePath;
            _legacyFilePath = legacyFilePath;
            _saved = saved;
            _loadError = loadError;
            _current = saved;
        }

        public TimeVersion Version => _version;

        public string JsonFilePath => _jsonFilePath;

        public string? LegacyFilePath => _legacyFilePath;

        public Exception? LoadError => _loadError;

        public PublishedProfile? Current { get => _current; set => _current = value; }

        /// <summary>
        /// Gets whether the file must be written (or deleted when <see cref="Current"/> is null).
        /// </summary>
        public bool IsDirty => !ReferenceEquals( _saved, _current );

        /// <summary>
        /// Gets whether this <see cref="Version"/> is used: a profile is in the file, in memory, or the file
        /// exists but cannot be read. False for a version that has only been probed (<see cref="NoFile"/>)
        /// and true for one that has been removed but whose number has been minted.
        /// </summary>
        public bool IsUsed => _saved != null || _current != null || _loadError != null;

        internal static FileCacheInfo NoFile( TimeVersion version, string jsonFilePath )
        {
            return new FileCacheInfo( version, jsonFilePath, null, null, null );
        }

        // Reads the jsonFilePath, or the legacyFilePath when it is not null.
        internal static FileCacheInfo Read( TimeVersion version, string jsonFilePath, string? legacyFilePath )
        {
            var path = legacyFilePath ?? jsonFilePath;
            try
            {
                // A legacy profile file contains its legacy version: reading it converts it.
                var profile = PublishedProfile.Parse( File.ReadAllBytes( path ) );
                if( profile.Version != version )
                {
                    throw new JsonException( $"File '{path}' contains the version '{profile.Version}'." );
                }
                return new FileCacheInfo( version, jsonFilePath, legacyFilePath, profile, null );
            }
            catch( Exception ex )
            {
                return new FileCacheInfo( version, jsonFilePath, legacyFilePath, null, ex );
            }
        }

        internal void OnSaved()
        {
            _saved = _current;
            _loadError = null;
            _legacyFilePath = null;
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
        _profiles = new Dictionary<TimeVersion, FileCacheInfo>();
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
    /// <param name="version">The version.</param>
    /// <returns>The full path of the Json file.</returns>
    public string GetProfileFilePath( TimeVersion version )
    {
        // RootPath ends with the directory separator: it is the prefix.
        return PublishedProfile.GetProfilePath( version, _rootPath, ".json", Path.DirectorySeparatorChar );
    }

    // The "v{Year.DayOfYear.Number}.json" file of a version: the profile files written before the TimeVersion.
    string GetLegacyProfileFilePath( TimeVersion version )
    {
        return PublishedProfile.GetLegacyProfilePath( version, _rootPath, ".json", Path.DirectorySeparatorChar );
    }

    /// <summary>
    /// Creates a free, time based version for a new profile: the <see cref="TimeVersion.Date"/> is the
    /// (utc) day and the <see cref="TimeVersion.ClashNumber"/> starts at 0 (no fourth part) and is incremented
    /// until no profile of this folder uses the resulting version.
    /// <para>
    /// The <paramref name="branchKind"/>, <paramref name="exploratoryName"/> and <paramref name="isCIBuild"/>
    /// describe the build this profile comes from: they place the version - and hence its file - on the branch
    /// that produced it.
    /// </para>
    /// <para>
    /// A version whose file exists but cannot be read (see <see cref="GetLoadError(TimeVersion)"/>) counts as used:
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
    /// <returns>A version that no profile of this folder uses.</returns>
    public TimeVersion CreateNewProfileVersion( CSVersionKind branchKind,
                                                ReadOnlySpan<char> exploratoryName = default,
                                                bool isCIBuild = false,
                                                DateTime? utcNow = null )
    {
        var now = utcNow ?? DateTime.UtcNow;
        var v = TimeVersion.Create( DateOnly.FromDateTime( now ) );
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
            v = v.SetCINumber( 0 );
        }
        return FindFreeClashNumber( v );
    }

    /// <summary>
    /// Creates the version of the profile that supersedes <paramref name="origin"/>: the same
    /// <see cref="TimeVersion.Date"/> and branch - as if it had been built the same day - with the next free
    /// <see cref="TimeVersion.ClashNumber"/>.
    /// </summary>
    /// <param name="origin">The version of the profile being superseded.</param>
    /// <returns>A version that no profile of this folder uses.</returns>
    public TimeVersion CreateSupersedingProfileVersion( TimeVersion origin )
    {
        ArgumentNullException.ThrowIfNull( origin );
        return FindFreeClashNumber( origin.SetClashNumber( origin.ClashNumber + 1 ) );
    }

    // Mints the ClashNumber above every version already used that day on that branch - never in a hole - and the
    // regular and the CI form of a branch SHARE that counter (TimeVersion.BranchName is the same for both).
    // This is what makes the numbers meaningful once RemoveSupersededCIProfiles deletes profiles: reusing a
    // freed number would let a version stop identifying a publication, and would let a freshly minted CI
    // form sort below the release it actually follows.
    // A file that exists but cannot be read counts as used: Save would replace it.
    TimeVersion FindFreeClashNumber( TimeVersion v )
    {
        LoadAll();
        int clashNumber = v.ClashNumber;
        foreach( var (version, info) in _profiles )
        {
            if( info.IsUsed
                && version.ClashNumber >= clashNumber
                && version.Date == v.Date
                && version.BranchName == v.BranchName )
            {
                clashNumber = version.ClashNumber + 1;
            }
        }
        return v.SetClashNumber( clashNumber );
    }

    /// <summary>
    /// Gets the profile of a version. The file is read on demand: when this is null,
    /// <see cref="GetLoadError(TimeVersion)"/> tells whether the file was missing or unreadable.
    /// </summary>
    /// <param name="version">The version to find.</param>
    /// <returns>The profile or null.</returns>
    public PublishedProfile? Find( TimeVersion version ) => LoadInfo( version ).Current;

    /// <summary>
    /// Gets the error that occurred while reading the file of a version. The file is read on demand.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>The error or null.</returns>
    public Exception? GetLoadError( TimeVersion version ) => LoadInfo( version ).LoadError;

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
    public IEnumerable<(TimeVersion Version, Exception Error)> LoadErrors
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
    /// An existing file that cannot be read (see <see cref="GetLoadError(TimeVersion)"/>) doesn't prevent
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
    public bool Remove( TimeVersion version )
    {
        var info = LoadInfo( version );
        if( info.Current == null ) return false;
        info.Current = null;
        return true;
    }

    /// <summary>
    /// Removes the CI profiles of a branch that a publication supersedes: a publication supersedes the CI
    /// publications that precede it on its own branch, and a non CI one supersedes them all.
    /// <para>
    /// A superseded CI publication describes a state that no longer applies - and whose packages the CI feeds
    /// eventually unlist - so it is deleted rather than kept, exactly like
    /// <see cref="OnExpiredPackage(string, SVersion)"/> does. This is what keeps a folder from growing with one
    /// profile per CI build, and it is what lets a reader take a CI profile it finds in the index at face value:
    /// at most one alive CI profile per branch remains, and it is newer than every non CI publication of that
    /// branch.
    /// </para>
    /// <para>
    /// Only the alive profiles are considered: a <see cref="PublishedProfile.IsDeprecated"/> one is a record of
    /// a problem and is left alone. The non CI profiles are never removed. All the files are read.
    /// </para>
    /// </summary>
    /// <param name="publishedVersion">The version of the profile that has just been published.</param>
    /// <returns>The removed versions, in ascending order. Empty when nothing was superseded.</returns>
    public ImmutableArray<TimeVersion> RemoveSupersededCIProfiles( TimeVersion publishedVersion )
    {
        ArgumentNullException.ThrowIfNull( publishedVersion );
        var branchName = publishedVersion.BranchName;
        LoadAll();
        var removed = ImmutableArray.CreateBuilder<TimeVersion>();
        foreach( var info in _profiles.Values )
        {
            var p = info.Current;
            if( p == null || p.IsDeprecated ) continue;
            var v = p.Version;
            if( !v.IsCI || v.BranchName != branchName || v == publishedVersion ) continue;
            // A CI publication supersedes only the CI publications that precede it: a more recent one (there
            // should be none) is not superseded by it.
            if( publishedVersion.IsCI && v > publishedVersion ) continue;
            info.Current = null;
            removed.Add( v );
        }
        removed.Sort();
        return removed.DrainToImmutable();
    }

    /// <summary>
    /// Deprecates a profile. This is idempotent and doesn't require the profile to exist.
    /// </summary>
    /// <param name="version">The version to deprecate.</param>
    /// <returns>True if the profile has been found and actually deprecated. False otherwise.</returns>
    public bool Deprecate( TimeVersion version )
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
    /// Removes every profile that carries the provided package: the counterpart of
    /// <see cref="OnDeprecatedPackage(string, SVersion)"/> for a deprecation that has expired. All the
    /// files are read.
    /// <para>
    /// An expired deprecation removes the version tag and unlists the packages from the feeds: a profile
    /// that carries one of them describes something that no longer exists, so it is deleted rather than
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
            if( p != null && p.ProducedPackages.TryGetValue( packageId, out var produced ) && produced.Version == version )
            {
                info.Current = null;
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// Adds a superseding profile for every profile that carries one of the fixed packages: the same
    /// produced packages with the fixed versions replaced, at a <see cref="CreateSupersedingProfileVersion"/>
    /// version. All the files are read.
    /// <para>
    /// The superseded profiles are left untouched: a profile records what was published, and a fix does not
    /// change the past. A <see cref="PublishedProfile.IsDeprecated"/> profile is <c>locked</c> and gets no
    /// successor at all.
    /// </para>
    /// <para>
    /// This is idempotent: produced packages that some profile already carries are not created a second
    /// time, so retrying an interrupted fix publication adds nothing.
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
        // created here already carries the fixed versions - it must not be superseded in its turn.
        var origins = _profiles.Values.Where( i => i.Current != null && !i.Current.IsDeprecated )
                                      .Select( i => i.Current! )
                                      .OrderBy( p => p.Version )
                                      .ToList();
        var known = _profiles.Values.Where( i => i.Current != null )
                                    .Select( i => i.Current! )
                                    .ToList();
        var created = ImmutableArray.CreateBuilder<PublishedProfile>();
        foreach( var origin in origins )
        {
            var updated = CreateSupersedingProfile( origin, fixedPackages );
            if( updated == null || known.Any( o => SameProducedPackages( o, updated ) ) ) continue;
            Add( updated );
            known.Add( updated );
            created.Add( updated );
        }
        return created.DrainToImmutable();

        // Two profiles carry the same produced packages when they carry the same versions of the same
        // package identifiers: the superseding profile differs from its origin by nothing else, so this is
        // what makes a retry a no-op.
        static bool SameProducedPackages( PublishedProfile p1, PublishedProfile p2 )
        {
            if( p1.ProducedPackages.Count != p2.ProducedPackages.Count ) return false;
            foreach( var (packageId, instance) in p1.ProducedPackages )
            {
                if( !p2.ProducedPackages.TryGetValue( packageId, out var other ) || other.Version != instance.Version )
                {
                    return false;
                }
            }
            return true;
        }
    }

    // Returns null when the profile carries none of the fixed packages: only the repositories and the
    // packages that actually change are rebuilt, the others are shared with the origin.
    //
    // The dependencies are carried over: a fix successor is a projection of its origin and OnFixBuildAsync
    // has no roadmap to recompute them from. They can therefore be stale - a fix that changed an external
    // reference is not reflected here - which is acceptable only because a fix is a minimal patch on a
    // released line. The one thing that CANNOT be carried verbatim is an ambiguity anchored on a produced
    // package whose version the fix moves: its anchor has to move with it, and resolutions that the new
    // version has caught up with stop being disagreements at all.
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
                                        repositories.DrainToImmutable(),
                                        origin.DirectDependencies,
                                        CarryTransitiveDependencies( origin, fixedPackages ) );
    }

    // Re-anchors the ambiguities that name a fixed produced package and drops the ones the fix resolved.
    // Everything else - the regular entries, and any ambiguity anchored elsewhere - is shared with the origin:
    // their identifiers are external packages that a fix does not move.
    static TransitiveDependencies CarryTransitiveDependencies( PublishedProfile origin,
                                                               IReadOnlyDictionary<PackageInstance, SVersion> fixedPackages )
    {
        var t = origin.TransitiveDependencies;
        ImmutableArray<AmbiguousDependency>.Builder? ambiguous = null;
        for( int i = 0; i < t.Ambiguous.Length; ++i )
        {
            var a = t.Ambiguous[i];
            if( a.ResolvedFrom != VersionSource.ProducedPackages
                || !fixedPackages.TryGetValue( new PackageInstance( a.PackageId, a.Version ), out var superseding ) )
            {
                if( ambiguous != null ) ambiguous.Add( a );
                continue;
            }
            if( ambiguous == null )
            {
                ambiguous = ImmutableArray.CreateBuilder<AmbiguousDependency>( t.Ambiguous.Length );
                ambiguous.AddRange( t.Ambiguous.Take( i ) );
            }
            // Only the harmful direction is reported: a resolution the superseding version has reached or
            // passed is no longer a disagreement, and an ambiguity with nothing left to report disappears.
            var greater = a.Resolutions.Where( r => r.Version > superseding ).ToImmutableArray();
            if( greater.IsEmpty ) continue;
            ambiguous.Add( new AmbiguousDependency( a.PackageId,
                                                    superseding,
                                                    VersionSource.ProducedPackages,
                                                    greater ) );
        }
        return ambiguous == null
                ? t
                : new TransitiveDependencies( t.Regular, ambiguous.DrainToImmutable() );
    }

    /// <summary>
    /// Writes the added and updated profiles, deletes the files of the removed ones and, when at least one
    /// of them changed, refreshes the <see cref="IndexFileName"/> file (see <see cref="CreateIndex"/>).
    /// <para>
    /// The legacy "v{Year.DayOfYear.Number}.json" files that have been read are moved to their
    /// "v{Year.Month.Day}.json" (or "v{Year.Month.Day.ClashNumber}.json") file (and the index is refreshed): this is not a change of the profile,
    /// it doesn't count.
    /// </para>
    /// </summary>
    /// <returns>The number of created, updated or deleted profile files. The index doesn't count.</returns>
    public int Save()
    {
        int count = 0;
        bool migrated = false;
        foreach( var info in _profiles.Values )
        {
            bool isDirty = info.IsDirty;
            var legacyPath = info.LegacyFilePath;
            var p = info.Current;
            // An unreadable legacy file stays where it is (a dirty one is replaced below).
            if( !isDirty && (legacyPath == null || p == null) ) continue;
            if( p == null )
            {
                File.Delete( legacyPath ?? info.JsonFilePath );
            }
            else
            {
                var dirPath = Path.GetDirectoryName( info.JsonFilePath );
                if( dirPath != null ) Directory.CreateDirectory( dirPath );
                File.WriteAllBytes( info.JsonFilePath, p.ToUtf8Bytes() );
                if( legacyPath != null ) File.Delete( legacyPath );
            }
            migrated |= legacyPath != null;
            info.OnSaved();
            if( isDirty ) ++count;
        }
        // The index reflects the profile files: it is written last, and only when they moved.
        if( count > 0 || migrated ) WriteIndex();
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

    FileCacheInfo LoadInfo( TimeVersion version )
    {
        if( _profiles.TryGetValue( version, out var info ) )
        {
            return info;
        }
        var path = GetProfileFilePath( version );
        if( File.Exists( path ) )
        {
            info = FileCacheInfo.Read( version, path, null );
        }
        else
        {
            var legacyPath = GetLegacyProfileFilePath( version );
            info = File.Exists( legacyPath )
                    ? FileCacheInfo.Read( version, path, legacyPath )
                    : FileCacheInfo.NoFile( version, path );
        }
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
            if( !SVersion.TryMatch( ref fName, out var sVersion ) || fName.Length != 0 )
            {
                continue;
            }
            // A legacy "Year.DayOfYear.Number" file: its DayOfYear is greater than 12 (see AsTimeVersion).
            bool isLegacy = sVersion.Minor > 12;
            var version = sVersion.AsTimeVersion( acceptDayOfYear: true );
            // Skip any version that is not a profile one and any duplicate (already loaded, bad +metadata...).
            if( version == null || _profiles.ContainsKey( version ) ) continue;
            // Skip any file that is not at its canonical path: a file in the wrong branch folder
            // is not a profile of this folder.
            var path = GetProfileFilePath( version );
            var expectedPath = isLegacy ? GetLegacyProfileFilePath( version ) : path;
            if( !expectedPath.Equals( f, StringComparison.OrdinalIgnoreCase ) ) continue;
            _profiles.Add( version, FileCacheInfo.Read( version, path, isLegacy ? f : null ) );
        }
        _allLoaded = true;
    }
}
