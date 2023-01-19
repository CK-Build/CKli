using CK.Core;
using CK.Env;
using System;
using System.Reflection;
using System.Xml.Linq;

namespace CKli
{
    /// <summary>
    /// Implements mappings for the types defined in this assembly.
    /// </summary>
    public sealed class CoreTypes : IXTypedMap
    {
        readonly Type _worldType;

        public CoreTypes( Type worldType )
        {
            _worldType = worldType;
        }

        public Type? GetNameMappping( XName n )
        {
            return n.LocalName switch
            {
                "LoadLibrary" => typeof( XLoadLibrary ),

                "ArtifactCenter" => typeof( XArtifactCenter ),
                "LocalFeedProvider" => typeof( XLocalFeedProvider ),
                "SharedHttpClient" => typeof( XSharedHttpClient ),
                "Artifacts" => typeof( XArtifacts ),
                "Branch" => typeof( XBranch ),
                "BuildProjectSpec" => typeof( XBuildProjectSpec ),
                "GitFolder" => typeof( XGitFolder ),
                "File" => typeof( XPathItem ),
                "Folder" => typeof( XPathItem ),
                "SharedSolutionSpec" => typeof( XSharedSolutionSpec ),
                "SolutionSpec" => typeof( XSolutionSpec ),
                "WorldSecrets" => typeof( XWorldSecrets ),
                "World" => _worldType,
                _ => null
            };
        }

        public bool HasAlreadyRegistered( Assembly a ) => a == typeof( XWorldBase<> ).Assembly || a == typeof( XLoadLibrary ).Assembly;
    }
}
