using System.Collections.Generic;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Interactive
    {
        sealed class HeaderDisplay
        {
            readonly Interactive _interactive;
            List<IRenderable> _logs;

            public HeaderDisplay( Interactive interactive )
            {
                _interactive = interactive;
                _logs = new List<IRenderable>();
            }

            public void AddLog( IRenderable r )
            {
                _logs.Add( r );
            }

            public void Reset()
            {
                _logs.Clear();
            }

            public void Render()
            {
                // Temporary
                foreach( var log in _logs )
                {
                    _interactive._screen.Display( log );
                }
            }

        }
    }
}
