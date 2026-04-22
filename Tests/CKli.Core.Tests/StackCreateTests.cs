using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CK.Core;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class StackCreateTests
{
    [Explicit]
    [TestCase( "Public", "https://github.com/", "ck-build/Demo-Stack" )]
    [TestCase( "Private", "https://github.com/", "ck-build/Demo-Stack" )]
    [TestCase( "Private", "https://gitlab.com/", "olivier-spinelli/Demo-Stack" )]
    [TestCase( "Public", "https://gitlab.com/", "olivier-spinelli/Demo-Stack" )]
    [TestCase( "Private", "https://gitlab.com/", "signature-code/Test/Demo-Stack" )]
    [TestCase( "Public", "https://gitlab.com/", "signature-code/Test/Demo-Stack" )]
    public async Task Create_Demo_Async( string mode, string rootUrl, string repositoryPath )
    {
        bool isPublic = mode == "Public";
        var url = new Uri( rootUrl + repositoryPath );

        var context = TestEnv.EnsureCleanFolder();

        // Removes previous Demo-Stack. 
        var gitKey = new GitRepositoryKey( context.SecretsStore, url, isPublic );
        gitKey.TryGetHostingInfo( TestHelper.Monitor, out var gitHubHosting, out var repoPath ).ShouldBeTrue();
        repoPath.ShouldBe( repositoryPath );
        await gitHubHosting.DeleteRepositoryAsync( TestHelper.Monitor, repoPath ).ConfigureAwait( false );

        using var stack = await StackRepository.CreateAsync( TestHelper.Monitor, context, url, isPublic, ignoreParentStack: true );
        stack.ShouldNotBeNull();
        stack.StackName.ShouldBe( "Demo" );
        stack.IsPublic.ShouldBe( isPublic );
        stack.StackWorkingFolder.LastPart.ShouldBe( isPublic ? StackRepository.PublicStackName : StackRepository.PrivateStackName );

        // Verify definition file exists
        var defFile = stack.StackWorkingFolder.AppendPart( "Demo.xml" );
        File.Exists( defFile ).ShouldBeTrue();

        // Verify git repository initialized
        Directory.Exists( stack.StackWorkingFolder.AppendPart( ".git" ) ).ShouldBeTrue();

        // Verify $Local and .gitignore created
        Directory.Exists( stack.StackWorkingFolder.AppendPart( "$Local" ) ).ShouldBeTrue();
        File.Exists( stack.StackWorkingFolder.AppendPart( ".gitignore" ) ).ShouldBeTrue();

    }

}
