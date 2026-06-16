using CK.Core;
using CKli.Core;
using System;
using System.Globalization;

namespace CKli.HotZone.Plugin;

/// <summary>
/// Helper used to display the first columns of a build roadmap (the index and the rank range symbols).
/// </summary>
public ref struct BuildIndexAndRankDisplayState
{
    readonly int _buildIndexLen;
    readonly ScreenType _screen;
    readonly int _nodeCount;
    readonly Func<int, int> _nodeRank;
    int _currentNode;
    int _buildIndex;
    int _prevRank;

    /// <summary>
    /// Initializes a new display state.
    /// </summary>
    /// <param name="screen">The scree type.</param>
    /// <param name="maxBuildIndex">The number of solutions to build.</param>
    /// <param name="nodeCount">The total number of solutions.</param>
    /// <param name="nodeRank">Rank provider for 0-based indexed node.</param>
    public BuildIndexAndRankDisplayState( ScreenType screen, int maxBuildIndex, int nodeCount, Func<int,int> nodeRank )
    {
        Throw.CheckArgument( maxBuildIndex >= 0 );
        _buildIndexLen = maxBuildIndex switch
        {
            0 => 0,
            < 10 => 1,
            < 100 => 2,
            < 1000 => 3,
            _ => 4
        };
        _screen = screen;
        _nodeCount = nodeCount;
        _nodeRank = nodeRank;
        _prevRank = -1;
        _currentNode = -1;
    }

    /// <summary>
    /// Gets the screen type.
    /// </summary>
    public ScreenType Screen => _screen;

    /// <summary>
    /// Forward this state to the next solution and returns its line header.
    /// </summary>
    /// <param name="isBuildable">
    /// Whether the next solution must be built: its one-based build index
    /// must appear before the rank range symbols.
    /// </param>
    /// <returns>The renderable.</returns>
    public IRenderable MoveNext( bool isBuildable, int marginRight )
    {
        Throw.CheckState( ++_currentNode < _nodeCount );
        int buildIndex = isBuildable ? ++_buildIndex : 0;
        int rank = _nodeRank( _currentNode );
        int nextNode = _currentNode + 1;
        var begOfRank = _prevRank < rank;
        var endOfRank = nextNode == _nodeCount || _nodeRank( nextNode ) > rank;
        var cR = begOfRank
                    ? (endOfRank ? "-" : "╓")
                    : (endOfRank ? "╙" : "║");
        _prevRank = rank;
        return RenderBuildIndexAndRank( _screen, buildIndex, _buildIndexLen, cR, marginRight );

        static IRenderable RenderBuildIndexAndRank( ScreenType screen, int buildIndex, int buildIndexLen, string cRank, int marginRight )
        {
            if( buildIndex > 0 )
            {
                Throw.DebugAssert( buildIndexLen > 0 );
                var num = buildIndex.ToString( CultureInfo.InvariantCulture );
                return screen.Text( num.PadRight( buildIndexLen + 1 ) + cRank ).Box( TextStyle.Default, marginRight: marginRight );
            }
            var d = screen.Text( cRank );
            return buildIndexLen > 0
                    ? d.Box( TextStyle.Default, paddingLeft: buildIndexLen + 1, marginRight: marginRight )
                    : d.Box( TextStyle.Default, marginRight: marginRight );
        }

    }

}
