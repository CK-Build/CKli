namespace CKli.Core;

public sealed class StringScreenType : ScreenType
{
    public static readonly ScreenType Default = new StringScreenType();

    StringScreenType()
        : base( false )
    {
    }
}
