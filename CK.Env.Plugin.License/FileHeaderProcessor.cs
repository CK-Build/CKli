//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.IO;
//using System.Text.RegularExpressions;
//using System.Reflection;
//using CK.Core;
//using System.Globalization;
//using CK.Text;

//namespace CK.Env.Plugin
//{
//    static class FileHeaderProcessor
//    {
//        public static int Process( NormalizedPath rootPath, Func<DirectoryInfo,bool> directoryFilter, bool addNew, bool removeExisting, string licenseHeaderText )
//        {
//            DirectoryInfo d = new DirectoryInfo( rootPath );
//            if( d.Exists && directoryFilter( d ) )
//            {
//                string text = null;
//                if( addNew )
//                {
//                    text = licenseHeaderText;
//                    if( text.Length == 0 ) text = null;
//                    else
//                    {
//                        if( !text.EndsWith( Environment.NewLine ) ) text += Environment.NewLine;
//                        // Inserts a new blank line.
//                        text += Environment.NewLine;
//                    }
//                }
//                if( text != null || removeExisting )
//                {
//                    return ProcessDirectory( root.Length, d, text, removeExisting );
//                }
//            }
//            return 0;
//        }

//        static int ProcessDirectory( int rootLength, DirectoryInfo d, string text, bool removeExisting )
//        {
//            int count = 0;
//            if( d.Name == ".svn" || d.Name == ".nuget" || d.Name == ".git" || (d.Attributes & FileAttributes.Hidden) != 0 ) return count;
//            foreach( FileInfo f in d.GetFiles( "*.cs" ) )
//            {
//                if( ProcessFile( rootLength, f, text, removeExisting ) ) ++count;
//            }
//            foreach( DirectoryInfo c in d.GetDirectories() )
//            {
//                count += ProcessDirectory( rootLength, c, text, removeExisting );
//            }
//            return count;
//        }

//        static bool ProcessFile( int rootLength, FileInfo f, string text, bool removeExisting )
//        {
//            if( !f.Name.EndsWith( ".Designer.cs" ) )
//            {
//                AddHeader( rootLength, f, text, removeExisting );
//                return true;
//            }
//            return false;
//        }

//        internal static Regex _existing = new Regex( "\\s*#region \\w+ License\\s.*?#endregion\\s+",
//            RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant | RegexOptions.Compiled );

//        static void AddHeader( int rootLength, FileInfo f, string text, bool removeExisting )
//        {
//            if( text != null )
//            {
//                string fName = f.FullName.Substring( rootLength );
//                text = text.Replace( "[FILE]", fName );
//                text = text.Replace( "[CurrentYear]", DateTime.Now.Year.ToString( CultureInfo.InvariantCulture ) );
//            }
//            string content = File.ReadAllText( f.FullName );
//            if( removeExisting )
//            {
//                content = _existing.Replace( content, String.Empty, 1 );
//            }
//            File.WriteAllText( f.FullName, text + content );
//        }

//    }
//}
