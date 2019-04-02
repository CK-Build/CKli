using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;

namespace CK.Env.NPM
{
    /// <summary>
    /// Central root class that manages <see cref="NPMProject"/> loading and caching.
    /// </summary>
    public class NPMContext
    {
        readonly Dictionary<NormalizedPath, NPMProject> _projects;

        public NPMContext( FileSystem f )
        {
            FileSystem = f;
            _projects = new Dictionary<NormalizedPath, NPMProject>();
        }

        public FileSystem FileSystem { get; }

        public NPMProject Ensure( IActivityMonitor m, INPMProjectDescription description )
        {
            if( _projects.TryGetValue( description.FullPath, out var p ) )
            {
                p.UpdateDescription( m, description );
                return p;
            }
            p = new NPMProject( this, m, description );
            _projects.Add( description.FullPath, p );
            return p;
        }
    }
}
