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
    public class SharedSolutionSpec
    {
        /// <summary>
        /// Initializes a new initial <see cref="SharedSolutionSpec"/>.
        /// </summary>
        /// <param name="e">The <see cref="XTypedObject"/> initializer.</param>
        public SharedSolutionSpec( XTypedObject.Initializer r )
        {
            var e = r.Element;

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
                             .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Name" ) );

            ExcludedPlugins = e.Elements( nameof( ExcludedPlugins ) )
                             .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Type" ), s => SimpleTypeFinder.WeakResolver( (string)s.Attribute( "Type" ), true ) )
                             .Values;
        }

        /// <summary>
        /// Initializes a secondary <see cref="SharedSolutionSpec"/> that alters a previous one.
        /// </summary>
        /// <param name="other">The previous specification.</param>
        /// <param name="r">The initializer.
        /// </param>
        public SharedSolutionSpec( SharedSolutionSpec other, XTypedObject.Initializer r )
        {
            NoDotNetUnitTests = other.NoDotNetUnitTests;
            NoStrongNameSigning = other.NoStrongNameSigning;
            NoSharedPropsFile = other.NoSharedPropsFile;
            DisableSourceLink = other.DisableSourceLink;

            var nuGetSources = other.NuGetSources.ToDictionary( s => s.Name );
            var excludedNuGetSourceNames = new HashSet<string>( other.RemoveNuGetSourceNames );
            var npmSources = other.NPMSources.ToDictionary( s => s.Scope );
            var excludedNPMScopeNames = new HashSet<string>( other.RemoveNPMScopeNames );
            var artifactTargets = new HashSet<string>( other.ArtifactTargets );

            var e = r.Element;

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
                                .ApplyAddRemoveClear( artifactTargets, eF => (string)eF.AttributeRequired( "Name" ) );

            ExcludedPlugins = e.Elements( nameof( ExcludedPlugins ) )
                    .ApplyAddRemoveClear( new HashSet<Type>( other.ExcludedPlugins ), eF => SimpleTypeFinder.WeakResolver( (string)eF.AttributeRequired( "Type" ), true ) );
        }

        /// <summary>
        /// Gets whether the solution has no unit tests.
        /// Defaults to false.
        /// </summary>
        public bool NoDotNetUnitTests { get; }

        /// <summary>
        /// Gets whether no strong name singing should be used.
        /// Defaults to false.
        /// </summary>
        public bool NoStrongNameSigning { get; }

        /// <summary>
        /// Gets whether no shared props file should be used.
        /// Defaults to false.
        /// </summary>
        public bool NoSharedPropsFile { get; }

        /// <summary>
        /// Gets whether source link is disabled.
        /// Impacts Common/Shared.props file.
        /// Defaults to false.
        /// </summary>
        public bool DisableSourceLink { get; }

        /// <summary>
        /// Defines the set of NuGet sources that is used.
        /// Impacts NuGet.config file.
        /// </summary>
        public IReadOnlyCollection<INuGetSource> NuGetSources { get; }

        /// <summary>
        /// Gets the NuGet source names that must be excluded.
        /// Must be used to clean up existing source names that must no more be used.
        /// Impacts NuGet.config file.
        /// </summary>
        public IReadOnlyCollection<string> RemoveNuGetSourceNames { get; }

        /// <summary>
        /// Defines the set of NPM sources that is used.
        /// Impacts .npmrc file.
        /// </summary>
        public IReadOnlyCollection<INPMSource> NPMSources { get; }

        /// <summary>
        /// Gets the NPM scope names that must be excluded.
        /// Must be used to clean up existing scope names that must no more be used.
        /// Impacts .npmrc file.
        /// </summary>
        public IReadOnlyCollection<string> RemoveNPMScopeNames { get; }

        /// <summary>
        /// Gets the repositories names where produced artifacts must be pushed.
        /// </summary>
        public IReadOnlyCollection<string> ArtifactTargets { get; }

        /// <summary>
        /// Defines the set of Git or GitBranch plugins type that must NOT be activated.
        /// By default, all available Git plugins are active.
        /// Note that the excluded type must actually axist otherwise an exception is thrown.
        /// </summary>
        public IReadOnlyCollection<Type> ExcludedPlugins { get; }
    }
}
