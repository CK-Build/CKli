using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class OnAPIBreackingChange
{
    [Test, Explicit( "Use this when a breaking change in the public API occurs." )]
    public void clear_CKliCore_and_CKliPluginsCore_from_NuGet_global_cache()
    {
        NuGetHelper.Cache.RemovePackage( TestHelper.Monitor, "CKli.Core", null );
        NuGetHelper.Cache.RemovePackage( TestHelper.Monitor, "CKli.Plugins.Core", null );
    }
}
