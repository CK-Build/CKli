using System;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Interactive
    {
        sealed class FooterDisplay
        {
            readonly Interactive _interactive;

            public FooterDisplay( Interactive interactive )
            {
                _interactive = interactive;
            }

            internal void Render()
            {
            }

            internal void Reset()
            {
            }
        }
    }
}
