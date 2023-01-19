using CK.Core;
using CSemVer;
using System;
using System.Xml.Linq;

namespace CK.Env
{
    public class ReleaseNoteInfo
    {
        internal ReleaseNoteInfo( ReleaseSolutionInfo info )
        {
            SolutionName = info.Solution.Solution.Name;
            PreviousVersion = info.PreviousVersion;
            Current = info.CurrentReleaseInfo;
            ReleaseNote = info.ReleaseNote;
        }

        public ReleaseNoteInfo( XElement e )
        {
            SolutionName = (string)e.AttributeRequired( XmlNames.xSolutionName );
            Current = new ReleaseInfo( e.Element( XmlNames.xReleaseInfo ) );
            var p = e.Element( XmlNames.xPreviousVersion );
            if( p != null ) PreviousVersion = CSVersion.Parse( p.Value );
            ReleaseNote = e.Element( XmlNames.xReleaseNote ).Value;
        }

        public XElement ToXml()
        {
            return new XElement( XmlNames.xR,
                                 new XAttribute( XmlNames.xSolutionName, SolutionName ),
                                 Current.ToXml(),
                                 PreviousVersion != null ? new XElement( XmlNames.xPreviousVersion, PreviousVersion.ToString() ) : null,
                                 new XElement( XmlNames.xReleaseNote, new XCData( ReleaseNote ?? String.Empty ) ) );
        }

        /// <summary>
        /// Gets the solution name.
        /// </summary>
        public string SolutionName { get; }

        /// <summary>
        /// Gets the previous version, associated to a commit below the current one.
        /// This is null if no previous version has been found.
        /// </summary>
        public CSVersion PreviousVersion { get; }

        /// <summary>
        /// Gets the <see cref="ReleaseInfo"/>.
        /// </summary>
        public ReleaseInfo Current { get; }

        /// <summary>
        /// Gets the release note.
        /// </summary>
        public string ReleaseNote { get; }
    }
}
