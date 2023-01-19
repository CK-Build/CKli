# System.CommandLine

## Setting the ExitCode in a synchronous handler

See here: https://learn.microsoft.com/en-us/dotnet/standard/commandline/model-binding#set-exit-codes

An asynchronous handler can be a `Func<...,Task<int>>`: the handler can simply returns the exit code.
As synchronous handler can only be an `Action<...>`: to set the exit code, one need to write a
naked handler that needs to locate its parameters through the `InvocationContex` (kind of Service locator)
and to use the context to set the exit code. Rather ugly.

Supporting synchronous handler with exit code can be done (see [HandlerWithExitCode](HandlerWithExitCode.cs)).
This can now be written:
```csharp
command.SetHandler( (console, val) =>
{
    console.WriteLine( $"value = {val}" );
    return 0;
},
Binder.Console, Binder.Constant( 8 ) );
```

This uses the [Binder](Binder.cs) helper.

## The Binder helper

This helper provides an easy way to bind handler parameters. It exposes properties (like `Console.Binder`) and
some functions like `Binder.Constant` above or:
```csharp
/// <summary>
/// Binds to the required service from <see cref="BindingContext"/>.
/// </summary>
public static IValueDescriptor<T> Service<T>() where T : notnull;
```

## Surprise!
Even if `Command.SetHandler` expects `IValueDescriptor` binders, this doesn't work:

```csharp
sealed class ContantDescriptor<T> : IValueDescriptor<T>
{
    readonly T _value;

    public ContantDescriptor( T value )
    {
        _value = value;
    }

    public string ValueName => nameof(Constant);

    public Type ValueType => typeof(T);

    public bool HasDefaultValue => false;

    public object? GetDefaultValue() => _value;
}

public static IValueDescriptor<T> Constant<T>( T value ) => new ContantDescriptor<T>( value );
```

The descriptor MUST actually be a `IValueSource`. `BinderBase` does the job, but this type mismatch is rather unfortunate.
The following works:
```csharp
sealed class ContantDescriptor<T> : BinderBase<T>
{
    readonly T _value;

    public ContantDescriptor( T value )
    {
        _value = value;
    }

    protected override T GetBoundValue( BindingContext bindingContext ) => _value;
}
```









