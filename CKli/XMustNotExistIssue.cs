using System;
using System.Collections.Generic;
using System.Text;
using CK.Core;

using System.IO;
using CKli;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using CK.Text;
using CK.Env.Analysis;
using CK.Env;
using System.Linq;

namespace CKli
{
    [CK.Env.XName( "MustNotExist")]
    public class XMustNotExistIssue : XIssue
    {
        public XMustNotExistIssue(
            XPathItem path,
            IssueCollector issueCollector,
            Initializer initializer )
            : base( issueCollector, initializer )
        {
            Item = path;
        }

        public XPathItem Item { get; }

        protected override bool CreateIssue( IRunContextIssue builder )
        {
            if( Item.FileInfo.Exists )
            {
                if( Item.FileInfo.IsDirectory != Item.IsFolder )
                {
                    builder.Monitor.Fatal( $"File/Directory mismatch for '{Item.FullPath}' (expected {Item.Kind})." );
                }
                else
                {
                    using( builder.Monitor.OpenError( $"{Item.Kind} '{Item.FullPath}' exists." ) )
                    {
                        Func<IActivityMonitor, bool> fix = m => Item.FileSystem.Delete( m, Item.FullPath );
                        builder.CreateIssue( $"Delete:{Item.FullPath}", "Item must be deleted", fix );
                    }
                }
            }
            return true;
        }
    }
}
