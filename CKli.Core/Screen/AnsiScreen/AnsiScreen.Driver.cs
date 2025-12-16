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

        protected internal override void HideAnimation( out int screenWidth, out VerticalContent? logs )
        {
            logs = _screen._animation.ClearHeader();
            _screen._animation.Hide( false );
            screenWidth = _screen._animation.ScreenWidth;
        }
    }

}
