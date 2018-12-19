using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CK.Core;
using CKSetup;

namespace CK.Env
{
    public class CKSetupStoreTypeFactory : IArtifactTypeFactory
    {
        public IArtifactRepositoryInfo CreateInfo( XElement e )
        {
            string type = (string)e.Attribute( "Type" );
            if( type == "CKSetup" )
            {
                string url = (string)e.Attribute( "Url" );
                if( url == null || url == Facade.DefaultStorePath ) return DefaultPublicStore.Default;

                string name = (string)e.AttributeRequired( "Name" );
                if( !Regex.IsMatch( name, "^\\w+$", RegexOptions.CultureInvariant ) )
                {
                    throw new ArgumentException( $"Invalid name. Must be an identifier ('^\\w+$' regex)." );
                }
                if( name == "Public" ) throw new ArgumentException( $"'Public' name is reserved for the default public store." );
                return new CKSetupStoreRepositoryInfo( url, name );
            }
            return null;
        }

        public IArtifactRepository FindOrCreate( IActivityMonitor m, IArtifactRepositoryInfo info )
        {
            throw new NotImplementedException();
        }
    }
}
