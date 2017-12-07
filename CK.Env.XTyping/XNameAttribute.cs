using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class XNameAttribute : Attribute
    {
        public XNameAttribute( string name, string xmlNamespace = null )
        {
            Name = XName.Get( name, xmlNamespace ?? String.Empty );
        }

        public XName Name { get; }
    }
}
