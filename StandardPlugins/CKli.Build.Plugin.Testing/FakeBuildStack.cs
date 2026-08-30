using CK.Core;
using CK.Testing;
using CKli.Core;

namespace CKli;

/// <summary>
/// Models a stack.
/// Obtained by <see cref="FakeBuildTestEnv.CreateStackAsync(string, System.Action{IActivityMonitor, NormalizedPath, System.Xml.Linq.XElement}?, bool, bool)"/>.
/// </summary>
public sealed class FakeBuildStack
{
    readonly IMonitorTestHelper _helper;
    readonly CKliTestHelperExtensions.RemotesFolder _remotes;
    readonly bool _isPublic;
    readonly FakeBuildWorld _defaultWorld;

    internal FakeBuildStack( IMonitorTestHelper helper, CKliTestHelperExtensions.RemotesFolder remotes, CKliEnv defaultWorldContext, bool privateStack )
    {
        _helper = helper;
        _remotes = remotes;
        _isPublic = !privateStack;
        _defaultWorld = new FakeBuildWorld( helper, this, defaultWorldContext );
    }

    /// <summary>
    /// Gets the <see cref="StackRepository.StackRoot"/>.
    /// </summary>
    public NormalizedPath StackRoot => _defaultWorld.WorldRoot.CurrentDirectory;

    /// <summary>
    /// Gets whether this stack is public.
    /// </summary>
    public bool IsPublic => _isPublic;

    /// <summary>
    /// Gets the default world.
    /// </summary>
    public FakeBuildWorld DefaultWorld => _defaultWorld;

    /// <summary>
    /// Gets the Remotes "bare/" folders. 
    /// </summary>
    public CKliTestHelperExtensions.RemotesFolder Remotes => _remotes;

}
