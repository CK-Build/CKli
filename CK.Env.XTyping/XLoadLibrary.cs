using CK.Core;
using System;
using System.IO;
using System.Reflection;

namespace CK.Env
{
    public class XLoadLibrary : XTypedObject
    {
        public XLoadLibrary( Initializer initializer )
            : base( initializer )
        {
            Load( initializer.Services.GetService<XTypedFactory>( true ), initializer.Monitor );
            Name = initializer.Reader.HandleRequiredAttribute<string>( nameof(Name) );
        }

        public string Name { get; set; }

        public bool Optional { get; set; }

        void Load( XTypedFactory factory, IActivityMonitor m )
        {
            using( m.OpenInfo( Optional ? $"Loading optional library '{Name}'." : $"Loading library '{Name}'." ) )
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
