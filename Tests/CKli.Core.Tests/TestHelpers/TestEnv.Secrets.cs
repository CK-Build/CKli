using CK.Core;
using Shouldly;
using System;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

static partial class TestEnv
{
    /// <summary>
    /// When a test pushes (from git local to remotes), we need the write PAT for the "FILESYSTEM".
    /// <para>
    /// That is useless (credentials are not used on local file system) but it's good to not make an
    /// exception for this case: <see cref="RemoveFileSystemWritePAT"/> must be called from a finally (or a
    /// TearDown) so that a failing test doesn't leave the secret behind.
    /// </para>
    /// </summary>
    public static void SetFileSystemWritePAT() => UserSecrets( """set FILESYSTEM_GIT "don't care" """ );

    /// <summary>
    /// When a test DOESN'T push (it has no impact on the remotes), the secret must not be registered: this
    /// ensures that the test cannot push anything. Removing an unregistered secret is not an error.
    /// </summary>
    public static void RemoveFileSystemWritePAT() => UserSecrets( "remove FILESYSTEM_GIT" );

    static void UserSecrets( string command )
    {
        // CKliRootEnv.InstanceName requires the initialization done by SetupEnv. TestEnv is the
        // [SetUpFixture] of this assembly: it has run before any test or [SetUp] can call this.
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  $"user-secrets {command} --id {CKliRootEnv.InstanceName}",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );
    }
}
