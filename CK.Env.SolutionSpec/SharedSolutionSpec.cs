using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <param name="r">The element reader.</param>
        public SharedSolutionSpec( in XElementReader r )
        {
            NoDotNetUnitTests = r.HandleOptionalAttribute( nameof( NoDotNetUnitTests ), false );
            NoStrongNameSigning = r.HandleOptionalAttribute( nameof( NoStrongNameSigning ), false );
            NoSharedPropsFile = r.HandleOptionalAttribute( nameof( NoSharedPropsFile ), false );
            DisableSourceLink = r.HandleOptionalAttribute( nameof(DisableSourceLink), false );

            ArtifactTargets = r.HandleCollection(
                    nameof( ArtifactTargets ),
                    new HashSet<string>(),
                    eR => eR.HandleRequiredAttribute<string>( "Name" ) );

            ExcludedPlugins = r.HandleCollection(
                                    nameof( ExcludedPlugins ),
                                    new HashSet<Type>(),
                                    eR => SimpleTypeFinder.WeakResolver( eR.HandleRequiredAttribute<string>( "Type" ), true ) );

            var e = r.Element;

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

        }

        /// <summary>
        /// Initializes a secondary <see cref="SharedSolutionSpec"/> that alters a previous one.
        /// </summary>
        /// <param name="other">The previous specification.</param>
        /// <param name="r">The element reader.</param>
        public SharedSolutionSpec( SharedSolutionSpec other, in XElementReader r )
        {
            DisableSourceLink = r.HandleOptionalAttribute( nameof( DisableSourceLink ), other.DisableSourceLink );
            NoDotNetUnitTests = r.HandleOptionalAttribute( nameof( NoDotNetUnitTests ), other.NoDotNetUnitTests );
            NoStrongNameSigning = r.HandleOptionalAttribute( nameof( NoStrongNameSigning ), other.NoStrongNameSigning );
            NoSharedPropsFile = r.HandleOptionalAttribute( nameof( NoSharedPropsFile ), other.NoSharedPropsFile );

            var nuGetSources = other.NuGetSources.ToDictionary( s => s.Name );
            var excludedNuGetSourceNames = new HashSet<string>( other.RemoveNuGetSourceNames );
            var npmSources = other.NPMSources.ToDictionary( s => s.Scope );
            var excludedNPMScopeNames = new HashSet<string>( other.RemoveNPMScopeNames );

            ArtifactTargets = r.HandleCollection(
                                nameof( ArtifactTargets ),
                                new HashSet<string>( other.ArtifactTargets ),
                                eR => eR.HandleRequiredAttribute<string>( "Name" ) );

            ExcludedPlugins = r.HandleCollection(
                                    nameof( ExcludedPlugins ),
                                    new HashSet<Type>( other.ExcludedPlugins ),
                                    eR => SimpleTypeFinder.WeakResolver( eR.HandleRequiredAttribute<string>( "Type" ), true ) );

            var e = r.Element;

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
