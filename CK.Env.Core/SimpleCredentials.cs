using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public class SimpleCredentials
    {
        public SimpleCredentials( string userName, string password )
        {
            UserName = userName;
            Password = password;
        }

        public string UserName { get; }

        public string Password { get; }
    }
}
