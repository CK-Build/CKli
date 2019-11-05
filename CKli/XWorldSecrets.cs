using System.Collections.Generic;

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
