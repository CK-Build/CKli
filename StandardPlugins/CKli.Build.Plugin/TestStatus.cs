using CKli.Core;
using System;

namespace CKli.Build.Plugin;

/// <summary>
/// The tests of a repository (or of a release) once a roadmap is done: this is the "tests" column of the
/// roadmap display (see <see cref="Roadmap.BuildSolution.TestStatus"/>).
/// </summary>
public enum TestStatus
{
    /// <summary>
    /// The tests have already run successfully on the content: a repository that is not built or a built one
    /// whose build doesn't change its content (no dependency update: a code change only, for instance).
    /// </summary>
    Tested,

    /// <summary>
    /// The command runs the tests: a built repository whose content has never been tested (any of them with
    /// "--force-tests") unless "--skip-tests", an untested repository that is not built with "--with-tests"
    /// (or any of them with "--force-tests") or a release to publish.
    /// </summary>
    WillTest,

    /// <summary>
    /// Not built and never tested: the command doesn't test it ("--with-tests" does).
    /// </summary>
    Untested,

    /// <summary>
    /// Built with "--skip-tests": the produced version will be untested.
    /// </summary>
    Skipped
}

/// <summary>
/// Extends <see cref="TestStatus"/>.
/// </summary>
public static class TestStatusExtensions
{
    /// <summary>
    /// Renders the one-character glyph of the status: a boxed "T" (black on cyan) for <see cref="TestStatus.WillTest"/>,
    /// "✓", "?" and "✗" for the others.
    /// </summary>
    /// <param name="status">This status.</param>
    /// <param name="screen">The screen type.</param>
    /// <returns>The renderable.</returns>
    public static IRenderable ToRenderable( this TestStatus status, ScreenType screen )
    {
        return status switch
        {
            TestStatus.Tested => screen.Text( "✓", ConsoleColor.DarkGreen ),
            TestStatus.WillTest => screen.Text( "T", foreColor: ConsoleColor.Black, ConsoleColor.Cyan ),
            TestStatus.Untested => screen.Text( "?", ConsoleColor.DarkYellow ),
            _ => screen.Text( "✗", ConsoleColor.Red )
        };
    }
}
