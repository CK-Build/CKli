using CK.Core;
using CK.NuGetClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env
{
    public class SolutionSettings : ISolutionSettings
    {
        public SolutionSettings( XElement e )
        {
            NoUnitTests = (bool?)e.Attribute( nameof( NoUnitTests ) ) ?? false;
            NoStrongNameSigning = (bool?)e.Attribute( nameof( NoStrongNameSigning ) ) ?? false;
            ProduceCKSetupComponents = (bool?)e.Attribute( nameof(ProduceCKSetupComponents) ) ?? false;
            DisableSourceLink = (bool?)e.Attribute( nameof(DisableSourceLink) ) ?? false;
            SqlServer = (string)e.Attribute( nameof( SqlServer ) );

            NuGetSources = e.Elements( nameof( NuGetSources) )
                             .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Name" ), s => new NuGetSource( s ) )
                             .Values;
            ExcludedNuGetSourceNames = e.Elements( nameof( ExcludedNuGetSourceNames ) )
                                        .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Name" ) );
            NuGetPushFeeds = e.Elements( nameof( NuGetPushFeeds ) )
                             .ApplyAddRemoveClear(eF => NuGetFeedInfo.Create(eF), f => f.Name)
                             .Values;
            ExcludedNuGetPushFeedNames = e.Elements( nameof( ExcludedNuGetPushFeedNames ) )
                                        .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Name" ) );
            Plugins = e.Elements( nameof( Plugins ) )
                             .ApplyAddRemoveClear( s => (string)s.AttributeRequired( "Type" ), s => SimpleTypeFinder.WeakResolver( (string)s.Attribute( "Type" ), true ) )
                             .Values;
        }

        public SolutionSettings( ISolutionSettings other, XElement applyConfig = null )
        {
            NoUnitTests = other.NoUnitTests;
            NoStrongNameSigning = other.NoStrongNameSigning;
            ProduceCKSetupComponents = other.ProduceCKSetupComponents;
            DisableSourceLink = other.DisableSourceLink;
            SqlServer = other.SqlServer;
            if( applyConfig == null )
            {
                ExcludedNuGetSourceNames = other.ExcludedNuGetSourceNames;
                ExcludedNuGetPushFeedNames = other.ExcludedNuGetPushFeedNames;
                NuGetSources = other.NuGetSources;
                NuGetPushFeeds = other.NuGetPushFeeds;
            }
            else
            {
                var excludedNuGetSourceNames = new HashSet<string>( other.ExcludedNuGetSourceNames );
                var excludedNuGetPushFeedNames = new HashSet<string>( other.ExcludedNuGetPushFeedNames );
                var nuGetSources = other.NuGetSources.ToDictionary( s => s.Name );
                var nuGetPushFeeds = other.NuGetPushFeeds.ToDictionary( f => f.Name );

                var disableSourceLink = (bool?)applyConfig.Attribute( nameof( DisableSourceLink ) );
                if( disableSourceLink.HasValue ) DisableSourceLink = disableSourceLink.Value;

                var produceCKSetupComponents = (bool?)applyConfig.Attribute( nameof( ProduceCKSetupComponents ) );
                if( produceCKSetupComponents.HasValue ) ProduceCKSetupComponents= produceCKSetupComponents.Value;

                var noUnitTests = (bool?)applyConfig.Attribute( nameof( NoUnitTests ) );
                if( noUnitTests.HasValue ) NoUnitTests = noUnitTests.Value;

                var noStrongNameSigning = (bool?)applyConfig.Attribute( nameof( NoStrongNameSigning ) );
                if( noStrongNameSigning.HasValue ) NoStrongNameSigning = noStrongNameSigning.Value;

                var sqlServer = (string)applyConfig.Attribute( nameof( SqlServer ) );
                if( sqlServer != null ) SqlServer = sqlServer;

                NuGetSources = applyConfig.Elements( nameof( NuGetSources ) )
                                .ApplyAddRemoveClear( nuGetSources, s => (string)s.AttributeRequired( "Name" ), s => new NuGetSource( s ) )
                                .Values;

                ExcludedNuGetSourceNames = applyConfig.Elements( nameof( ExcludedNuGetSourceNames ) )
                                            .ApplyAddRemoveClear( excludedNuGetSourceNames, s => (string)s.AttributeRequired( "Name" ) );

                NuGetPushFeeds = applyConfig.Elements( nameof( NuGetPushFeeds ) )
                                    .ApplyAddRemoveClear( nuGetPushFeeds, eF => NuGetFeedInfo.Create( eF ), f => f.Name )
                                    .Values;

                ExcludedNuGetPushFeedNames = applyConfig.Elements( nameof( ExcludedNuGetPushFeedNames ) )
                           .ApplyAddRemoveClear( excludedNuGetPushFeedNames, s => (string)s.AttributeRequired( "Name" ) );

                Plugins = applyConfig.Elements( nameof( Plugins ) )
                     .ApplyAddRemoveClear( new HashSet<Type>( other.Plugins ), e => SimpleTypeFinder.WeakResolver( (string)e.AttributeRequired( "Type" ), true ) );
            }
        }

        public SolutionSettings With( XElement e ) => new SolutionSettings( this, e );

        public bool NoUnitTests { get; }

        public bool NoStrongNameSigning { get; }

        public bool ProduceCKSetupComponents { get; }

        public bool DisableSourceLink { get; }

        public string SqlServer { get; }

        public IReadOnlyCollection<INuGetSource> NuGetSources { get; }

        public IReadOnlyCollection<string> ExcludedNuGetSourceNames { get; }

        public IReadOnlyCollection<INuGetFeedInfo> NuGetPushFeeds { get; }

        public IReadOnlyCollection<string> ExcludedNuGetPushFeedNames { get; }

        public IReadOnlyCollection<Type> Plugins { get; }
    }
}
