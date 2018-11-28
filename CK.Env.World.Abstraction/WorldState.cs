using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// 
    /// </summary>
    public abstract class WorldState : IWorldState
    {
        readonly IWorldStore _store;
        readonly RawXmlWorldState _rawState;
        bool _isDirty;

        /// <summary>
        /// Initializes a new WorldState.
        /// </summary>
        /// <param name="store">The store. Can not be null.</param>
        /// <param name="rawState">The raw state. Can not be null.</param>
        protected WorldState( IWorldStore store, RawXmlWorldState rawState )
        {
            if( store == null ) throw new ArgumentNullException( nameof( store ) );
            if( rawState == null ) throw new ArgumentNullException( nameof( rawState ) );
            _store = store;
            _rawState = rawState;
            Debug.Assert( ((int[])Enum.GetValues( typeof( GlobalWorkStatus ) )).SequenceEqual( Enumerable.Range( 0, 7 ) ) );
            _roWorkState = new XElement[7];
            SetReadonlyState();
            _rawState.Document.Changed += RawStateChanged;
        }

        void RawStateChanged( object sender, XObjectChangeEventArgs e )
        {
            _isDirty = true;
            SetReadonlyState();
        }

        /// <summary>
        /// Gets whether this state is dirty.
        /// </summary>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// Saves this state. If <see cref="IsDirty"/> is false, nothing is done.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Save( IActivityMonitor m )
        {
            if( _isDirty )
            {
                if( !_store.SetLocalState( m, _rawState ) ) return false;
                _isDirty = false;
                SetReadonlyState();
            }
            return true;
        }

        /// <summary>
        /// Gets the world name.
        /// </summary>
        public IWorldName WorldName => _rawState.World;

        /// <summary>
        /// Gets the current git status that applies to the whole world.
        /// </summary>
        public StandardGitStatus GlobalGitStatus => _rawState.GlobalGitStatus;

        /// <summary>
        /// Gets the global work status.
        /// </summary>
        public GlobalWorkStatus WorkStatus => _rawState.WorkStatus;

        /// <summary>
        /// Gets the operation name (when <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.OtherOperation"/>).
        /// </summary>
        public string OtherOperationName => _rawState.OtherOperationName;

        /// <summary>
        /// Gets the mutable <see cref="XElement"/> general state.
        /// This is where state information that are not specific to an operation are stored.
        /// </summary>
        public XElement GeneralState => _rawState.GeneralState;

        /// <summary>
        /// Gets the mutable <see cref="XElement"/> state for an operation.
        /// </summary>
        /// <param name="status">The work status.</param>
        /// <returns>The state element.</returns>
        public XElement GetWorkState( GlobalWorkStatus status ) => _rawState.GetWorkState( status );

        void SetWorkStatus( GlobalWorkStatus s, string otherOperationName = null )
        {
            if( s == GlobalWorkStatus.OtherOperation == String.IsNullOrWhiteSpace( otherOperationName ) )
            {
                throw new ArgumentException( $"Incompatible operation name.", nameof( otherOperationName ) );
            }
            if( WorkStatus != s || OtherOperationName != otherOperationName )
            {
                _rawState.WorkStatus = s;
                _rawState.OtherOperationName = otherOperationName;
            }
        }

        bool SetWorkStatusAndSave( IActivityMonitor m, GlobalWorkStatus s, string otherOperationName = null )
        {
            SetWorkStatus( s, otherOperationName );
            return Save( m );
        }

        /// <summary>
        /// Gets whether the <see cref="WorkStatus"/> is not <see cref="GlobalWorkStatus.Idle"/>.
        /// </summary>
        public bool IsConcludeCurrentWorkEnabled => WorkStatus != GlobalWorkStatus.Idle;

        /// <summary>
        /// Concludes the current work.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ConcludeCurrentWork( IActivityMonitor m )
        {
            switch( WorkStatus )
            {
                case GlobalWorkStatus.SwitchingToLocal: if( !DoSwitchToLocal( m ) ) return false; break;
                case GlobalWorkStatus.SwitchingToDevelop: if( !DoSwitchToDevelop( m ) ) return false; break;
                case GlobalWorkStatus.Releasing: if( !DoReleasing( m ) ) return false; break;
                case GlobalWorkStatus.CancellingRelease: if( !DoCancellingRelease( m ) ) return false; break;
                case GlobalWorkStatus.PublishingRelease: if( !DoPublishingRelease( m ) ) return false; break;
                case GlobalWorkStatus.OtherOperation: if( !DoOtherOperation( m ) ) return false; break;
                default: throw new InvalidOperationException( nameof( IsConcludeCurrentWorkEnabled ) );
            }
            m.Info( $"Work done. Current Status: {WorkStatus} / {GlobalGitStatus}." );
            return true;
        }

        protected abstract bool DoSwitchToLocal( IActivityMonitor m );
        protected abstract bool DoSwitchToDevelop( IActivityMonitor m );
        protected abstract bool DoReleasing( IActivityMonitor m );
        protected abstract bool DoCancellingRelease( IActivityMonitor m );
        protected abstract bool DoPublishingRelease( IActivityMonitor m );
        protected abstract bool DoOtherOperation( IActivityMonitor m );

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="GlobalGitStatus"/>
        /// is <see cref="StandardGitStatus.DevelopBranch"/>.
        /// </summary>
        public bool CanSwitchToLocal => WorkStatus == GlobalWorkStatus.Idle && GlobalGitStatus == StandardGitStatus.DevelopBranch;

        /// <summary>
        /// Switches back from develop to local branch.
        /// Must throw an <see cref="InvalidOperationException"/> if <see cref="CanSwitchToLocal"/> is false.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchToLocal( IActivityMonitor m )
        {
            if( !CanSwitchToLocal ) throw new InvalidOperationException( nameof( CanSwitchToLocal ) );
            return SetWorkStatusAndSave( m, GlobalWorkStatus.SwitchingToLocal ) && ConcludeCurrentWork( m );
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="GlobalGitStatus"/>
        /// is <see cref="StandardGitStatus.LocalBranch"/>.
        /// </summary>
        public bool CanSwitchToDevelop => WorkStatus == GlobalWorkStatus.Idle && GlobalGitStatus == StandardGitStatus.LocalBranch;

        /// <summary>
        /// Switches back from local to develop branch.
        /// Must throw an <see cref="InvalidOperationException"/> if <see cref="CanSwitchToDevelop"/> is false.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchToDevelop( IActivityMonitor m )
        {
            if( !CanSwitchToDevelop ) throw new InvalidOperationException( nameof( CanSwitchToDevelop ) );
            return SetWorkStatusAndSave( m, GlobalWorkStatus.SwitchingToDevelop ) && ConcludeCurrentWork( m );
        }

        public bool CanRelease => WorkStatus == GlobalWorkStatus.Idle && GlobalGitStatus == StandardGitStatus.DevelopBranch;

        /// <summary>
        /// Starts a release after an optional pull.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="pull">Pull all branches first.</param>
        /// <returns>True on success, false on error.</returns>
        public abstract bool Release( IActivityMonitor m, IReleaseVersionSelector versionSelector, bool pull = true );

        /// <summary>
        /// Gets whether <see cref="CancelRelease"/> can be called.
        /// </summary>
        public bool CanCancelRelease => WorkStatus == GlobalWorkStatus.Releasing || WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;

        /// <summary>
        /// Cancel the current release.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool CancelRelease( IActivityMonitor m )
        {
            if( !CanCancelRelease ) throw new InvalidOperationException( nameof( CanCancelRelease ) );
            return SetWorkStatusAndSave( m, GlobalWorkStatus.CancellingRelease ) && ConcludeCurrentWork( m );
        }

        /// <summary>
        /// Release can be published when <see cref="GlobalWorkStatus.WaitingReleaseConfirmation"/>.
        /// </summary>
        public bool CanPublishRelease => WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;

        public bool PublishRelease( IActivityMonitor m )
        {
            if( !CanPublishRelease ) throw new InvalidOperationException( nameof( CanPublishRelease ) );
            return DoPublishingRelease( m );
        }

        #region Read only State

        StandardGitStatus _roGlobalGitStatus;
        GlobalWorkStatus _roGlobalWorkStatus;
        string _roOtherOperationName;
        XElement _roGeneralState;
        readonly XElement[] _roWorkState;

        void SetReadonlyState()
        {
            _roGlobalGitStatus = _rawState.GlobalGitStatus;
            _roGlobalWorkStatus = _rawState.WorkStatus;
            _roOtherOperationName = _rawState.OtherOperationName;
            _roGeneralState = new XElement( _rawState.GeneralState );
            _roGeneralState.Changing += PreventChanges;
            for( int i = 0; i < 7; i++ )
            {
                _roWorkState[i] = new XElement( _rawState.GetWorkState( (GlobalWorkStatus)i ) );
                _roWorkState[i].Changing += PreventChanges;
            }
        }

        static void PreventChanges( object sender, XObjectChangeEventArgs e )
        {
            throw new InvalidOperationException( "XElement is read-only." );
        }

        StandardGitStatus IWorldState.GlobalGitStatus => _roGlobalGitStatus;

        GlobalWorkStatus IWorldState.WorkStatus => _roGlobalWorkStatus;

        string IWorldState.OtherOperationName => _roOtherOperationName;

        XElement IWorldState.GeneralState => _roGeneralState;

        XElement IWorldState.GetWorkState( GlobalWorkStatus status ) => _roWorkState[(int)status];

        #endregion

    }

}
