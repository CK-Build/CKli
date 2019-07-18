using CK.Core;
using System;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates shared <see cref="XDocument"/> state.
    /// </summary>
    public class SharedWorldState : BaseWorldState
    {
        public SharedWorldState( WorldStore store, IWorldName w, XDocument d = null )
            : base( store, w, false, d )
        {
        }       

    }
}
