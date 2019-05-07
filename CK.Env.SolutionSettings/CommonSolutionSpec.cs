using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Immutable implementation of <see cref="ICommonSolutionSpec"/>.
    /// </summary>
    public class CommonSolutionSpec : ICommonSolutionSpec
    {
        public CommonSolutionSpec( XElement e, ArtifactCenter artifacts )
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
                             .ApplyAddRemoveClear( s => artifacts.Find( (string)s.AttributeRequired( "Name" ) ) );

            Plugins = e.Elements( nameof( Plugins ) )
                             .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Type" ), s => SimpleTypeFinder.WeakResolver( (string)s.Attribute( "Type" ), true ) )
                             .Values;
        }

        public CommonSolutionSpec( ICommonSolutionSpec other, ArtifactCenter artifacts, XElement applyConfig = null )
        {
            NoDotNetUnitTests = other.NoDotNetUnitTests;
            NoStrongNameSigning = other.NoStrongNameSigning;
            NoSharedPropsFile = other.NoSharedPropsFile;
            DisableSourceLink = other.DisableSourceLink;
            if( applyConfig == null )
            {
                NuGetSources = other.NuGetSources;
                RemoveNuGetSourceNames = other.RemoveNuGetSourceNames;
                NPMSources = other.NPMSources;
                RemoveNPMScopeNames = other.RemoveNPMScopeNames;
                ArtifactTargets = other.ArtifactTargets;
            }
            else
            {
                var nuGetSources = other.NuGetSources.ToDictionary( s => s.Name );
                var excludedNuGetSourceNames = new HashSet<string>( other.RemoveNuGetSourceNames );
                var npmSources = other.NPMSources.ToDictionary( s => s.Scope );
                var excludedNPMScopeNames = new HashSet<string>( other.RemoveNPMScopeNames );

                var artifactTargets = new HashSet<IArtifactRepository>( other.ArtifactTargets );

                var disableSourceLink = (bool?)applyConfig.Attribute( nameof( DisableSourceLink ) );
                if( disableSourceLink.HasValue ) DisableSourceLink = disableSourceLink.Value;

                var noUnitTests = (bool?)applyConfig.Attribute( nameof( NoDotNetUnitTests ) );
                if( noUnitTests.HasValue ) NoDotNetUnitTests = noUnitTests.Value;

                var noStrongNameSigning = (bool?)applyConfig.Attribute( nameof( NoStrongNameSigning ) );
                if( noStrongNameSigning.HasValue ) NoStrongNameSigning = noStrongNameSigning.Value;

                var noNoSharedPropsFile = (bool?)applyConfig.Attribute( nameof( NoSharedPropsFile ) );
                if( noNoSharedPropsFile.HasValue ) NoSharedPropsFile = noNoSharedPropsFile.Value;

                NuGetSources = applyConfig.Elements( nameof( NuGetSources ) )
                                .ApplyAddRemoveClear( nuGetSources, s => (string)s.AttributeRequired( "Name" ), s => new NuGetSource( s ) )
                                .Values;

                RemoveNuGetSourceNames = applyConfig.Elements( nameof( RemoveNuGetSourceNames ) )
                                            .ApplyAddRemoveClear( excludedNuGetSourceNames, s => (string)s.AttributeRequired( "Name" ) );

                NPMSources = applyConfig.Elements( nameof( NPMSources ) )
                                .ApplyAddRemoveClear( npmSources, s => (string)s.AttributeRequired( "Scope" ), s => new NPMSource( s ) )
                                .Values;

                RemoveNPMScopeNames = applyConfig.Elements( nameof( RemoveNPMScopeNames ) )
                                            .ApplyAddRemoveClear( excludedNuGetSourceNames, s => (string)s.AttributeRequired( "Scope" ) );


                ArtifactTargets = applyConfig.Elements( nameof( ArtifactTargets ) )
                                    .ApplyAddRemoveClear( artifactTargets, eF => artifacts.Find( (string)eF.AttributeRequired( "Name" ) ) );

                Plugins = applyConfig.Elements( nameof( Plugins ) )
                     .ApplyAddRemoveClear( new HashSet<Type>( other.Plugins ), e => SimpleTypeFinder.WeakResolver( (string)e.AttributeRequired( "Type" ), true ) );
            }
        }

        public bool NoDotNetUnitTests { get; }

        public bool NoStrongNameSigning { get; }

        public bool NoSharedPropsFile { get; }

        public bool DisableSourceLink { get; }

        public IReadOnlyCollection<INuGetSource> NuGetSources { get; }

        public IReadOnlyCollection<string> RemoveNuGetSourceNames { get; }

        public IReadOnlyCollection<INPMSource> NPMSources { get; }

        public IReadOnlyCollection<string> RemoveNPMScopeNames { get; }

        public IReadOnlyCollection<IArtifactRepository> ArtifactTargets { get; }

        public IReadOnlyCollection<Type> Plugins { get; }
    }
}
