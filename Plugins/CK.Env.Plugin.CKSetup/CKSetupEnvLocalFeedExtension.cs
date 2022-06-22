namespace CK.Env
{
    public static class CKSetupEnvLocalFeedExtension
    {
        public const string CKSetupStoreName = "CKSetupStore";

        public static string GetCKSetupStorePath( this IEnvLocalFeed @this )
        {
            return @this.PhysicalPath.AppendPart( CKSetupStoreName );
        }
    }
}
