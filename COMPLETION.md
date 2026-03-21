# Shell Completion

CKli supports tab completion for PowerShell, bash, zsh, and fish. Completions cover commands, subcommands, flags, options, and their descriptions.

CKli follows the same completion pattern as the [dotnet CLI (.NET 10+)](https://learn.microsoft.com/en-us/dotnet/core/tools/enable-tab-autocomplete): a `completions script <shell>` command generates shell-specific scripts, and a `complete` command handles dynamic completion requests at tab-time.

## Quick Setup

### PowerShell

Add to your profile (`$PROFILE`):

```powershell
ckli completions script pwsh | Out-String | Invoke-Expression
```

`Out-String` is required because `Invoke-Expression` processes pipe input line-by-line and the script block spans multiple lines.

For interactive menu-style completion with descriptions (similar to fish), also add:

```powershell
Set-PSReadLineKeyHandler -Key Tab -Function MenuComplete
```

### Bash

Add to `~/.bashrc`:

```bash
eval "$(ckli completions script bash)"
```

Or generate once for faster startup:

```bash
ckli completions script bash > ~/.ckli-completions.bash
echo 'source ~/.ckli-completions.bash' >> ~/.bashrc
```

### Zsh

Add to `~/.zshrc` (after `compinit`):

```zsh
eval "$(ckli completions script zsh)"
```

If you haven't initialized the zsh completion system, add this before:

```zsh
autoload -Uz compinit && compinit
```

### Fish

```bash
ckli completions script fish > ~/.config/fish/completions/ckli.fish
```

Fish automatically loads files from `~/.config/fish/completions/`. No other setup needed.

## What Gets Completed

| Context | Example | Completions shown |
|---------|---------|-------------------|
| Empty | `ckli ` | All commands and namespaces |
| Partial command | `ckli bu` | `build`, `*build` |
| Namespace | `ckli fix ` | Subcommands: `start`, `info`, ... |
| After command | `ckli build --` | All flags, options, globals |
| After flag | `ckli build --all ` | Remaining flags and options |
| After option + value | `ckli build -b main ` | Remaining flags and options |
| After option (no value) | `ckli build --branch` | Nothing (waiting for value) |

Used flags and options are excluded from subsequent suggestions. Aliases (e.g. `--branch` and `-b`) are excluded together.

Global options (`--help`, `--version`, `--path`, `--ckli-screen`, `--ckli-debug`) are suggested for all commands.

Commands containing shell-reserved characters (e.g. `*build`, `-?`) may get escaped by the shell into `\*build` or `-\?` when completing. This is normal shell behavior and not something CKli controls.

## Shell Feature Support

| Feature | PowerShell | Bash | Zsh | Fish |
|---------|------------|------|-----|------|
| Command names | Yes | Yes | Yes | Yes |
| Descriptions | Yes (tooltip) | No | Yes | Yes |
| Grouping by type | Type icons | No | Yes | No |
| Special char handling (`*build`) | Native | Quoted | Native | Native |

## How It Works

Two intrinsic commands power the completion system:

- `ckli completions script <shell>` outputs a shell-specific script that hooks into the shell's completion mechanism.
- `ckli complete [tokens...]` returns matching completions as three-column TSV (`completion\tdescription\ttype`).

When you press Tab, the shell script calls `ckli complete` with the current command line tokens and parses the output.

Both commands appear in `ckli --help` but are hidden from tab completion suggestions.

### Plugin Commands

Plugin commands are automatically included in completions. When a World loads its plugins (e.g. when you run any command from inside a stack), CKli generates a manifest file at `$Local/<PluginSolutionName>/completion.tsv`. The `complete` command reads this manifest at tab-time without needing to load plugins.

If you add or modify plugins, run any ckli command from the stack directory to regenerate the manifest.
