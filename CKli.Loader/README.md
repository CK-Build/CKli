# CKli.Loader

**CKli.Loader** is a tiny, single-purpose assembly: it provides the concrete implementation of
`World.PluginLoader`, the delegate that loads a compiled (or reflection-discovered) `CKli.Plugins.dll`
into an isolated, unloadable `AssemblyLoadContext`.

It exists as its own project — rather than living in `CKli.Core` or `CKli.Plugins.Core` — because it sits
at a very specific boundary in the plugin architecture:

- `CKli.Core` defines the *contract* (`World.PluginLoader`, `IPluginFactory`) but must stay agnostic of
  *how* plugins are actually loaded, so that tests and alternate hosts can plug in their own strategy
  (see `Tests/CKli.Core.Tests/TestHelpers/TestEnv.cs`, which wires the very same loader).
- `CKli.Plugins.Core` defines the types shared between the host and the generated/compiled plugin code
  (`PluginCollectorContext`, `PluginCollector`, `IPluginFactory`'s dependencies) and must be visible from
  *both* the default load context and the plugins' collectible one.
- `CKli.Loader` is the only assembly that actually touches `System.Runtime.Loader.AssemblyLoadContext`.
  Keeping it minimal (it has a single `ProjectReference` to `CKli.Plugins.Core`) limits the set of
  assemblies that must be carefully reasoned about when it comes to assembly resolution and unloading.

The host wires it once, imperatively, at startup:

```csharp
// CKli/Program.cs
World.PluginLoader = CKli.Loader.PluginLoadContext.Load;
```

## The problem it solves

CKli discovers, compiles and loads plugins from the `<WorldName>-Plugins` solution of a Stack (see
CKli.Core's README, "Plugin discovery and loading"). Plugins can be added, removed, upgraded or
recompiled at any time, and a single CKli process may need to load a *different* set of plugins for a
different World during its lifetime (interactive mode, tests). This requires:

- **Isolation** — a plugin assembly (and whatever private dependencies it drags in) must not collide with
  assemblies already loaded by the host, nor with another World's plugin assemblies loaded earlier in the
  same process.
- **Unloading** — once a World's plugins are no longer needed, the assemblies must be collectible so the
  `AssemblyLoadContext` (and everything loaded into it) can be garbage-collected.

The .NET answer to both is a collectible `AssemblyLoadContext`. `PluginLoadContext` is exactly that,
specialized for CKli's plugin-loading protocol.

## Key type: `PluginLoadContext`

```csharp
public sealed class PluginLoadContext : AssemblyLoadContext, IPluginFactory
```

It is constructed with `isCollectible: true` and named after the `WorldName`, so multiple Worlds loaded in
the same process each get their own independent context (visible as `AssemblyLoadContext.All` entries,
useful when debugging).

It also *implements* `IPluginFactory` itself — it forwards `Create`, `CompileMode` and `GenerateCode` to
the real `IPluginFactory` instance it discovers inside the loaded plugin assembly (`_pluginFactory`). This
lets `PluginMachinery` (in `CKli.Core`) hold a single `IPluginFactory` reference that, when disposed, both
releases the inner factory *and* unloads the `AssemblyLoadContext` — see `Dispose()`:

```csharp
void IDisposable.Dispose()
{
    _pluginFactory?.Dispose();
    _pluginFactory = null;
    Unload();
}
```

### `Load(...)`: the entry point

`World.PluginLoader` is a `PluginLoaderFunction` delegate; `PluginLoadContext.Load` is its implementation:

```csharp
static IPluginFactory? Load( IActivityMonitor monitor,
                              NormalizedPath dllPath,
                              PluginCollectorContext context,
                              out bool recoverableError,
                              out WeakReference? loader )
```

Called by `PluginMachinery` with the path to the compiled `CKli.Plugins.dll` for a World. It:

1. Lazily calls `Initialize()` the first time it runs (see below).
2. Returns `null` with `recoverableError = true` if the dll doesn't exist yet (nothing compiled).
3. Creates a new `PluginLoadContext`, exposes it through a `WeakReference` (`loader` out parameter) so the
   caller can later verify the context was actually collected after `Unload()`.
4. Calls `DoLoad`, which loads the assembly and locates the plugin entry point (see below).
5. On any exception during `DoLoad`, treats it as a **recoverable error**: it logs and disposes the
   context. The caller (`PluginMachinery`) reacts by deleting the generated `CKli.CompiledPlugins.cs` and
   falling back to reflection-based discovery, or ultimately runs CKli in a degraded
   "working without plugins" mode.

### `DoLoad`: compiled-first, reflection-fallback

Once the assembly is loaded via `LoadFromAssemblyPath`, `DoLoad` looks, in order, for:

1. A static `CKli.Plugins.CompiledPlugins.Get(PluginCollectorContext)` method — the fast path, present when
   `CKli.CompiledPlugins.cs` (source-generated per World) was successfully compiled. This is the normal,
   reflection-free steady state described in CKli.Core's "Plugin discovery and loading".
2. A static `CKli.Plugins.Plugins.Register(PluginCollectorContext)` method — the reflection-based fallback,
   always present, used when compiled code is missing, stale, or its signature no longer matches (e.g. the
   World's plugin configuration changed shape).

Both are located and invoked purely via `System.Reflection` (`GetType` / `GetMethod` / `Invoke`) — there is
no compile-time reference from `CKli.Loader` to the generated plugin assembly, which is precisely what
lets an arbitrary, per-World assembly be loaded generically.

### Assembly resolution: `Load(AssemblyName)`

`PluginLoadContext` overrides `AssemblyLoadContext.Load(AssemblyName)` to decide, for every assembly the
plugin code references, where it comes from:

```csharp
protected override Assembly? Load( AssemblyName assemblyName )
{
    if( !_assemblies.TryGetValue( assemblyName.Name, out var a ) )
    {
        var p = $"{_runFolder}/{assemblyName.Name}.dll";
        a = File.Exists( p ) ? LoadFromAssemblyPath( p ) : Assembly.Load( assemblyName );
    }
    return a;
}
```

- If the assembly is already known from the host's default context (see `Initialize()` below), that exact
  instance is reused — this is what keeps shared types (like `CKli.Plugins.Core`'s) identical between the
  host and the plugin context, avoiding `MissingMethodException` / type-identity mismatches.
- Otherwise it looks for a `.dll` copied next to the plugin assembly (`_runFolder`, the directory of the
  loaded `dllPath`) — this is why the plugin project (`CKli.Plugins.csproj`) must set
  `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`: any dependency private to the plugins
  (not already available in the default context) must be copied there to be discoverable.
- As a last resort it falls back to `Assembly.Load(assemblyName)` (the default context), logging an `Info`
  message pointing at the likely misconfiguration.

### `Initialize()`: snapshotting the host's assemblies

```csharp
public static void Initialize( AssemblyLoadContext? supplementary = null )
```

Called once (guarded by `Throw.CheckState`) before the first `Load`. It builds a static
`Dictionary<string, Assembly>` snapshot of every assembly name currently loaded in
`AssemblyLoadContext.Default` (plus an optional `supplementary` context, kept only because some previous
test-runner versions loaded the host assemblies elsewhere). Every subsequent `PluginLoadContext` instance
consults this same dictionary, so a plugin never gets a second, distinct copy of an assembly the host
already loaded.

A `GC.KeepAlive( typeof( CKli.Plugins.PluginCollector ) )` call forces a hard reference to
`CKli.Plugins.Core`, preventing the trimmer/linker from dropping what looks like an unused reference; the
comment in the source notes that `CKli.Testing` must do the same for the same reason (it loads plugins
directly from a World host's run folder, and without the reference `CKli.Plugins.Core` would otherwise be
picked up from the plugin context instead of the default one, again causing `MissingMethodException`).

`Initialize` also accepts an optional `supplementary` `AssemblyLoadContext` whose assemblies are merged into
the snapshot before `Default`'s. It exists because some hosts don't load the application into
`AssemblyLoadContext.Default` — the source comment records that NUnit3TestAdapter v6.0.0 briefly loaded test
assemblies into its own `TestAssemblyLoadContext` (fixed again by v6.1.0). Nothing currently passes a
`supplementary` context, but the parameter — and `AssemblyLoadContext.All`, which makes finding such a
context possible — is kept in case a future host needs it. `IsInitialized` exposes whether `Initialize` has
already run, for callers that want to check before calling it themselves.

### Verifying the unload

Because `Unload()` only *requests* collection — actual reclamation depends on the GC finding no more live
references into the context — `PluginMachinery` treats the `WeakReference` returned by `Load` as the source
of truth. It keeps at most one such reference alive at a time (`PluginMachinery._singleFactory`, a static
field: only one World's plugins are ever loaded per process at once) and, before loading a replacement,
calls `ReleaseCurrentSingleRunning`:

```csharp
// CKli.Core/Plugin/Impl/PluginMachinery.cs
for( int i = 0; _singleFactory.IsAlive && (i < 10); i++ )
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
}
if( _singleFactory.IsAlive )
{
    monitor.Error( "Current plugins cannot be unloaded. A Plugin is still referenced from the World. ..." );
    return false;
}
```

If, after ten collection passes, the context is still alive, something outside `PluginLoadContext` (a
plugin object, a delegate, an event subscription) is still rooting it — CKli surfaces this as an error
rather than silently leaking, since the whole point of a collectible context is that it *does* unload.

## Wiring summary

```
CKli/Program.cs                       World.PluginLoader = PluginLoadContext.Load   (production)
Tests/CKli.Core.Tests/TestEnv.cs      World.PluginLoader = PluginLoadContext.Load   (tests)
CKli.Core/Plugin/Impl/PluginMachinery.cs   calls World.PluginLoader(...) to obtain an IPluginFactory,
                                            uses it to Create(...) the PluginCollection, and disposes
                                            it (unloading the AssemblyLoadContext) when the World is done.
```

## Gotchas

- **Keep this assembly's own dependency surface minimal.** Anything `CKli.Loader` itself references ends
  up resolvable only through the default context's snapshot taken by `Initialize()` — it is not meant to
  carry plugin-facing functionality.
- **`<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`** on the plugins project is not
  optional: without it, private plugin dependencies are silently resolved against the default context
  instead of the plugin's own folder, which can work by accident until a version conflict surfaces.
- **`Initialize()` can only run once per process** (`Throw.CheckState`) — it captures a snapshot, not a
  live view, of the default context's assemblies.
