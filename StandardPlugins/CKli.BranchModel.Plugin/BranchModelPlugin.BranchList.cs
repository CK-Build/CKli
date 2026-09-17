using CK.Core;
using CKli.Core;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Displays the opened branches of the World.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <returns>Always true.</returns>
    [Description( "Displays the opened branches of the World and how each one is linked to its parent." )]
    [CommandPath( "branch list" )]
    public bool BranchList( IActivityMonitor monitor, CKliEnv context )
    {
        // The branch model is World global: this displays the BranchNamespace, not the Git branches of the
        // repositories (that is what "ckli issue" reports).
        var screen = context.Screen;
        var s = screen.ScreenType;
        screen.Display( s.Text( $"Opened branches of '{World.Name}':" )! );
        // One Display per branch: a multi line TextBlock trims each of its lines, so the indentation of the
        // tree is a Box margin, never spaces in the text.
        foreach( var (b, depth) in _namespace.GetDisplayBranches() )
        {
            var line = b.IsRoot
                        ? s.Text( b.Name )!
                        : s.Text( $"{b.LinkType.ToCodeString()} {b.Name}" )!;
            screen.Display( line.Box( marginLeft: 2 * depth ) );
        }
        // The codes are compact on purpose (they align in a column and read as a propagation gradient) but
        // they are display only: this legend spells the names that the configuration and "--link" take.
        screen.Display( s.Text( "" )! );
        screen.Display( s.Text( "Links:" )! );
        foreach( var link in new[] { BranchLinkType.Manual, BranchLinkType.Release, BranchLinkType.CI, BranchLinkType.Full } )
        {
            screen.Display( s.Text( $"{link.ToCodeString()} {link}{LinkDescription( link )}" )!.Box( marginLeft: 2 ) );
        }
        return true;

        static string LinkDescription( BranchLinkType link ) => link switch
        {
            BranchLinkType.Manual => ": nothing is propagated from the parent.",
            BranchLinkType.Release => ": a version built on the parent is merged.",
            BranchLinkType.CI => " (the default): any commit built on the parent is merged.",
            _ => """: every commit of the parent's "dev/" branch is merged."""
        };
    }

}
