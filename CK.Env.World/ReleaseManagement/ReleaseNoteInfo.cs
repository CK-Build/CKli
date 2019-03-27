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
            SolutionName = info.Solution.UniqueSolutionName;
            PreviousVersion = info.PreviousVersion;
            Current = info.CurrentReleaseInfo;
            ReleaseNote = info.ReleaseNote;
        }

        public ReleaseNoteInfo( XElement e )
        {
            SolutionName = (string)e.AttributeRequired( "SolutionName" );
            Current = new ReleaseInfo( e.Element( "ReleaseInfo" ) );
            var p = e.Element( "PreviousVersion" );
            if( p != null ) PreviousVersion = CSVersion.Parse( p.Value );
            ReleaseNote = e.Element( "ReleaseNote" ).Value;
        }

        public XElement ToXml()
        {
            return new XElement( "R",
                        new XAttribute( "SolutionName", SolutionName ),
                        Current.ToXml(),
                        PreviousVersion != null ? new XElement( "PreviousVersion", PreviousVersion.ToString() ) : null,
                        new XElement( "ReleaseNote", new XCData( ReleaseNote ?? String.Empty ) ) );
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
