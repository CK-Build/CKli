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
    [CK.Env.XName( "MustExist")]
    public class XMustExistIssuer : XIssuer, IFileInfoHandler
    {
        readonly List<Func<IActivityMonitor, IFileInfo, IFileInfo>> _fileProcessors;

        public XMustExistIssuer(
            XPathItem path,
            XReferentialFolder refFolder,
            IssueCollector issueCollector,
            Initializer initializer )
            : base( issueCollector, initializer )
        {
            Item = path;
            RefFolder = refFolder;
            _fileProcessors = new List<Func<IActivityMonitor, IFileInfo, IFileInfo>>();
            initializer.ChildServices.Add<IFileInfoHandler>( this );
        }

        public XPathItem Item { get; }

        public NormalizedPath Content { get; private set; }

        public NormalizedPath InitialContent { get; private set; }

        public XReferentialFolder RefFolder { get; }

        protected override bool OnCreated( Initializer initializer )
        {
            base.OnCreated( initializer );
            //if( InitialContent.IsEmpty ) InitialContent = Content;
            //else if( !Content.IsEmpty && Content != InitialContent )
            //{
            //    initializer.Monitor.Error( $"'{Item}': Content = '{Content}' is not the same as InitialContent = '{InitialContent}'." );
            //    return false;
            //}
            return true;
        }

        protected override bool CreateIssue( IRunContextIssue builder )
        {
            //if( Item.FileInfo.Exists )
            //{
            //    if( Item.FileInfo.IsDirectory != Item.IsFolder )
            //    {
            //        builder.Monitor.Fatal( $"File/Directory mismatch for '{Item.FullPath}' (expected {Item.Kind})." );
            //    }
            //    else
            //    {
            //        using( builder.Monitor.OpenTrace( $"{Item.Kind} '{Item.FullPath}' exists." ) )
            //        {
            //            FileProviderContentInfo exist = Item.ContentInfo;
            //            if( exist.CaseConflicts.Count > 0 )
            //            {
            //                using( builder.Monitor.OpenFatal( $"{exist.CaseConflicts.Count} case sensitive paths detected. They must be manually eliminated." ) )
            //                {
            //                    foreach( var p in exist.CaseConflicts ) builder.Monitor.Trace( p );
            //                }
            //            }
            //            else
            //            {
            //                if( !Content.IsEmpty )
            //                {
            //                    FileProviderContentInfo referential = GetReferentialFileProvider( builder.Monitor ).GetContentInfo( Content );
            //                    var diffResult = exist.ComputeDiff( referential );
            //                    if( diffResult.FixCasePaths.Count > 0 )
            //                    {
            //                        using( builder.Monitor.OpenFatal( $"In these {diffResult.FixCasePaths.Count} paths, casing must be strictly the same. This must be manually corrected." ) )
            //                        {
            //                            foreach( var p in diffResult.FixCasePaths ) builder.Monitor.Trace( p );
            //                        }
            //                    }
            //                    else
            //                    {
            //                        if( diffResult.Differences.Count > 0 )
            //                        {
            //                            using( builder.Monitor.OpenInfo( $"{diffResult.Differences.Count} differences found." ) )
            //                            {
            //                                foreach( var diff in diffResult.Differences) builder.Monitor.Info( diff.ToString() );
            //                            }
            //                            Func<IActivityMonitor, bool> fix = m => Item.FileSystem.ApplyDiff( m, diffResult );
            //                            builder.CreateIssue( $"Update:{Item.FullPath}:With:{Content}", "File content update required", fix );
            //                        }
            //                        else builder.Monitor.CloseGroup( $"Content is up to date." );
            //                    }
            //                }
            //            }
            //        }
            //    }
            //}
            //else
            //{
            //    builder.Monitor.Error( $"Missing {Item.Kind} '{Item.FullPath}'." );
            //    if( !InitialContent.IsEmpty )
            //    {
            //        builder.CreateIssue( $"Create:{Item.FullPath}:With:{InitialContent}", "Initial content must be created.",
            //                             m => Item.FileSystem.ApplyDiff( m, Item.ContentInfo.ComputeDiff( GetReferentialFileProvider( m ).GetContentInfo( InitialContent ) ) ) );
            //    }
            //    else if( Item.IsFolder )
            //    {
            //        builder.CreateIssue( $"CreateFolder:{Item.FullPath}", "Folder must be created.", m =>
            //        {
            //            if( Item.FileInfo.PhysicalPath != null )
            //            {
            //                Directory.CreateDirectory( Item.FileInfo.PhysicalPath );
            //                m.Info( $"Folder {Item.FullPath} has been created." );
            //            }
            //            else m.Error( $"Folder {Item.FullPath} can not be created." );
            //            return true;
            //        } );
            //        builder.SkipRunChildren = true;
            //    }
            //}
            return true;
        }

        XPathItem IFileInfoHandler.TargetItem => Item;

        NormalizedPath IFileInfoHandler.ContentPath => InitialContent;

        void IFileInfoHandler.AddProcessor( Func<IActivityMonitor, IFileInfo, IFileInfo> processor )
        {
            if( processor == null ) throw new ArgumentNullException( nameof( processor ) );
            _fileProcessors.Add( processor );
        }

        IFileProvider GetReferentialFileProvider( IActivityMonitor m )
        {
            if( _fileProcessors.Count == 0 ) return RefFolder.FileProvider;
            return new TransformedFileProvider( RefFolder.FileProvider, CreateFileTransformer( m ) );
        }

        Func<string,IFileInfo,IFileInfo> CreateFileTransformer( IActivityMonitor m )
        {
            return (path, f) =>
            {
                if( InitialContent.Path.Equals( path, StringComparison.OrdinalIgnoreCase ) )
                {
                    foreach( var t in _fileProcessors )
                    {
                        f = t( m, f );
                    }
                }
                return f;
            };
        }
    }
}
