using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using CodeCake.Abstractions;
using SimpleGitVersion;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CodeCake
{
    [AddPath( "%UserProfile%/.nuget/packages/**/tools*" )]
    public partial class Build : CodeCakeHost
    {
        public Build()
        {
            Cake.Log.Verbosity = Verbosity.Diagnostic;

            SimpleRepositoryInfo gitInfo = Cake.GetSimpleRepositoryInfo();
            StandardGlobalInfo globalInfo = CreateStandardGlobalInfo( gitInfo )
                                                .AddDotnet()
                                                .SetCIBuildTag();

            var nuGetType = globalInfo.ArtifactTypes.OfType<NuGetArtifactType>().Single();
            //Because this CCB is our reference for the ApplySettings, we must not modify Build.NuGetArtifactType.GetRemoteFeeds
            //So here we modify the feed.
            IList<ArtifactFeed> feeds = nuGetType.GetTargetFeeds();
            Debug.Assert( feeds.Last().GetType() == typeof( SignatureVSTSFeed ), "The default remote is the last one." );
            feeds[feeds.Count - 1] = new RemoteFeed( nuGetType, "nuget.org", "https://api.nuget.org/v3/index.json", "NUGET_ORG_PUSH_API_KEY" );

            Task( "Check-Repository" )
                .Does( () =>
                {
                    globalInfo.TerminateIfShouldStop();
                } );

            Task( "Clean" )
                .IsDependentOn( "Check-Repository" )
                .Does( () =>
                {
                    globalInfo.GetDotnetSolution().Clean();
                    Cake.CleanDirectories( globalInfo.ReleasesFolder );
                } );

            Task( "Build" )
                .IsDependentOn( "Check-Repository" )
                .IsDependentOn( "Clean" )
                .Does( () =>
                {
                    globalInfo.GetDotnetSolution().Build();
                } );

            Task( "Unit-Testing" )
                .IsDependentOn( "Build" )
                .WithCriteria( () => Cake.InteractiveMode() == InteractiveMode.NoInteraction
                                     || Cake.ReadInteractiveOption( "RunUnitTests", "Run Unit Tests?", 'Y', 'N' ) == 'Y' )
                .Does( () =>
                {
                    globalInfo.GetDotnetSolution().Test();
                } );

            Task( "Create-NuGet-Packages" )
                .WithCriteria( () => gitInfo.IsValid )
                .IsDependentOn( "Unit-Testing" )
                .Does( () =>
                {
                    globalInfo.GetDotnetSolution().Pack();
                } );

            Task( "Push-Artifacts" )
                .IsDependentOn( "Create-NuGet-Packages" )
                .WithCriteria( () => gitInfo.IsValid )
                .Does( () =>
                {
                    // Cheat here for this build.cs to avoid changing the default
                    //
                    //      protected override IEnumerable<ArtifactFeed> GetRemoteFeeds()
                    //      {
                    //          yield return new SignatureVSTSFeed( this, "Signature-Code", "CKEnvTest3" );
                    //      }
                    //
                    // that is targeted by the CK.Env.Plugin.NuGetCodeCakeBuilderFolder.AdaptBuildNugetRepositoryForPushFeeds
                    // method.
                    //


                    globalInfo.PushArtifacts();
                } );

            // The Default task for this script can be set here.
            Task( "Default" )
                .IsDependentOn( "Push-Artifacts" );
        }

    }
}
