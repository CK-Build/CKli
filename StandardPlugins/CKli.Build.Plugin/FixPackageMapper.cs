using CK.Core;
using CKli.ShallowSolution.Plugin;


namespace CKli.Build.Plugin;

/// <summary>
/// Better mapper than a <see cref="BrutalPackageMapper"/>: this allows upgrades
/// from any prerelease patch version as if it was the previous patch.
/// </summary>
sealed class FixPackageMapper : IPackageMapping
{
    readonly PackageMapper _mapper;

    public FixPackageMapper()
    {
        _mapper = new PackageMapper();
    }

    public void Add( string packageId, SVersion toFixVersion, SVersion targetVersion )
    {
        _mapper.Add( packageId, toFixVersion, targetVersion );
    }

    public bool IsEmpty => _mapper.IsEmpty;

    // Exact, like the PackageMapper it relaxes: the versions to fix are known beforehand, so a reference met
    // at another one (that the Patch - 1 fallback below doesn't catch either) is worth reporting.
    public PackageMappingType GetMappingType( string packageId ) => _mapper.GetMappingType( packageId );

    public SVersion? GetMappedVersion( string packageId, SVersion from )
    {
        var v = _mapper.GetMappedVersion( packageId, from );
        if( v == null && from.Patch > 0 )
        {
            var fromSource = SVersion.Create( from.Major, from.Minor, from.Patch - 1 );
            return _mapper.GetMappedVersion( packageId, fromSource );
        }
        return v;
    }

    public override string ToString() => _mapper.ToString();

}
