using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using CKSetup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli
{
    public class XGlobalSwitchBranchAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;
        readonly XPublishedPackageFeeds _localPackages;

        public XGlobalSwitchBranchAction(
            Initializer intializer,
            FileSystem fileSystem,
            XPublishedPackageFeeds localPackages,
            ActionCollector collector,
            XSolutionCentral solutions )
            : base( intializer, collector )
        {
            _fileSystem = fileSystem;
            _solutions = solutions;
            _localPackages = localPackages;
        }

        public override bool Run( IActivityMonitor m )
        {
            // Consider all GitFolders that contains at least a solution definition in 'develop' branch.
            var gitFolders = _solutions.AllDevelopSolutions.Select( s => s.GitBranch.Parent.GitFolder ).Distinct().ToList();
            var byActiveBranch = gitFolders.GroupBy( g => g.CurrentBranchName );
            if( byActiveBranch.Count() > 1 )
            {
                using( m.OpenWarn( $"{gitFolders.Count} git folders are not on the same branch. All folders must be on 'develop' or '{GitFolder.BlanckDevBranchName}' to switch." ) )
                {
                    foreach( var b in byActiveBranch )
                    {
                        m.Info( $"On branch '{b.Key}': {b.Select( g => g.SubPath.Path ).Concatenate()}" );
                    }
                }
                return true;
            }
            string current = byActiveBranch.Single().Key;
            if( current == "develop" )
            {
                Console.Write( $"Currently on 'develop'. Switch to '{GitFolder.BlanckDevBranchName}'? (Y/N):" );
                string a;
                while( (a = Console.ReadLine()) != "Y" && a != "N" ) ;
                if( a == "Y" )
                {
                    foreach( var g in gitFolders )
                    {
                        if( !g.SwitchFromDevelopToBlankDev( m ) ) return false;
                    }
                }
            }
            else if( current == GitFolder.BlanckDevBranchName )
            {
                Console.Write( $"Currently on '{GitFolder.BlanckDevBranchName}'. Switch to 'develop'? (Y/N):" );
                string a;
                while( (a = Console.ReadLine()) != "Y" && a != "N" ) ;
                if( a == "Y" )
                {
                    foreach( var g in gitFolders )
                    {
                        if( !g.SwitchFromBlankDevToDevelop( m ) ) return false;
                    }
                }
            }
            else
            {
                m.Warn( $"Git folders must be on 'develop' or '{GitFolder.BlanckDevBranchName}', not '{current}'." );
            }
            return true;
        }

    }
}
