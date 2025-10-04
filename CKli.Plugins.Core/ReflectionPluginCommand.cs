using CK.Core;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Tasks;

namespace CKli.Core;

sealed class ReflectionPluginCommand : PluginCommand
{
    readonly MethodInfo _method;
    readonly int _parameterCount;

    public ReflectionPluginCommand( IPluginTypeInfo typeInfo,
                                    string commandPath,
                                    string description,
                                    ImmutableArray<(string Name, string Description)> arguments,
                                    ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options,
                                    ImmutableArray<(ImmutableArray<string> Names, string Description)> flags,
                                    MethodInfo method,
                                    int parameterCount,
                                    MethodAsyncReturn returnType )
        : base( typeInfo, commandPath, description, arguments, options, flags, method.Name, returnType )
    {
        _method = method;
        _parameterCount = parameterCount;
    }

    protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor, CommandCommonContext context, CommandLineArguments cmdLine )
    {
        var args = new object?[_parameterCount];
        for( int i = 0; i < Arguments.Length; i++ )
        {
            args[i] = cmdLine.EatArgument();
        }
        for( int i = 0; i < Options.Length; i++ )
        {
            var o = Options[i];
            args[i + Arguments.Length] = o.Multiple
                                            ? cmdLine.EatMultipleOption( o.Names )
                                            : cmdLine.EatSingleOption( o.Names );
        }
        for( int i = 0; i < Flags.Length; i++ )
        {
            args[i + Arguments.Length + Options.Length] = cmdLine.EatFlag( Flags[i].Names );
        }
        switch( ReturnType )
        {
            case MethodAsyncReturn.None:
                return ValueTask.FromResult( (bool)_method.Invoke( _instance, args )! );
            case MethodAsyncReturn.ValueTask:
                return (ValueTask<bool>)_method.Invoke( _instance, args )!;
            default:
                Throw.DebugAssert( ReturnType == MethodAsyncReturn.Task );
                return new ValueTask<bool>( (Task<bool>)_method.Invoke( Instance, args )! );
        }
    }
}
