# CKli.Plugins.Core

**CKli.Plugins.Core** is the small, stable contract shared by every `<WorldName>-Plugins` solution and the CKli host.
It defines *how* a set of plugin assemblies is turned into a ready-to-run `IPluginFactory`, without pulling in
anything else from CKli.Core except the domain types plugins genuinely need (`World`, `PluginBase`,
`PrimaryPluginContext`, `Command`, `CommandLineArguments`, ...).

It has a single project reference: `CKli.Core`. Nothing references it back — CKli.Core knows about the plugin
*abstractions* (`IPluginFactory`, `IPluginTypeInfo`, `PluginInfo`, `PluginCollectorContext`, `PluginCollection`) but
never about `CKli.Plugins.Core` itself. The dependency only runs one way, from the generated/compiled plugins
solution down to this assembly.

## Why a separate assembly

A World's plugins live in their own solution (`<WorldName>-Plugins`, generated under `.PublicStack/CKli-Plugins` or
similar — see CKli.Core's README, "Plugin discovery and loading"). That solution's aggregator project
(`CKli.Plugins.csproj`) references every installed plugin plus `CKli.Plugins.Core`, and nothing else from the host:

```xml
<ItemGroup>
  <PackageReference Include="CKli.Plugins.Core" />
  <PackageReference Include="CKli.SomeStandard.Plugin" />
  <ProjectReference Include="..\MyPlugin\MyPlugin.csproj" />
</ItemGroup>
```

Individual plugin projects (source-based, created by `ckli plugin create`) reference only `CKli.Plugins.Core` too:

```xml
<ItemGroup>
  <PackageReference Include="CKli.Plugins.Core" />
</ItemGroup>
```

That is deliberate:

- **Plugin authors compile against a tiny, versioned surface**, not the whole host. `CKli.Plugins.Core` ships as a
  NuGet package (`GeneratePackageOnBuild`) whose version is kept in lockstep with the CKli tool version
  (`PluginMachinery.CheckCKliPluginsCoreVersion` rewrites `Directory.Packages.props` whenever they drift, and bumps
  it triggers a recompile).
- **It decouples "how plugins are discovered/wired" from "what a plugin can do".** The domain types
  (`World`, `Repo`, `PluginBase`, ...) live in CKli.Core; the discovery/collection *machinery* — attribute
  conventions, dependency-graph resolution, command adapter shape — lives here. A plugin project never needs to
  reference the parts of CKli.Core that implement Git/GitHosting/Stack orchestration.
- **It is the contract a code generator must honor.** CKli.Core's README describes the workflow: "discover once
  (reflection), then compile, then a regular load is just an `Assembly.Load` and a call to a single static
  initialization function". `ReflectionPluginCollector` is the reflection-based discovery engine that runs once (or
  whenever plugin configuration changes); its `Factory.GenerateCode()` emits a `CompiledPlugins` static class whose
  shape — `PluginInfo[]`, `PluginTypeInfo` instances, `Cmd_<path> : PluginCommand` classes — is entirely described by
  the types in this assembly. Whether the running `IPluginFactory` was built by reflection or by loading pre-compiled
  code, `CKli.Loader.PluginLoadContext` only ever needs to resolve two static entry points that both return an
  `IPluginFactory`:

  | Entry point | Present when | Shape |
  |---|---|---|
  | `CKli.Plugins.CompiledPlugins.Get(PluginCollectorContext)` | `plugin compile --mode Debug\|Release` was used | Returns `null` if the `<Plugins>` configuration signature changed (forces a fallback to reflection / recompile) |
  | `CKli.Plugins.Plugins.Register(PluginCollectorContext)` | Always (the generated `CKli.Plugins.cs` in the plugins solution) | Calls `PluginCollector.Create(ctx).BuildPluginFactory([...primary plugin types...])` |

  Both paths bottom out in this assembly. `CKli.Loader.PluginLoadContext.Initialize` even does
  `GC.KeepAlive(typeof(CKli.Plugins.PluginCollector))` specifically so this small assembly is resolved from the
  *default* `AssemblyLoadContext` rather than trimmed and reloaded inside the collectible plugin context — a
  `MissingMethodException` otherwise results, because the compiled/reflected plugin code and the host must share
  the very same `PluginCommand`/`IPluginTypeInfo`/`MethodAsyncReturn` types.

## Discovery → collection → compilation, at a glance

```
 <WorldName>-Plugins solution                    CKli.Loader.PluginLoadContext
 ┌───────────────────────────────┐                ┌─────────────────────────────┐
 │ CKli.Plugins.cs (generated)    │  Assembly.Load │ Looks for, in order:        │
 │  Plugins.Register(ctx) ──────► │ ─────────────► │  1. CompiledPlugins.Get(ctx)│
 │   PluginCollector.Create(ctx)  │                │  2. Plugins.Register(ctx)   │
 │    .BuildPluginFactory([...])  │                └─────────────────────────────┘
 └───────────────────────────────┘
              │
              ▼
   ReflectionPluginCollector          (this assembly, reflection-based discovery)
    - reflects over each primary plugin's assembly
    - builds a dependency graph of PluginBase-derived types
    - reflects [CommandPath] methods into PluginCommand instances
              │
              ▼
        IPluginFactory  ──Create(monitor, world)──►  PluginCollection (PluginCollectionImpl)
              │
              └──GenerateCode()──►  "CKli.CompiledPlugins.cs" (checked in, compiled by `ckli plugin compile`)
```

`PluginCollectorContext` (CKli.Core) carries the `WorldName`, the `<Plugins>` XML configuration per plugin name, and
a `Signature` (SHA1 of the CKli version + the set of enabled/disabled plugin names) used to invalidate compiled code
when the World's plugin configuration changes without needing a rebuild trigger from anywhere else.

## Key types

| Type | Role |
|---|---|
| `IPluginCollector` | The contract: `BuildPluginFactory(ReadOnlySpan<Type> defaultPrimaryPlugins) → IPluginFactory`. |
| `PluginCollector` (static) | Factory for the collector currently in use — today always `new ReflectionPluginCollector(context)`. Hides the concrete implementation from generated code. |
| `ReflectionPluginCollector` | Reflects over plugin assemblies to discover types, resolve constructor-injection dependencies, and collect commands. Implements `IPluginCollector`. Split across 3 files (partial class). |
| `ReflectionPluginCollector.PluginType` (nested) | Implements `IPluginTypeInfo` for a reflection-discovered type; knows how to `Instantiate(world, instantiated)` via its `ConstructorInfo`. |
| `ReflectionPluginCollector.Factory` (nested) | Implements `IPluginFactory`. `Create` instantiates the plugin graph via reflection; `GenerateCode` emits the compiled-plugins source. |
| `PluginTypeInfo` | Plain, immutable `IPluginTypeInfo` implementation used by **generated** code (mirrors what `PluginType` computes at reflection time). |
| `CommandCollector` | Reflects `[CommandPath]` methods off a plugin type, validates their parameter shape, and builds `ReflectionPluginCommand` instances plus the `CommandNamespace`. |
| `PluginCommand` (in `CKli.Core` namespace, defined here) | Abstract base (`Command` subclass) shared by `ReflectionPluginCommand` and every generated `Cmd_<path>` class. |
| `ReflectionPluginCommand` | Reflection-based `PluginCommand`: invokes the target method via `MethodInfo.Invoke`. |
| `PluginCollectionImpl` | The `PluginCollection` returned to the host: binds each `PluginCommand` to its live plugin instance. |
| `MethodAsyncReturn` | `None` \| `ValueTask` \| `Task` — which of the three allowed return shapes (`bool`, `ValueTask<bool>`, `Task<bool>`) a command method uses. |

## `IPluginCollector` / `PluginCollector`

```csharp
public interface IPluginCollector
{
    IPluginFactory BuildPluginFactory( ReadOnlySpan<Type> defaultPrimaryPlugins );
}

public static class PluginCollector
{
    public static IPluginCollector Create( PluginCollectorContext context )
        => new ReflectionPluginCollector( context );
}
```

`PluginCollector.Create(...).BuildPluginFactory([...])` is exactly the line the generated `CKli.Plugins.cs` file
emits (see `PluginMachinery.DefaultCKliPluginsFile` in CKli.Core), with `[...]` filled in with every primary plugin
`Type` known to the plugins solution — `ckli plugin create`/`ckli plugin add` maintain this array (the
`<AutoSection>` marker in the generated file) as plugins are added or removed. `PluginCollector` exists only so that
the generated call site never has to spell out `ReflectionPluginCollector` — swapping the concrete collector
implementation (should one ever be added) is a one-line change here.

## `ReflectionPluginCollector`: discovery and the dependency graph

`BuildPluginFactory` iterates the given primary plugin types. For each one it:

1. Validates the owning assembly is named `CKli.XXX.Plugin` (`PluginMachinery.EnsureFullPluginName`) and that the
   primary type's namespace matches the assembly name.
2. Resolves the plugin's `PluginStatus` from `PluginCollectorContext.PluginsConfiguration` — `Available`,
   `DisabledByConfiguration`, or `DisabledByMissingConfiguration` if no `<PluginName>` element exists in `<Plugins>`.
3. Walks every exported, non-abstract, non-generic type in the assembly that derives from `PluginBase`
   (`AddPluginType`), and records an `InitialReg` for it: the type must be a **non-nested sealed class with exactly
   one public constructor**. Constructor parameters must each be `World`, `PrimaryPluginContext`, or another
   *sealed* `PluginBase`-derived type — nothing else. A type is a **primary** plugin if its constructor takes a
   `PrimaryPluginContext`; a **support** plugin takes `World` (optionally) instead. A type cannot take both.

`Build()` then only materializes `PluginType` instances (and only *those* end up instantiated) for **primary**
plugins and whatever support plugins they transitively require — `RegisterPluginType` recurses over constructor
parameters, using a `null` sentinel in the `plugins` dictionary to detect dependency cycles
(`Throw.CKException` on a cycle or on a reference to an unregistered plugin type). Each resolved type gets an
`ActivationIndex` into a flat activation list; if a *required* (non-optional) dependency is disabled, the status
propagates as `DisabledByDependency` and the type gets `ActivationIndex = -1` (never instantiated, but its commands
still surface — disabled — in the command namespace).

While walking each type, `RegisterPluginType` also reflects its public methods for `[CommandPath]` and hands each
one to `CommandCollector.Add`.

`Build()` finally returns a `Factory` bundling: the immutable `PluginInfo` array, the activation list, the
`PluginCollectorContext`, the built `CommandNamespace`, and the flat `PluginCommand` list.

## `ReflectionPluginCollector.PluginType`

The reflection-time `IPluginTypeInfo`. Beyond the `IPluginTypeInfo` members (`Plugin`, `TypeName`, `IsPrimary`,
`Status`, `ActivationIndex`), it holds what's needed to actually build an instance:

```csharp
internal object Instantiate( World world, object[] instantiated )
```

For each constructor parameter it either injects `world`, wraps a fresh `PrimaryPluginContext(pluginInfo, xmlConfig, world)`,
or pulls an already-instantiated dependency out of `instantiated[]` by its `ActivationIndex`, then calls the cached
`ConstructorInfo.Invoke`.

## `ReflectionPluginCollector.Factory`: reflection execution vs. code generation

`Factory` is the `IPluginFactory` handed back to `CKli.Loader`. It has `CompileMode = PluginCompileMode.None` and
supports two entirely different consumers of the same collected data:

- **`Create(monitor, world)`** — the reflection path. Instantiates every `PluginType` in activation order (later
  entries can depend on earlier ones — the graph is built so indices only ever point backwards) into an
  `object[]`, then calls `PluginCollectionImpl.CreateAndBindCommands(...)`.
- **`GenerateCode()`** — emits the C# source of a `CKli.Plugins.CompiledPlugins` static class (this is what
  `ckli plugin compile --mode Debug|Release` writes to `CKli.CompiledPlugins.cs`, then builds). The generated code
  is a **literal transcription** of what reflection just computed, with no reflection left at run time:
  - a `_configSignature` byte array captured from `PluginCollectorContext.Signature`, so a stale compiled DLL is
    detected and rejected (`Get` returns `null`) if the World's plugin configuration changed;
  - a `PluginInfo[]` / `PluginTypeInfo[]` literal mirroring the reflected `PluginInfo`/`PluginType` graph
    (using the *public* `PluginTypeInfo` class from this assembly, not the internal `ReflectionPluginCollector.PluginType`);
  - one `new <PluginTypeName>( ... )` expression per activation-list entry, with dependencies wired by direct object
    reference instead of an index lookup;
  - one `sealed class Cmd_<path> : PluginCommand` per collected command (see below).

> **Touching a command means regenerating this file — never hand-editing it.** The `_configSignature` guard
> above only detects a changed `<Plugins>` *configuration*; it says nothing about the command *methods*. So a
> `[CommandPath]` method whose flags, options, parameters or return type changed leaves a generated file that is
> stale and still accepted. Worse, such a file usually still **compiles**: every flag is a `bool` and every
> command parameter has a default, so a shifted `Flags[i]` index or a dropped trailing argument binds silently
> to the wrong parameter. Delete `CKli.CompiledPlugins.cs` and run
> [`ckli plugin compile`](../README.md#plugin-compile---mode-nonedebugrelease).

## `CommandCollector`: from `[CommandPath]` methods to `PluginCommand`

`CommandCollector.Add(typeInfo, method, commandPath, attributes)` is called once per `[CommandPath("...")]`-decorated
public method found by `ReflectionPluginCollector`. It normalizes and validates the path
(`Command.IsValidCommandPath`, and it must not collide with an intrinsic CKli command), then walks the method's
`ParameterInfo[]` to enforce the convention documented in CKli.Core's README ("Plugin commands"):

1. **Parameter 0** must be `IActivityMonitor`.
2. **Return type** must be `bool`, `ValueTask<bool>`, or `Task<bool>` → recorded as `MethodAsyncReturn`.
3. Parameters 1–2 may optionally be `CKliEnv` and/or `CommandLineArguments`, in that order. If `CommandLineArguments`
   is present, it must be the *last* parameter — the method takes over argument parsing entirely.
4. Otherwise, remaining parameters are consumed in three strict phases:

   | Phase | Accepts | Stops at |
   |---|---|---|
   | Arguments | `string` without a default value | first optional `string`, `string[]`, or `bool` |
   | Options | `string` (single) or `string[]` (multiple), both requiring a default/being optional | first `bool` |
   | Flags | `bool` (must default to `false`, never `true`) | end of parameter list |

   Any parameter out of order or of an unsupported type throws (`Throw.CKException`) with a descriptive message —
   including a specific error when `CKliEnv` shows up anywhere but position 1.

Each option/flag parameter's exposed name(s) default to the kebab-case of the parameter name (`--branch-name`) unless
overridden by `[OptionName("--branch,-b")]` (first name long `--...`, subsequent ones short `-x`); each
parameter/method can carry a `[Description("...")]`.

The result is wrapped into a `ReflectionPluginCommand` and added both to `CommandCollector.PluginCommands` (the flat
list `Factory` needs) and to a `CommandNamespaceBuilder` (`BuildCommands()` produces the `CommandNamespace` used for
command-path lookup/dispatch and help rendering).

## `PluginCommand` / `ReflectionPluginCommand`

`PluginCommand` (abstract, in `CKli.Core`) extends `Command` and stores everything `CommandCollector` computed:
method name, `MethodAsyncReturn`, and the parameter-index bookkeeping needed to reassemble a call
(`IdxCKliEnvParameter`, `IdxCmdLineParameter`, plus the inherited `Arguments`/`Options`/`Flags`). It exposes a mutable
internal `_instance` field — set once by `PluginCollectionImpl.CreateAndBindCommands` — that concrete subclasses read
through the protected `Instance` property.

`ReflectionPluginCommand` is the only concrete subclass defined here (generated code emits its own `Cmd_<path>`
subclasses instead). Its `HandleCommandAsync` rebuilds the method's `object?[]` argument array at every invocation —
positioning `monitor`, `context`, `cmdLine` or the parsed arguments/options/flags according to the recorded
indices — then dispatches on `ReturnType` and calls `MethodInfo.Invoke`:

```csharp
switch( ReturnType )
{
    case MethodAsyncReturn.None:     return ValueTask.FromResult( (bool)_method.Invoke( _instance, args )! );
    case MethodAsyncReturn.ValueTask: return (ValueTask<bool>)_method.Invoke( _instance, args )!;
    default:                          return new ValueTask<bool>( (Task<bool>)_method.Invoke( Instance, args )! );
}
```

When `IdxCmdLineParameter == -1` (the common case — plain arguments/options/flags), it pulls values off the
`CommandLineArguments` itself (`EatArgument()`, `EatSingleOption`/`EatMultipleOption`, `EatFlag`) in the exact order
`CommandCollector` recorded, and calls `cmdLine.Close(monitor)` to fail the command if unconsumed tokens remain. The
code `Factory.GenerateCommand` emits does the identical sequence, just unrolled into local variables (`a0`, `o0`,
`f0`, ...) instead of a shared loop, and calls the plugin method directly instead of through `MethodInfo`.

## `PluginTypeInfo`

A minimal, public, immutable `IPluginTypeInfo`:

```csharp
public sealed class PluginTypeInfo : IPluginTypeInfo
{
    public PluginTypeInfo( PluginInfo plugin, string typeName, bool isPrimary, int status, int activationIndex );
    public PluginInfo Plugin { get; }
    public string TypeName { get; }
    public bool IsPrimary { get; }
    public PluginStatus Status { get; }
    public int ActivationIndex { get; }
}
```

It exists purely so generated code can materialize `IPluginTypeInfo` instances without depending on the internal
`ReflectionPluginCollector.PluginType` (which also carries reflection-only state such as the `ConstructorInfo`).
Both classes describe exactly the same data; only their construction path differs.

## `PluginCollectionImpl`

```csharp
public sealed class PluginCollectionImpl : PluginCollection
{
    public static PluginCollectionImpl CreateAndBindCommands(
        object[] instantiated,
        IReadOnlyCollection<PluginInfo> plugins,
        CommandNamespace commands,
        IEnumerable<PluginCommand> pluginCommands );
}
```

The only non-trivial thing `CreateAndBindCommands` does is bind each `PluginCommand` to its live plugin instance:
`c._instance = instantiated[c.PluginTypeInfo.ActivationIndex]` (skipped, leaving `_instance == null`, for commands
whose owning type is disabled — `Command.PluginTypeInfo` / `Command.IsDisabled` on the base `Command` class is what
keeps CKli from ever dispatching to them). Everything else — `PluginCollection`'s own `Dispose`-forwarding to
`IDisposable` plugin instances — is inherited from `CKli.Core`.

## `MethodAsyncReturn`

```csharp
public enum MethodAsyncReturn { None, ValueTask, Task }
```

The only three return shapes a `[CommandPath]` method may declare (`bool`, `ValueTask<bool>`, `Task<bool>`
respectively). It exists as its own type — rather than being inferred ad hoc at each call site — because both
`CommandCollector` (reflection time) and `Factory.GenerateCode()` (code-gen time) need to agree on the exact same
three-way switch used later by `ReflectionPluginCommand.HandleCommandAsync` and every generated `Cmd_<path>`.

## What this means for a plugin author

A plugin project needs nothing beyond a `PackageReference` (or `ProjectReference`, for the standard plugins built
in-tree) to `CKli.Plugins.Core` — that pulls `CKli.Core` transitively for the domain types. There is nothing to call
into this assembly directly: a plugin author writes `sealed class` types deriving from `PluginBase` /
`PrimaryPluginBase` / `PrimaryRepoPlugin<T>` / `RepoPluginBase<T>` (see CKli.Core's README) with `[CommandPath]`
methods following the parameter convention above; `ReflectionPluginCollector` (via the generated `Plugins.Register`
entry point) does the rest — discovery, dependency wiring, and command adaptation — whether the World runs in
`--mode None` (pure reflection, this assembly doing all the work at every load) or `Debug`/`Release`
(`Factory.GenerateCode()` having already turned that same reflected graph into ahead-of-time C#).
