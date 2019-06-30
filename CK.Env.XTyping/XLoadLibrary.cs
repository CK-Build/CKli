using CK.Core;
using System;
using System.Reflection;

namespace CK.Env
{
    public class XLoadLibrary : XTypedObject
    {
        public XLoadLibrary( Initializer initializer )
            : base( initializer )
        {
            Load( initializer.Services.GetService<XTypedFactory>(), initializer.Monitor );
        }

        public string Name { get; set; }

        public bool Optional { get; set; }

        void Load( XTypedFactory factory, IActivityMonitor m )
        {
            using( m.OpenInfo( Optional ? $"Loading library '{Name}'." : $"Loading optional library '{Name}'." ) )
            {
                try
                {
                    var a = Assembly.Load( Name );
                    factory.AutoRegisterFromdAssembly( m, a );
                }
                catch( Exception ex )
                {
                    if( Optional )
                    {
                        m.Warn( "Loading failed for optional assembly.", ex );
                    }
                    else m.Error( "Load failed.", ex );
                    throw;
                }
            }
        }

    }
}
