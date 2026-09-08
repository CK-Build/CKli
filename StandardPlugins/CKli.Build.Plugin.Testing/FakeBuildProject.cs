using CK.Core;
using CKli.Core;
using Microsoft.Extensions.FileProviders;
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

    /// <summary>
    /// Reads a .csproj file to extract the &lt;PackageReference&gt;.
    /// </summary>
    /// <param name="f">The file info.</param>
    /// <returns>The package references.</returns>
    public static ImmutableArray<PackageInstance> ReadReferences( IFileInfo f )
    {
        // XDocument.Load and not XNode.ReadFrom on a fresh XmlReader: that reader is in the Initial
        // state, and ReadFrom requires an Interactive one ("The XmlReader state should be Interactive.").
        using( var s = f.CreateReadStream() )
        {
            return XDocument.Load( s ).Root!
                        .Elements( XNames.ItemGroup )
                        .Elements( XNames.PackageReference )
                        .Select( r => new PackageInstance( (string)r.Attribute( XNames.Include )!, SVersion.Parse( (string)r.Attribute( XNames.Version )! ) ) )
                        .ToImmutableArray();
        }
    }
}
