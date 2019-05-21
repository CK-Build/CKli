using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;

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
