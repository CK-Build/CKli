using CK.Core;
using System;

namespace CKli.Core;

public sealed partial class PluginMachinery
{
    static IPluginFactory? _onErrorPluginFactory;

    static IPluginFactory GetOnErrorPluginFactory() => _onErrorPluginFactory ??= new OnErrorPluginFactory();

    sealed class OnErrorPluginFactory : IPluginFactory
    {
        sealed class ErrorPluginCollection : PluginCollection
        {
            public ErrorPluginCollection()
                : base( [], [], CommandNamespace.Empty )
            {
            }

            public override bool HasLoadError => true;

        }

        public PluginCompileMode CompileMode => PluginCompileMode.Release;

        public PluginCollection Create( IActivityMonitor monitor, World world ) => new ErrorPluginCollection();

        public void Dispose()
        {
        }

        public string GenerateCode()
        {
            throw new NotSupportedException();
        }
    }

#pragma warning restore IDE1006 // Naming Styles

}

