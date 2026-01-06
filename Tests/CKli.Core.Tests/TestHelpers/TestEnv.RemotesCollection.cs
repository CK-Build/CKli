using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core.Tests;

static partial class TestEnv
{
    public sealed partial class RemotesCollection
    {
        readonly string _name;
        readonly string[] _repositoryNames;
        readonly Uri _stackUri;

        internal RemotesCollection( string name, string[] repositoryNames )
        {
            _name = name;
            _repositoryNames = repositoryNames;
            _stackUri = GetUriFor( _name + "-Stack" );
        }

        public string Name => _name;

        public Uri StackUri => _stackUri;

        public IReadOnlyList<string> Repositories => _repositoryNames;

        public Uri GetUriFor( string repositoryName )
        {
            if( _repositoryNames.Contains( repositoryName ) )
            {
                return new Uri( _barePath.AppendPart( _name ).AppendPart( repositoryName ) );
            }
            return new Uri( "file:///Missing '" + repositoryName + "' repository in '" + _name + "' remotes" );
        }

        public override string ToString() => $"{_name} - {_repositoryNames.Length} repositories";
    }

}
