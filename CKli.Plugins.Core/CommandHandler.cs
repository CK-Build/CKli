using System.Reflection;

namespace CKli.Core;

sealed class CommandHandler
{
    readonly IPluginTypeInfo _typeInfo;
    readonly MethodInfo _method;
    readonly bool _isAsync;

    public CommandHandler( IPluginTypeInfo typeInfo, MethodInfo method, bool isAsync )
    {
        _typeInfo = typeInfo;
        _method = method;
        _isAsync = isAsync;
    }

    public override string ToString() => $"{_typeInfo.TypeName}.{_method.Name}";
}
