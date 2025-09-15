using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.Core.Tests;

static partial class Remotes
{
    sealed partial class RemotesCollection : IRemotesCollection
    {
        readonly string _name;
        readonly NormalizedPath _path;
        readonly Dictionary<string, NormalizedPath> _repositories;
        readonly Uri _stackUri;
        readonly bool _isReadOnly;

        internal RemotesCollection( NormalizedPath path, bool readOnly )
        {
            _name = Path.GetFileName( path );
            _path = path;
            _isReadOnly = readOnly;
            _repositories = new Dictionary<string, NormalizedPath>();
            foreach( var d in Directory.EnumerateDirectories( path ) )
            {
                var r = Path.GetFileName( d );
                _repositories.Add( r, d );
                if( readOnly )
                {
                    File.WriteAllText( Path.Combine( d, ".git", "hooks", "pre-receive.bat" ), "exit 1" );
                }
            }
            _stackUri = GetUriFor( _name + "-Stack" );
        }

        public string Name => _name;

        public bool IsReadOnly => _isReadOnly;

        public Uri StackUri => _stackUri;

        public IReadOnlyDictionary<string, NormalizedPath> Repositories => _repositories;

        public Uri GetUriFor( string repositoryName )
        {
            var path = _repositories.GetValueOrDefault( repositoryName );
            var safe =  path.IsEmptyPath
                            ? $"Missing '{repositoryName}' repository in '{_name}' remotes"
                            : path.Path;
            return new Uri( "file:///" + safe, UriKind.Absolute );
        }

        public override string ToString() => $"{_name} ({(_isReadOnly ? "read only" : "allow push")}) - {_repositories.Count} repositories";
    }

}
