namespace CKli.Core;

public interface IScreen
{
    void DisplayError( string message );

    void DisplayWarning( string message );

    void OnLogText( string text );

    void HideSpin();
}
