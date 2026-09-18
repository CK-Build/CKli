using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.Tests;

[TestFixture]
public class WorldIssueTests
{
    sealed class TestIssue : World.Issue
    {
        public TestIssue( bool implicitIssue )
            : base( "Some issue.", CreateBody(), repo: null, implicitIssue )
        {
        }

        static IRenderable CreateBody() => ScreenType.Default.Text( "First body line." )
                                                            .AddBelow( ScreenType.Default.Text( "Second body line." ) );

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor,
                                                         CKliEnv context,
                                                         World world,
                                                         CancellationToken scopeAlive ) => ValueTask.FromResult( true );
    }

    [Test]
    public void Implicit_issue_is_entirely_displayed_in_DarkGray()
    {
        // The DarkGray applies to the whole Collapsable: its "> " and "│ " markers, the title and every body line.
        // (The trailing [GRAY] is the end of line reset to the default style.)
        var display = DebugRenderer.Render( new TestIssue( implicitIssue: true ).ToRenderable( ScreenType.Default ) );
        display.ShouldBe( """
            [DARKGRAY]> Ⓘ Some issue.[GRAY]⮐
            [DARKGRAY]│ First body line.[GRAY]⮐
            [DARKGRAY]│ Second body line.[GRAY]⮐

            """ );

        // A non implicit issue is displayed with the default style.
        var regular = DebugRenderer.Render( new TestIssue( implicitIssue: false ).ToRenderable( ScreenType.Default ) );
        regular.ShouldNotContain( "DARKGRAY" );
        regular.ShouldBe( """
            > ⚙ Some issue.⮐
            │ First body line.⮐
            │ Second body line.⮐

            """ );
    }
}
