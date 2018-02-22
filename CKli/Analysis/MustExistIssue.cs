using System;
using System.Collections.Generic;
using System.Text;
using CK.Core;

using System.IO;
using CKli;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using CK.Text;

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

        public XPathItem Item { get; }

        public NormalizedPath Content { get; private set; }

        public NormalizedPath InitialContent { get; private set; }

        public XReferentialFolder RefFolder { get; }

        protected override bool OnCreated( Initializer initializer )
        {
            base.OnCreated( initializer );
            if( InitialContent.IsEmpty ) InitialContent = Content;
            else if( !Content.IsEmpty && Content != InitialContent )
            {
                initializer.Monitor.Error( $"'{Item}': Content = '{Content}' is not the same as InitialContent = '{InitialContent}'." );
                return false;
            }
            return true;
        }

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
                    using( builder.Monitor.OpenTrace( $"{Item.Kind} '{Item.FullPath}' exists." ) )
                    {
                        FileProviderContentInfo exist = Item.ContentInfo;
                        if( exist.CaseConflicts.Count > 0 )
                        {
                            using( builder.Monitor.OpenFatal( $"{exist.CaseConflicts.Count} case sensitive paths detected. They must be manually eliminated." ) )
                            {
                                foreach( var p in exist.CaseConflicts ) builder.Monitor.Trace( p );
                            }
                        }
                        else
                        {
                            if( !Content.IsEmpty )
                            {
                                FileProviderContentInfo referential = RefFolder.FileProvider.GetContentInfo( Content );
                                var diffResult = exist.ComputeDiff( referential );
                                if( diffResult.FixCasePaths.Count > 0 )
                                {
                                    using( builder.Monitor.OpenFatal( $"In these {diffResult.FixCasePaths.Count} paths, casing must be strictly the same. This must be manually corrected." ) )
                                    {
                                        foreach( var p in diffResult.FixCasePaths ) builder.Monitor.Trace( p );
                                    }
                                }
                                else
                                {
                                    if( diffResult.Differences.Count > 0 )
                                    {
                                        using( builder.Monitor.OpenInfo( $"{diffResult.Differences.Count} differences found." ) )
                                        {
                                            foreach( var diff in diffResult.Differences) builder.Monitor.Info( diff.ToString() );
                                        }
                                        Func<IActivityMonitor, bool> fix = m => Item.FileSystem.ApplyDiff( m, diffResult );
                                        builder.CreateIssue( "Correcting content", fix );
                                    }
                                    else builder.Monitor.CloseGroup( $"Content is up to date." );
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                builder.Monitor.Error( $"Missing {Item.Kind} '{Item.FullPath}'." );
                if( !InitialContent.IsEmpty )
                {
                    FileProviderContentInfo referential = RefFolder.FileProvider.GetContentInfo( InitialContent );
                    builder.CreateIssue( "Creating initial content", m => Item.FileSystem.ApplyDiff( m, Item.ContentInfo.ComputeDiff( referential ) ) );
                }
            }
            return true;
        }
    }
}
