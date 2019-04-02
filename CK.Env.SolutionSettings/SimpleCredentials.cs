namespace CK.Env
{
    /// <summary>
    /// Encapsulates simple credentials.
    /// </summary>
    public class SimpleCredentials
    {
        /// <summary>
        /// Initializes a new simple credential.
        /// </summary>
        /// <param name="userName">The user name.</param>
        /// <param name="password">The password or secret.</param>
        public SimpleCredentials( string userName, string password )
        {
            UserName = userName;
            Password = password;
        }

        /// <summary>
        /// User name.
        /// </summary>
        public string UserName { get; }

        /// <summary>
        /// The pass word or secret.
        /// </summary>
        public string Password { get; }
    }
}
