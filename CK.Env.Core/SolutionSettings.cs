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
            DisableSourceLink = (bool?)e.Attribute( nameof(DisableSourceLink) ) ?? false;
            ProduceCKSetupComponents = (bool?)e.Attribute( nameof(ProduceCKSetupComponents) ) ?? false;
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
        }

        public SolutionSettings( ISolutionSettings other, XElement applyConfig = null )
        {
            SuppressNuGetConfigFile = other.SuppressNuGetConfigFile;
            ProduceCKSetupComponents = other.ProduceCKSetupComponents;
            DisableSourceLink = other.DisableSourceLink;
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

                var suppressNuGetConfigFile = (bool?)applyConfig.Attribute( nameof( SuppressNuGetConfigFile ) );
                if( suppressNuGetConfigFile.HasValue ) SuppressNuGetConfigFile = suppressNuGetConfigFile.Value;

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
            }
        }

        public SolutionSettings With( XElement e ) => new SolutionSettings( this, e );

        public bool SuppressNuGetConfigFile { get; }

        public bool ProduceCKSetupComponents { get; }

        public bool DisableSourceLink { get; }

        public IReadOnlyCollection<INuGetSource> NuGetSources { get; }

        public IReadOnlyCollection<string> ExcludedNuGetSourceNames { get; }

        public IReadOnlyCollection<INuGetFeedInfo> NuGetPushFeeds { get; }

        public IReadOnlyCollection<string> ExcludedNuGetPushFeedNames { get; }

    }
}
