using System.Xml.Linq;

namespace CK.Env.NuGet
{
    /// <summary>
    /// Immutable implementation of <see cref="INuGetRepositoryInfo"/> for standard feeds
    /// where a secret push API key is required.
    /// </summary>
    public class NuGetStandardFeedInfo : NuGetRepositoryInfo
    {
        public NuGetStandardFeedInfo( in XElementReader r )
            : base( r )
        {
            Name = r.HandleRequiredAttribute<string>( "Name" );
            Url = r.HandleRequiredAttribute<string>( "Url" );
            SecretKeyName = r.HandleRequiredAttribute<string>( "SecretKeyName" );
        }

        public override NuGetRepositoryType Type => NuGetRepositoryType.NuGetStandard;

        public override string Name { get; }

        public string Url { get; }

        public override string SecretKeyName { get; }

    }
}
