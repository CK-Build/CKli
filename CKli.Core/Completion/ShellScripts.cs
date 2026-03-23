using System;

namespace CKli.Core.Completion;

/// <summary>
/// Shell-specific completion scripts that call <c>ckli complete</c>.
/// Output format is three-column TSV: completion\tdescription\ttype
/// </summary>
public static class ShellScripts
{
    /// <summary>
    /// Gets the script for the shell.
    /// </summary>
    /// <param name="shell">Supported shells are "bash", "zsh", "fish" and "pwsh".</param>
    /// <returns>The script to register.</returns>
    public static string? Get( string shell ) => shell.ToLowerInvariant() switch
    {
        "bash" => Bash(),
        "zsh" => Zsh(),
        "fish" => Fish(),
        "pwsh" => Pwsh(),
        _ => null
    };

    /// <summary>
    /// Bash: plain text completions only (no description support).
    /// Quotes candidates to handle * prefix commands.
    /// </summary>
    static string Bash() => """
        _ckli_completions() {
            local IFS=$'\n'
            local words=("${COMP_WORDS[@]:1:COMP_CWORD}")
            local completions
            completions=$(ckli complete "${words[@]}" 2>/dev/null)
            COMPREPLY=()
            while IFS=$'\t' read -r comp desc type; do
                if [[ -n "$comp" ]]; then
                    COMPREPLY+=("$(printf '%q' "$comp")")
                fi
            done <<< "$completions"
        }
        complete -o default -F _ckli_completions ckli
        """;

    /// <summary>
    /// Zsh: descriptions + grouping by type (commands, options, flags as separate groups).
    /// </summary>
    static string Zsh() => """
        _ckli_completions() {
            local -a matches globals
            local comp desc type
            while IFS=$'\t' read -r comp desc type; do
                [[ -z "$comp" ]] && continue
                comp="${comp//:/\\:}"
                case "$type" in
                    global) globals+=("$comp:${desc:-global option}") ;;
                    *)      matches+=("$comp:$desc") ;;
                esac
            done < <(ckli complete ${words[2,-1]} 2>/dev/null)
            _describe -V 'commands' matches
            _describe -V 'global options' globals
        }
        compdef _ckli_completions ckli
        """;

    /// <summary>
    /// Fish: tab-separated completion + description (native format). Type column ignored.
    /// </summary>
    static string Fish() => """
        function __ckli_completions
            set -l tokens (commandline -cop)
            set -e tokens[1]
            ckli complete $tokens 2>/dev/null | while read -l line
                # Fish uses tab-separated "completion\tdescription" natively.
                # Strip the third column (type) if present.
                echo $line | string replace -r '\t[^\t]*$' ''
            end
        end
        complete -c ckli -f -a '(__ckli_completions)'
        """;

    /// <summary>
    /// PowerShell: CompletionResult with type mapping and tooltip.
    /// command -> Command, namespace -> Keyword, option/flag/global -> ParameterName.
    /// </summary>
    static string Pwsh() => """
        Register-ArgumentCompleter -Native -CommandName ckli -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)
            $cmdLine = $commandAst.ToString()
            $cmdArgs = if ($cmdLine.Length -gt 5) { $cmdLine.Substring(5) } else { '' }
            $tokens = @(($cmdArgs.Trim() -split '\s+') | Where-Object { $_ })
            $results = & ckli complete @tokens 2>$null
            foreach ($line in $results) {
                $parts = $line -split "`t", 3
                $comp = $parts[0]
                $desc = if ($parts.Length -gt 1) { $parts[1] } else { '' }
                $kind = if ($parts.Length -gt 2) { $parts[2] } else { 'command' }
                $resultType = switch ($kind) {
                    'command'   { 'Command' }
                    'namespace' { 'Keyword' }
                    'option'    { 'ParameterName' }
                    'flag'      { 'ParameterName' }
                    'global'    { 'ParameterName' }
                    'multi'     { 'ParameterName' }
                    default     { 'ParameterValue' }
                }
                if ($comp) {
                    [System.Management.Automation.CompletionResult]::new(
                        $comp, $comp, $resultType, ($desc ? $desc : $comp)
                    )
                }
            }
        }
        """;
}
