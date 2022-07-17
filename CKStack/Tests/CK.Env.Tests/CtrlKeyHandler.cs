namespace CK.Env.Tests
{
    static class CtrlKeyHandler
    {
        enum KeyCode : int
        {
            Control = 0x11,
            Shift = 0x10,
        }

        const int KeyPressedMask = 0x8000;

        [System.Runtime.InteropServices.DllImport( "user32.dll" )]
        static extern short GetKeyState( int key );

        static bool _callFailed;
        static bool IsKeyDown( KeyCode key ) => (GetKeyState( (int)key ) & KeyPressedMask) != 0;

        public static bool IsPressed
        {
            get
            {
                if( !_callFailed )
                {
                    try
                    {
                        return IsKeyDown( KeyCode.Control );
                    }
                    catch
                    {
                        _callFailed = true;
                    }
                }
                return false;
            }
        }
    }
}
