using CK.Core;
using System;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates local <see cref="XDocument"/> state.
    /// </summary>
    public class LocalWorldState : BaseWorldState
    {
        readonly XElement _generalState;
        readonly XElement _lastBuild;
        readonly XElement _builds;
        readonly XElement _releaseBuildResult;
        readonly XElement _ciBuildResult;
        readonly XElement _localBuildResult;
        readonly XElement _publishedBuildHistory;
        readonly BuildResult[] _buildResults;
        LogFilter _logFilter;
        LogFilter _monitorLogFilter;

        public LocalWorldState( IWorldStore store, IWorldName w, XDocument? d = null )
            : base( store, w, true, d )
        {
            var r = XDocument.Root;
            _generalState = r.EnsureElement( XmlNames.xGeneralState );
            _lastBuild = r.EnsureElement( XmlNames.xLastBuild );
            _builds = r.EnsureElement( XmlNames.xBuilds );
            _releaseBuildResult = _builds.EnsureElement( XmlNames.xRelease );
            _ciBuildResult = _builds.EnsureElement( XmlNames.xCI );
            _localBuildResult = _builds.EnsureElement( XmlNames.xLocal );
            _publishedBuildHistory = r.EnsureElement( XmlNames.xPublishedBuildHistory );
            LastBuildType = _lastBuild.AttributeEnum( XmlNames.xType, BuildResultType.None );
            _buildResults = new BuildResult[3];
        }

        /// <summary>
        /// Gets or sets the current global status.
        /// </summary>
        public GlobalWorkStatus WorkStatus
        {
            get => XDocument.Root.AttributeEnum( XmlNames.xWorkStatus, GlobalWorkStatus.Idle );
            set => XDocument.Root.SetAttributeValue( XmlNames.xWorkStatus, value.ToString() );
        }

        /// <summary>
        /// Gets or sets the user log filter.
        /// </summary>
        public LogFilter UserLogFilter
        {
            get
            {
                if( _logFilter == LogFilter.Undefined )
                {
                    LogFilter.TryParse( (string)_generalState.Attribute( XmlNames.xUserLogFilter ) ?? "", out _logFilter );
                }
                return _logFilter;
            }
            set
            {
                if( value != _logFilter ) _generalState.SetAttributeValue( XmlNames.xUserLogFilter, (_logFilter = value).ToString() );
            }
        }

        /// <summary>
        /// Gets or sets the monitor log filter: the actual level of the logs.
        /// </summary>
        public LogFilter MonitorLogFilter
        {
            get
            {
                if( _monitorLogFilter == LogFilter.Undefined )
                {
                    LogFilter.TryParse( (string)_generalState.Attribute( XmlNames.xMonitorLogFilter ) ?? "", out _monitorLogFilter );
                }
                return _logFilter;
            }
            set
            {
                if( value != _monitorLogFilter ) _generalState.SetAttributeValue( XmlNames.xUserLogFilter, (_monitorLogFilter = value).ToString() );
            }
        }

        /// <summary>
        /// Gets or sets the current Roadmap element.
        /// The element is automatically created as needed.
        /// When setting, the value must not be null and its <see cref="XElement.Name"/> must be <see cref="XmlNames.xRoadmap"/>.
        /// </summary>
        public XElement Roadmap
        {
            get => _generalState.EnsureElement( XmlNames.xRoadmap );
            set
            {
                if( value == null || value.Name != XmlNames.xRoadmap ) throw new ArgumentException();
                _generalState.ReplaceElementByName( value );
            }
        }

        /// <summary>
        /// Gets the <see cref="XmlNames.xGitSnapshot"/> element or null if there is no snapshot.
        /// </summary>
        public XElement GetGitSnapshot() => _generalState.Element( XmlNames.xGitSnapshot );

        /// <summary>
        /// Sets the <see cref="XmlNames.xGitSnapshot"/> element.
        /// There must be no current snapshot otherwise an <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        /// <param name="e">The non null snapshot.</param>
        public void SetGitSnapshot( XElement e )
        {
            if( e == null || e.Name != XmlNames.xGitSnapshot ) throw new ArgumentException();
            if( GetGitSnapshot() != null ) throw new InvalidOperationException();
            _generalState.Add( e );
        }

        /// <summary>
        /// Clears the <see cref="XmlNames.xGitSnapshot"/> element.
        /// There must be a current snapshot otherwise an <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        public void ClearGitSnapshot()
        {
            var g = GetGitSnapshot();
            if( g == null ) throw new InvalidOperationException();
            g.Remove();
            Debug.Assert( GetGitSnapshot() == null );
        }

        /// <summary>
        /// Sets a build result, updating <see cref="LastBuildType"/>.
        /// </summary>
        /// <param name="r">The build result.</param>
        public void SetBuildResult( BuildResult r )
        {
            if( r == null ) throw new ArgumentNullException( nameof( r ) );
            if( r.Type == BuildResultType.None ) throw new ArgumentException( nameof( r ) );
            LastBuildType = r.Type;
            _lastBuild.SetAttributeValue( XmlNames.xType, r.Type.ToString() );
            var rXml = r.ToXml();
            switch( r.Type )
            {
                case BuildResultType.Local: _localBuildResult.ReplaceElementByName( rXml ); break;
                case BuildResultType.CI: _ciBuildResult.ReplaceElementByName( rXml ); break;
                case BuildResultType.Release: _releaseBuildResult.ReplaceElementByName( rXml ); break;
            }
            _buildResults[(int)r.Type - 1] = r;
        }

        /// <summary>
        /// Gets the last build result for a build type or null if not found.
        /// </summary>
        /// <param name="type">The build type.</param>
        /// <returns>The build result or null.</returns>
        public BuildResult? GetBuildResult( BuildResultType type )
        {
            if( type == BuildResultType.None ) return null;
            if( _buildResults[(int)type - 1] == null )
            {
                XElement? e = GetParentBuildResultElement( type );
                e = e?.Elements().FirstOrDefault();
                if( e != null ) _buildResults[(int)type - 1] = new BuildResult( e );
            }
            return _buildResults[(int)type - 1];
        }

        XElement? GetParentBuildResultElement( BuildResultType type )
        {
            switch( type )
            {
                case BuildResultType.Local: return _localBuildResult;
                case BuildResultType.CI: return _ciBuildResult;
                case BuildResultType.Release: return _releaseBuildResult;
            }
            return null;
        }

        /// <summary>
        /// Publishes the build result for a build type: transfers the current xml result
        /// to the PublishedBuildHistory element.
        /// </summary>
        /// <param name="type">The build type.</param>
        public void PublishBuildResult( BuildResultType type )
        {
            if( type == BuildResultType.None ) throw new ArgumentException();
            var b = _buildResults[(int)type - 1];
            if( b == null ) throw new InvalidOperationException( $"No current BuildResultType '{type}'." );
            var e = GetParentBuildResultElement( type );
            var xmlBuild = e?.Elements().FirstOrDefault();
            if( xmlBuild != null )
            {
                _publishedBuildHistory.Add( xmlBuild );
                xmlBuild.Remove();
            }
        }

        /// <summary>
        /// Gets the last build type.
        /// </summary>
        public BuildResultType LastBuildType { get; private set; }


    }
}
