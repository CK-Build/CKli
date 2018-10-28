using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env
{
    public class NuGetConfigFile
    {
        readonly GitFolder _folder;
        readonly XDocument _doc;
        readonly NormalizedPath _path;
        readonly XElement _packageSources;

        public NuGetConfigFile( GitFolder f, IActivityMonitor m )
        {
            var (xDoc, pathXml) = f.GetXmlDocument( m, "nuget.config" );
            if( xDoc != null )
            {
                var e = xDoc.Root;
                var packageSources = e.Element( "packageSources" );
                if( packageSources == null )
                {
                    m.Fatal( $"nuget.config must contain at least one <packageSources> element." );
                }
                else
                {
                    _folder = f;
                    _doc = xDoc;
                    _path = pathXml;
                    _packageSources = packageSources;
                }
            }
        }

        internal bool IsValid => _folder != null;

        /// <summary>
        /// Updates the NuGet config file with LocalFeed/Release, LocalFeed/CI and/or LocalFeed/Local
        /// sources.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="ensureRelease">True to add the LocalFeed/Release.</param>
        /// <param name="ensureCI">True to add the LocalFeed/CI.</param>
        /// <param name="ensureLocal">True to add the LocalFeed/Local.</param>
        /// <returns>Success and whether local sources have actually been added.</returns>
        public (bool Success, bool Added) EnsureLocalFeedsNuGetSource( IActivityMonitor m, bool ensureRelease = true, bool ensureCI = true, bool ensureLocal = true )
        {
            if( !IsValid ) return (false, false);
            bool added = false;
            if( ensureRelease && !_packageSources.Elements( "add" ).Any( x => (string)x.Attribute( "key" ) == "LocalFeed-Release" ) )
            {
                var localFeed = _folder.FeedProvider.GetReleaseFeedFolder( m ).PhysicalPath;
                _packageSources.Add( new XElement( "add",
                                                new XAttribute( "key", "LocalFeed-Release" ),
                                                new XAttribute( "value", localFeed ) ) );
                added = true;
            }
            if( ensureCI && !_packageSources.Elements( "add" ).Any( x => (string)x.Attribute( "key" ) == "LocalFeed-CI" ) )
            {
                var localFeed = _folder.FeedProvider.GetCIFeedFolder( m ).PhysicalPath;
                _packageSources.Add( new XElement( "add",
                                                new XAttribute( "key", "LocalFeed-CI" ),
                                                new XAttribute( "value", localFeed ) ) );
                added = true;
            }
            if( ensureLocal && !_packageSources.Elements( "add" ).Any( x => (string)x.Attribute( "key" ) == "LocalFeed-Local" ) )
            {
                var blankFeed = _folder.FeedProvider.GetLocalFeedFolder( m ).PhysicalPath;
                _packageSources.Add( new XElement( "add",
                                                new XAttribute( "key", "LocalFeed-Local" ),
                                                new XAttribute( "value", blankFeed ) ) );
                added = true;
            }
            if( added )
            {
                return (_folder.FileSystem.CopyTo( m, _doc.ToString(), _path ), true);
            }
            return (true, false);
        }

        /// <summary>
        /// Removes any local sources.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>Success and whether local sources have actually been removed.</returns>
        public (bool Success, bool Removed) RemoveLocalFeedsNuGetSource( IActivityMonitor m )
        {
            if( !IsValid ) return (false, false);
            var e = _packageSources
                     .Elements( "add" )
                     .Where( b => (string)b.Attribute( "key" ) == "LocalFeed-Local"
                                    || (string)b.Attribute( "key" ) == "LocalFeed-CI"
                                    || (string)b.Attribute( "key" ) == "LocalFeed-Release" );
            if( e.Any() )
            {
                e.Remove();
                return (_folder.FileSystem.CopyTo( m, _doc.ToString(), _path ), true);
            }
            return (true, false);
        }


    }
}
