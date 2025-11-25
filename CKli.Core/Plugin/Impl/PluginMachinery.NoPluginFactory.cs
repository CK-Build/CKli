using CK.Core;
using System;

namespace CKli.Core;

public sealed partial class PluginMachinery
{
    static IPluginFactory? _noPluginFactory;

    static IPluginFactory GetNoPluginFactory() => _noPluginFactory ??= new NoPluginFactory();

    sealed class NoPluginFactory : IPluginFactory
    {
        sealed class EmptyPluginCollection : PluginCollection
        {
            public EmptyPluginCollection()
                : base( [], [], CommandNamespace.Empty )
            {
            }
        }

        public PluginCompileMode CompileMode => PluginCompileMode.Release;

        public PluginCollection Create( IActivityMonitor monitor, World world ) => new EmptyPluginCollection();

        public void Dispose()
        {
        }

        public string GenerateCode()
        {
            throw new NotImplementedException();
        }
    }

#pragma warning restore IDE1006 // Naming Styles

}

