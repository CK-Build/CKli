using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using Microsoft.Extensions.FileProviders;

using System.Xml.Linq;
using System.Xml.XPath;

namespace CKli
{
    public class XTextReplace : XTypedObject
    {
        readonly IFileInfoHandler _target;

        public XTextReplace(
            Initializer initializer,
            IFileInfoHandler target )
            : base( initializer )
        {
            _target = target;
            target.AddProcessor( Process );
        }

        public string Text { get; private set; }

        public string SourceXPath { get; private set; }

        public string SourcePropertyName { get; private set; }

        private IFileInfo Process( IActivityMonitor m, IFileInfo f )
        {
            XElement e = _target.TargetItem.XElement.XPathSelectElement( SourceXPath );
            if( e == null )
            {
                m.Error( $"Unable to locate SourceXPath = '{SourceXPath}' from '{_target.TargetItem.XElement.ToStringPath()}'." );
                return null;
            }
            var o = e.Annotation<XTypedObject>();
            var p = o.GetType().GetProperty( SourcePropertyName );
            if( p == null )
            {
                m.Error( $"Unable to get Property = '{SourcePropertyName}' on '{o.XElement.ToStringPath()}'." );
                return null;
            }
            var v = p.GetValue( o );

            var t = f.AsTextFileInfo();
            if( t != null )
            {
                f = t.WithTransformedText( text => text.Replace( Text, v.ToString() ) );
            }
            else m.Warn( $"File {_target.ContentPath} is not a text file." );
            return f;
        }

    }
}
