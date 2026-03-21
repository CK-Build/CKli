# Writing Plugin Commands

Plugin commands are discovered via attributes on public methods. The method signature determines how the command appears in help and shell completions.

## Attributes

- **`[CommandPath("path")]`** marks a method as a command handler. Multi-word paths create namespace hierarchies (e.g. `"fix start"`).
- **`[Description("...")]`** provides the description shown in help and completions. Can decorate the method (command description) or individual parameters.
- **`[OptionName("--long-name, -s")]`** overrides the option/flag name. First name is the long form, rest are short aliases.

## Parameter Mapping

The method signature determines how parameters appear as arguments, options, and flags:

```csharp
[CommandPath("build")]
[Description("Build packages.")]
public ValueTask<bool> Build(
    IActivityMonitor monitor,                   // Required, not in completions

    // Arguments: required string parameters (positional, not tab-completed)
    [Description("The target to build.")]
    string target,

    // Options: optional string parameters (completed as --name <value>)
    [OptionName("--branch, -b")]
    [Description("Branch to build.")]
    string? branch = null,

    // Multi-valued options: string array (can appear multiple times)
    [OptionName("--exclude")]
    [Description("Exclude pattern.")]
    string[]? exclude = null,

    // Flags: bool with default false (completed as --name)
    [OptionName("--all, -a")]
    [Description("Build everything.")]
    bool all = false,

    [Description("Skip tests.")]
    bool skipTests = false                      // Becomes --skip-tests
)
```

This produces:

```
build --branch    ->  option (takes a value)
build -b          ->  option (alias)
build --exclude   ->  multi (repeatable option)
build --all       ->  flag
build -a          ->  flag (alias)
build --skip-tests -> flag
```

## Documenting Namespaces

When your plugin commands live under a namespace, you can provide a description using `[NamespaceDescription]` on your plugin class:

```csharp
[NamespaceDescription("hosting", "Hosting management commands.")]
[NamespaceDescription("hosting deploy", "Deploy to hosting providers.")]
public class HostingPlugin : PluginBase
{
    [CommandPath("hosting deploy azure")]
    [Description("Deploys to Azure.")]
    public bool DeployAzure(IActivityMonitor monitor, CKliEnv context) { ... }
}
```

- Place the attribute on the plugin class, not on methods.
- Use multiple attributes to describe several namespace levels.
- **First-write-wins**: if two plugins describe the same namespace, the first one loaded takes precedence.
- Namespace descriptions appear in shell completions only (not in `--help`).

## Naming Rules

- Parameters without `[OptionName]` are converted to kebab-case: `skipTests` becomes `--skip-tests`.
- Short aliases are derived from the `[OptionName]` attribute only.
- `bool` parameters **must** default to `false` to be recognized as flags.
- `string?` parameters with `= null` default are options.
- `string[]?` parameters with `= null` default are multi-valued options.

## Return Types

Command methods can return `bool`, `Task<bool>`, or `ValueTask<bool>`.

## Optional Context Parameters

After `IActivityMonitor`, you can optionally accept:

```csharp
CKliEnv? context = null,              // Access to screen, world, repos
CommandLineArguments? cmdLine = null,  // Raw command line (replaces typed parameters)
```

If `CommandLineArguments` is used, you handle parsing yourself and no typed parameters are generated for completions.

## Further Reading

For guidance on designing CLI commands, options, and naming conventions:

- [GNU Standards for Command Line Interfaces](https://www.gnu.org/prep/standards/html_node/Command_002dLine-Interfaces.html) - POSIX conventions, short/long options, standard options
- [Command Line Interface Guidelines](https://clig.dev/) - design philosophy for user-friendly, composable CLI programs
- [System.CommandLine design guidance](https://learn.microsoft.com/en-us/dotnet/standard/commandline/design-guidance) - .NET-specific conventions for commands, options, naming, and verbosity
