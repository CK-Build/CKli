using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    public class XWorldSecrets : XTypedObject
    {
        public XWorldSecrets( Initializer initializer, SecretKeyStore keyStore ) : base( initializer )
        {
            initializer.Reader.HandleAddRemoveClearChildren(
                new HashSet<object>(),
                b =>
                {
                    string name = b.HandleRequiredAttribute<string>( "Name" );
                    string password = b.HandleRequiredAttribute<string>( "Value" );
                    string description = b.HandleRequiredAttribute<string>( "Description" );
                    keyStore.DeclareSecretKey( name, desc => description, true, "World" );
                    keyStore.SetSecret( initializer.Monitor, name, password );
                    return name;
                }
            );
            initializer.Reader.WarnUnhandled();
        }
    }
}
