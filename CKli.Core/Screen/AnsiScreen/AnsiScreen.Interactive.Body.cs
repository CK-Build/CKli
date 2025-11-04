using System;
using System.Collections.Generic;
using static System.Reflection.Metadata.BlobBuilder;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Interactive
    {
        sealed class BodyDisplay
        {
            readonly Interactive _interactive;
            List<IRenderable> _current;

            public BodyDisplay( Interactive interactive )
            {
                _interactive = interactive;
                _current = new List<IRenderable>();
            }

            public void Add( IRenderable r )
            {
                _current.Add( r );
            }

            public void Render()
            {
                // Temporary.
                foreach( var log in _current )
                {
                    _interactive._screen.Display( log );
                }
            }

            internal void Reset()
            {
                _current.Clear();
            }
        }
    }
}
