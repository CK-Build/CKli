namespace CK.IniFile.SpecificImplementation
{
    public class NpmrcFile : IniFile<NpmrcFormat, NpmrcLine>
    {
        public NpmrcFile( NpmrcFormat format ) : base( format )
        {
        }


        public static NpmrcFile FromText(string text)
        {
            return (NpmrcFile)FromText( text, NpmrcFormat.DefaultConfig );
        }
    }
}
