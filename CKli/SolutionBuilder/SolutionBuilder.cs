using CK.Build;
using CK.Core;
using CK.Env;
using CK.Env.DependencyModel;
using CK.Env.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli
{
    class SolutionBuilder
    {
        public static SolutionContext? LoadSolutions( IActivityMonitor monitor,
                                                      ArtifactCenter artifactCenter,
                                                      FileSystem fs,
                                                      string branchName,
                                                      ISolutionProvider[] providers )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
            var solutions = new SolutionContext();
            NormalizedPath branch = $"branches/{branchName}";
            foreach( var r in fs.GitFolders )
            {
                var s = solutions.AddSolution( r.DisplayPath, r.DisplayPath.LastPart + ".solution" );
                foreach( var p in providers )
                {
                    if( !p.Configure( monitor, r, r.DisplayPath.Combine( branch ), s ) ) return null;
                }
                SynchronizeSources( monitor, artifactCenter, s );
                SynchronizeArtifactTargets( monitor, artifactCenter, s );
            }
            return solutions;

            static void SynchronizeSources( IActivityMonitor monitor, ArtifactCenter artifactCenter, Solution solution )
            {
                var requiredFeedTypes = new HashSet<ArtifactType>( solution.AllPackageReferences.Select( r => r.Target.Artifact.Type! ) );
                foreach( var feed in artifactCenter.Feeds )
                {
                    if( requiredFeedTypes.Contains( feed.ArtifactType ) )
                    {
                        if( solution.AddArtifactSource( feed ) )
                        {
                            monitor.Info( $"Added feed '{feed}' to {solution}." );
                        }
                    }
                    else
                    {
                        if( solution.RemoveArtifactSource( feed ) )
                        {
                            monitor.Info( $"Removed feed '{feed}' from {solution}." );
                        }
                    }
                }
            }

            static void SynchronizeArtifactTargets( IActivityMonitor monitor, ArtifactCenter artifactCenter, Solution solution )
            {
                var requiredRepostoryTypes = new HashSet<ArtifactType>( solution.GeneratedArtifacts.Select( g => g.Artifact.Type! ) );
                foreach( var repository in artifactCenter.Repositories )
                {
                    if( requiredRepostoryTypes.Any( t => repository.HandleArtifactType( t ) ) )
                    {
                        if( solution.AddArtifactTarget( repository ) )
                        {
                            monitor.Info( $"Added repository '{repository}' to {solution}." );
                        }
                    }
                    else
                    {
                        if( solution.RemoveArtifactTarget( repository ) )
                        {
                            monitor.Info( $"Removed repository '{repository}' from {solution}." );
                        }
                    }
                }
            }

        }
    }

}

