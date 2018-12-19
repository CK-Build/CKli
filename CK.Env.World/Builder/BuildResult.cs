using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    public class BuildResult
    {
        internal BuildResult( IReadOnlyList<IDependentSolution> s, IReadOnlyList<SVersion> v )
        {
            GeneratedArtifacts = s.SelectMany( solution => solution.GeneratedArtifacts
                                                            .Select( a => ( a, solution.UniqueSolutionName, v[solution.Index] ) ) )
                                  .ToList();
        }

        /// <summary>
        /// Initializes a new <see cref="BuildResult"/> from a xml element.
        /// </summary>
        /// <param name="e">The xml element.</param>
        public BuildResult( XElement e )
        {
            GeneratedArtifacts = e.Elements( "A" )
                                  .Select( a => (
                                            new Artifact( (string)a.AttributeRequired("Type"), (string)a.AttributeRequired( "Name" ) ),
                                            (string)a.AttributeRequired( "Solution" ),
                                            SVersion.Parse( (string)a.AttributeRequired( "Version" ) ) ) )
                                  .ToList();
        }

        /// <summary>
        /// Gets the <see cref="Artifact"/> with their originating solution and version.
        /// </summary>
        public IReadOnlyList<(Artifact Artifact, string SolutionName, SVersion Version)> GeneratedArtifacts { get; }

        /// <summary>
        /// Exports this <see cref="BuildResult"/> as xml.
        /// </summary>
        /// <returns>A "BuildResult" element.</returns>
        public XElement ToXml()
        {
            var artifacts = GeneratedArtifacts.Select( a => new XElement( "A",
                                                                   new XAttribute( "Type", a.Artifact.Type ),
                                                                   new XAttribute( "Name", a.Artifact.Name ),
                                                                   new XAttribute( "Solution", a.SolutionName ),
                                                                   new XAttribute( "Version", a.Version ) ) );
            return new XElement( "BuildResult", artifacts );
        }

    }
}
