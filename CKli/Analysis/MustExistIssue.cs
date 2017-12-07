using System;
using System.Collections.Generic;
using System.Text;
using CK.Core;

using System.IO;
using CKli;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;

namespace CK.Env.Analysis
{
    [XName("MustExist")]
    public class MustExistIssue : XIssue
    {
        public MustExistIssue(
            XPathItem path,
            XReferentialFolder refFolder,
            IssueCollector issueCollector,
            Initializer initializer )
            : base( issueCollector, initializer )
        {
            Item = path;
            RefFolder = refFolder;
        }

        XPathItem Item { get; }

        NormalizedPath Content { get; }

        XReferentialFolder RefFolder { get; }

        protected override bool DoRun( IRunContextIssue ctx )
        {
            if( Item.FileInfo.Exists )
            {
                if( Item.FileInfo.IsDirectory != Item.IsFolder )
                {
                    ctx.Monitor.Fatal( $"File/Directory mismatch for '{Item.FullPath}' (expected {Item.KindName})." );
                }
                else
                {
                    ctx.Monitor.Trace( $"{Item.KindName} '{Item.FullPath}' exists." );
                }
            }
            else
            {
                ctx.Monitor.Error( $"Missing {Item.KindName} '{Item.FullPath}'." );
                if( !Content.IsEmpty )
                {
                    ctx.CreateFix( "Creating content", m => Item.CopyFrom( m, RefFolder, Content ) );
                }
            }
            return true;
        }
    }
}
