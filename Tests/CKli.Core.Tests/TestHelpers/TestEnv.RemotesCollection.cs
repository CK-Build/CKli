using CK.Core;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
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
            var safe = _repositoryNames.Contains( repositoryName )
                            ? $"file:///{_barePath}/{_name}/{repositoryName}"
                            : $"file:///Missing '{repositoryName}' repository in '{_name}' remotes";
            return new Uri( safe, UriKind.Absolute );
        }

        public override string ToString() => $"{_name} - {_repositoryNames.Length} repositories";
    }

}
