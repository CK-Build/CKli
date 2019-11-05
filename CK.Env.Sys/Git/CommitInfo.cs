namespace CK.Env
{
    public class CommitInfo
    {
        public CommitInfo( string message, string sha )
        {
            Message = message;
            Sha = sha;
        }
        public string Message { get; set; }
        public string Sha { get; set; }
    }
}
