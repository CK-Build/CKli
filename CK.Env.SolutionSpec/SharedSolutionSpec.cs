using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Immutable implementation of <see cref="ISharedSolutionSpec"/>.
    /// </summary>
    public class SharedSolutionSpec : ISharedSolutionSpec
    {
        /// <summary>
        /// Initializes a new initial <see cref="SharedSolutionSpec"/>.
        /// The ArtifactCenter is required because of <see cref="ArtifactTargets"/> that
        /// directly exposes the target <see cref="IArtifactRepository"/> of a solution.
        /// </summary>
        /// <param name="e">The Xml element.</param>
        /// <param name="a">The artifact center.</param>
        public SharedSolutionSpec( XElement e, ArtifactCenter a )
        {
            NoDotNetUnitTests = (bool?)e.Attribute( nameof( NoDotNetUnitTests ) ) ?? false;
            NoStrongNameSigning = (bool?)e.Attribute( nameof( NoStrongNameSigning ) ) ?? false;
            NoSharedPropsFile = (bool?)e.Attribute( nameof( NoSharedPropsFile ) ) ?? false;
            DisableSourceLink = (bool?)e.Attribute( nameof(DisableSourceLink) ) ?? false;

            NuGetSources = e.Elements( nameof( NuGetSources ) )
                             .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Name" ), s => new NuGetSource( s ) )
                             .Values;
            RemoveNuGetSourceNames = e.Elements( nameof( RemoveNuGetSourceNames ) )
                                        .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Name" ) );

            NPMSources = e.Elements( nameof( NPMSources ) )
                             .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Scope" ), s => new NPMSource( s ) )
                             .Values;
            RemoveNPMScopeNames = e.Elements( nameof( RemoveNPMScopeNames ) )
                                        .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Scope" ) );

            ArtifactTargets = e.Elements( nameof( ArtifactTargets ) )
                             .ApplyAddRemoveClear( s => a.Find( (string)s.AttributeRequired( "Name" ) ) );

            ExcludedPlugins = e.Elements( nameof( ExcludedPlugins ) )
                             .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Type" ), s => SimpleTypeFinder.WeakResolver( (string)s.Attribute( "Type" ), true ) )
                             .Values;
        }

        /// <summary>
        /// Initializes a secondary <see cref="SharedSolutionSpec"/> that alters a previous one.
        /// </summary>
        /// <param name="other">The previous specification.</param>
        /// <param name="a">The required artifact center.</param>
        /// <param name="e">
        /// The new Xml element.
        /// When null, a shallow copy is obtained and since these objects are immutable, this acts
        /// as an independent clone.
        /// </param>
        public SharedSolutionSpec( ISharedSolutionSpec other, ArtifactCenter a, XElement e = null )
        {
            NoDotNetUnitTests = other.NoDotNetUnitTests;
            NoStrongNameSigning = other.NoStrongNameSigning;
            NoSharedPropsFile = other.NoSharedPropsFile;
            DisableSourceLink = other.DisableSourceLink;
            if( e == null )
            {
                NuGetSources = other.NuGetSources;
                RemoveNuGetSourceNames = other.RemoveNuGetSourceNames;
                NPMSources = other.NPMSources;
                RemoveNPMScopeNames = other.RemoveNPMScopeNames;
                ArtifactTargets = other.ArtifactTargets;
                ExcludedPlugins = other.ExcludedPlugins;
            }
            else
            {
                var nuGetSources = other.NuGetSources.ToDictionary( s => s.Name );
                var excludedNuGetSourceNames = new HashSet<string>( other.RemoveNuGetSourceNames );
                var npmSources = other.NPMSources.ToDictionary( s => s.Scope );
                var excludedNPMScopeNames = new HashSet<string>( other.RemoveNPMScopeNames );

                var artifactTargets = new HashSet<IArtifactRepository>( other.ArtifactTargets );

                var disableSourceLink = (bool?)e.Attribute( nameof( DisableSourceLink ) );
                if( disableSourceLink.HasValue ) DisableSourceLink = disableSourceLink.Value;

                var noUnitTests = (bool?)e.Attribute( nameof( NoDotNetUnitTests ) );
                if( noUnitTests.HasValue ) NoDotNetUnitTests = noUnitTests.Value;

                var noStrongNameSigning = (bool?)e.Attribute( nameof( NoStrongNameSigning ) );
                if( noStrongNameSigning.HasValue ) NoStrongNameSigning = noStrongNameSigning.Value;

                var noNoSharedPropsFile = (bool?)e.Attribute( nameof( NoSharedPropsFile ) );
                if( noNoSharedPropsFile.HasValue ) NoSharedPropsFile = noNoSharedPropsFile.Value;

                NuGetSources = e.Elements( nameof( NuGetSources ) )
                                .ApplyAddRemoveClear( nuGetSources, s => (string)s.AttributeRequired( "Name" ), s => new NuGetSource( s ) )
                                .Values;

                RemoveNuGetSourceNames = e.Elements( nameof( RemoveNuGetSourceNames ) )
                                            .ApplyAddRemoveClear( excludedNuGetSourceNames, s => (string)s.AttributeRequired( "Name" ) );

                NPMSources = e.Elements( nameof( NPMSources ) )
                                .ApplyAddRemoveClear( npmSources, s => (string)s.AttributeRequired( "Scope" ), s => new NPMSource( s ) )
                                .Values;

                RemoveNPMScopeNames = e.Elements( nameof( RemoveNPMScopeNames ) )
                                            .ApplyAddRemoveClear( excludedNuGetSourceNames, s => (string)s.AttributeRequired( "Scope" ) );


                ArtifactTargets = e.Elements( nameof( ArtifactTargets ) )
                                    .ApplyAddRemoveClear( artifactTargets, eF => a.Find( (string)eF.AttributeRequired( "Name" ) ) );

                ExcludedPlugins = e.Elements( nameof( ExcludedPlugins ) )
                     .ApplyAddRemoveClear( new HashSet<Type>( other.ExcludedPlugins ), eF => SimpleTypeFinder.WeakResolver( (string)eF.AttributeRequired( "Type" ), true ) );
            }
        }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.NoDotNetUnitTests"/>.
        /// </summary>
        public bool NoDotNetUnitTests { get; }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.NoStrongNameSigning"/>.
        /// </summary>
        public bool NoStrongNameSigning { get; }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.NoSharedPropsFile"/>.
        /// </summary>
        public bool NoSharedPropsFile { get; }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.DisableSourceLink"/>.
        /// </summary>
        public bool DisableSourceLink { get; }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.NuGetSources"/>.
        /// </summary>
        public IReadOnlyCollection<INuGetSource> NuGetSources { get; }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.RemoveNuGetSourceNames"/>.
        /// </summary>
        public IReadOnlyCollection<string> RemoveNuGetSourceNames { get; }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.NPMSources"/>.
        /// </summary>
        public IReadOnlyCollection<INPMSource> NPMSources { get; }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.RemoveNPMScopeNames"/>.
        /// </summary>
        public IReadOnlyCollection<string> RemoveNPMScopeNames { get; }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.ArtifactTargets"/>.
        /// </summary>
        public IReadOnlyCollection<IArtifactRepository> ArtifactTargets { get; }

        /// <summary>
        /// <see cref="ISharedSolutionSpec.ExcludedPlugins"/>.
        /// </summary>
        public IReadOnlyCollection<Type> ExcludedPlugins { get; }
    }
}
