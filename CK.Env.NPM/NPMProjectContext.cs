using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;

namespace CK.Env.NPM
{
    /// <summary>
    /// Central root class that manages <see cref="NPMProject"/> loading and caching.
    /// </summary>
    public class NPMProjectContext
    {
        readonly Dictionary<NormalizedPath, NPMProject> _projects;

        public NPMProjectContext( FileSystem f )
        {
            FileSystem = f;
            _projects = new Dictionary<NormalizedPath, NPMProject>();
        }

        public FileSystem FileSystem { get; }

        public NPMProject Ensure( IActivityMonitor m, INPMProjectSpec description )
        {
            if( _projects.TryGetValue( description.FullPath, out var p ) )
            {
                return p;
            }
            p = new NPMProject( this, m, description );
            _projects.Add( description.FullPath, p );
            return p;
        }
    }
}
