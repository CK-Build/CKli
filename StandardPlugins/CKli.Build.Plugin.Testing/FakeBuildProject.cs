using CK.Core;
using CKli.Core;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace CKli;

/// <summary>
/// Immutable representation of a project in a <see cref="FakeBuildRepo"/>.
/// </summary>
/// <param name="ProjectName">The project name (the produced package name).</param>
/// <param name="References">The package references of this project (the dependencies).</param>
public sealed record FakeBuildProject( string ProjectName, ImmutableArray<PackageInstance> References )
{
    public static XElement ToProjectElement( ImmutableArray<PackageInstance> references )
    {
        return new XElement( ShallowSolution.Plugin.XNames.Project,
                    new XElement( XNames.ItemGroup,
                             references.Select( p => new XElement( XNames.PackageReference,
                                                            new XAttribute( XNames.Include, p.PackageId ),
                                                            new XAttribute( XNames.Version, p.Version ) ) ) ) );
    }
}
