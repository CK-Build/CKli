using CK.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Centralizes known stacks: each time a <see cref="StackRoot"/> is created it is added to the
    /// <see cref="KnownStacks"/> list. The list allows duplicates of <see cref="StackInfo.StackUrl"/>.
    /// The factory <see cref="Load(IActivityMonitor, NormalizedPath)"/> method cleans up <see cref="StackInfo.RootPath"/>
    /// that have been deleted.
    /// </summary>
    public sealed class StackRootRegistry
    {
        public sealed class StackInfo
        {
            internal StackInfo( StackRepository stackRepository )
            {
                GitRepositoryPath = stackRepository.Path;
                RootPath = stackRepository.StackRoot;
                StackUrl = stackRepository.OriginUrl;
                IsPublic = stackRepository.IsPublic;
                WorldDefinitions = stackRepository.WorldDefinitions;
            }

            public NormalizedPath GitRepositoryPath { get; }
            public NormalizedPath RootPath { get; }
            public string StackName => RootPath.LastPart;
            public Uri StackUrl { get; }
            public bool IsPublic { get; }
            public IReadOnlyList<LocalWorldName> WorldDefinitions { get; }

            internal void Write( StringBuilder b )
            {
                b.Append( GitRepositoryPath )
                 .Append( '*' )
                 .Append( StackUrl.ToString() )
                 .AppendLine();
            }

            StackInfo( NormalizedPath gitRepositoryPath, Uri stackUrl, IReadOnlyList<LocalWorldName> worlds )
            {
                GitRepositoryPath = gitRepositoryPath;
                RootPath = gitRepositoryPath.RemoveLastPart();
                StackUrl = stackUrl;
                IsPublic = gitRepositoryPath.LastPart == StackRepository.PublicStackName;
                WorldDefinitions = worlds;
            }

            internal static StackInfo Read( IActivityMonitor monitor, string line )
            {
                var s = line.Split( '*', StringSplitOptions.TrimEntries );
                var gitPath = new NormalizedPath( s[0] );
                if( gitPath.Parts.Count <= 3 ) Throw.InvalidDataException( $"Too short path: '{gitPath}'." );
                if( gitPath.LastPart != StackRepository.PublicStackName && gitPath.LastPart != StackRepository.PrivateStackName )
                {
                    Throw.InvalidDataException( $"Invalid path: '{gitPath}'. Must end with '{StackRepository.PublicStackName}' or '{StackRepository.PrivateStackName}'." );
                }
                var url = new Uri( s[1], UriKind.Absolute );
                return new StackInfo( gitPath, url, StackRepository.ReadWorlds( gitPath ) );
            }
        }

        readonly NormalizedPath _regPath;
        readonly List<StackInfo> _stacks;

        StackRootRegistry( NormalizedPath regPath, List<StackInfo> stacks )
        {
            _regPath = regPath;
            _stacks = stacks;
        }

        /// <summary>
        /// Gets the stacks that have been registered.
        /// </summary>
        public IReadOnlyList<StackInfo> KnownStacks => _stacks;

        /// <summary>
        /// Builds detailed information of <see cref="KnownStacks"/> including duplicates per stack name.
        /// The primary one is the first cloned one.
        /// </summary>
        /// <returns>A list with the primary and duplicates <see cref="StackInfo"/></returns>
        public IEnumerable<(StackInfo Primary,
                            IEnumerable<(LocalWorldName World, bool Cloned)> PrimaryWorlds,
                            IEnumerable<(StackInfo Stack, bool BadUrl, bool BadPrivacy)> Duplicates)> GetListInfo()
        {
            return _stacks.Select( ( s, idx ) => (Stack: s, Index: idx) )
                          .GroupBy( s => s.Stack.RootPath.LastPart )
                          .Select( g => (PrimaryAndIndex: g.OrderBy( x => x.Index ).Select( x => (x.Stack, x.Index) ).First(),
                                         Duplicates: g.OrderBy( x => x.Index ).Skip( 1 ).Select( d => d.Stack )) )
                          .OrderBy( x => x.PrimaryAndIndex.Index )
                          .Select( g => (Primary: g.PrimaryAndIndex.Stack,
                                         PrimaryWorlds: g.PrimaryAndIndex.Stack.WorldDefinitions.Select( w => (w, w.ParallelName == null || Directory.Exists( w.Root )) ),          
                                         Duplicates: g.Duplicates.Select( d => (Stack: d,
                                                                                BadUrl: d.StackUrl != g.PrimaryAndIndex.Stack.StackUrl,
                                                                                BadPrivacy: d.IsPublic != g.PrimaryAndIndex.Stack.IsPublic) ) ) )
                          .ToList();
        }

        internal void OnCreated( StackRoot stack )
        {
            Debug.Assert( _stacks.All( s => s.GitRepositoryPath != stack.StackRepository.Path ) );
            _stacks.Add( new StackInfo( stack.StackRepository ) );
            Save();
        }

        void Save()
        {
            var b = new StringBuilder();
            foreach( var s in _stacks ) s.Write( b );
            File.WriteAllText( _regPath, b.ToString() );
        }

        public static StackRootRegistry Load( IActivityMonitor monitor, NormalizedPath userHostPath )
        {
            var stacks = new List<StackInfo>();
            var regPath = userHostPath.AppendPart( "StackRootRegistry.v0.txt" );
            bool mustSave = false;
            if( File.Exists( regPath ) )
            {
                foreach( var line in File.ReadLines( regPath ) )
                {
                    try
                    {
                        var s = StackInfo.Read( monitor, line );
                        if( !Directory.Exists( s.GitRepositoryPath ) )
                        {
                            monitor.Info( $"Stack '{s.GitRepositoryPath}' has been deleted." );
                        }
                        else
                        {
                            stacks.Add( s );
                        }
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( $"While reading line '{line}' from '{regPath}'. This faulty line will be deleted.", ex );
                        mustSave = true;
                    }
                }
            }
            var r = new StackRootRegistry( regPath, stacks );
            if( mustSave ) r.Save();
            return r;
        }
    }
}
