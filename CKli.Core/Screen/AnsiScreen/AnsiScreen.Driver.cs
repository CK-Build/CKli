using CK.Core;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed class Driver : InteractiveScreen.Driver
    {
        readonly AnsiScreen _screen;

        public Driver( AnsiScreen screen, CKliEnv initialContext )
            : base( screen, initialContext, screen._target )
        {
            _screen = screen;
        }

        protected internal override int UpdateScreenWidth()
        {
            _screen._width = ConsoleScreen.GetWindowWidth();
            _screen._animation.Hide();
            return _screen._width;
        }
    }

}
