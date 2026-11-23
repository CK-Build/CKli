using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core;

sealed partial class World
{
    /// <summary>
    /// Adds a manual issue for each Git setting that checks out a newline that is not the one of the platform.
    /// A setting of the machine gives one issue whatever the number of repositories, a setting of a
    /// repository gives an issue for this repository. No setting is ever changed.
    /// </summary>
    internal void AddGitNewLineIssues( IActivityMonitor monitor,
                                       ScreenType screenType,
                                       GitRepository stackRepository,
                                       IReadOnlyList<Repo> repos,
                                       List<Issue> issues )
    {
        bool isWindows = OperatingSystem.IsWindows();
        var machine = new Dictionary<(string, string, ConfigurationLevel), (GitNewLineProblem Problem, int Count)>();
        foreach( var (git, repo) in repos.Select( r => (r.GitRepository, (Repo?)r) ).Prepend( (stackRepository, null) ) )
        {
            var p = GitNewLineSettings.Check( monitor, git.Repository, isWindows );
            if( p == null ) continue;
            if( p.Level is ConfigurationLevel.Local or ConfigurationLevel.Worktree )
            {
                issues.Add( Create( p, $"""To fix: git -C "{git.WorkingFolder}" config --local --unset {p.Key}""", repo ) );
            }
            else
            {
                var key = (p.Key, p.Value, p.Level);
                machine[key] = (p, machine.GetValueOrDefault( key ).Count + 1);
            }
        }
        foreach( var (p, count) in machine.Values )
        {
            var fix = p.Level switch
            {
                ConfigurationLevel.Global => $"To fix: git config --global --unset {p.Key}",
                ConfigurationLevel.System => $"To fix: git config --system --unset {p.Key}",
                _ => $"To fix: remove '{p.Key}' from the {p.Level} configuration file."
            };
            issues.Add( Create( p, $"Concerns {count} repositories.{Environment.NewLine}{fix}", null ) );
        }

        Issue Create( GitNewLineProblem p, string body, Repo? repo )
        {
            static string Name( string newLine ) => newLine == "\n" ? "LF" : "CRLF";
            var title = $"Git checks out {Name( p.WorkingNewLine )} line endings: {Name( GitNewLineSettings.GetExpectedNewLine( isWindows ) )} is expected on this platform ({p.Level} '{p.Key} = {p.Value}').";
            body += Environment.NewLine + "Files already checked out keep their line endings: check the repositories out again afterwards.";
            return Issue.CreateManual( title, screenType.Text( body ), repo );
        }
    }
}
