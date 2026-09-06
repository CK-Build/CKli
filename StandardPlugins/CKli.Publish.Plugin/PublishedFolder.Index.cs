using CK.Core;
using CK.Packaging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CKli.Publish.Plugin;

public sealed partial class PublishedFolder
{
    /// <summary>
    /// The name of the index file at the <see cref="RootPath"/>: "index.json".
    /// <para>
    /// This is not a profile file name (a profile file is named after its version), so it is skipped
    /// by the folder's own file discovery and cannot hide a profile.
    /// </para>
    /// </summary>
    public const string IndexFileName = "index.json";

    /// <summary>
    /// The index group name of the stable versions (and their CI builds), whose
    /// <see cref="SVersion.BranchName"/> is the empty string: "(stable)".
    /// <para>
    /// This is deliberately NOT the World's root branch name - that one is the BranchModel's business and
    /// can be renamed (an LTS World has its own). The index names the versions it contains, not the
    /// branches of any particular World.
    /// </para>
    /// </summary>
    public const string StableGroupName = "(stable)";

    // Same options as PublishedProfile.ToUtf8Bytes: an explicit "\r\n" so that the file is the same on any
    // platform (JsonWriterOptions defaults to Environment.NewLine), and the relaxed encoder because this is
    // a file, never embedded in a html page nor in a script - it keeps the '+' of a build metadata readable.
    static readonly JsonWriterOptions _indexOptions = new JsonWriterOptions
    {
        Indented = true,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Gets the full path of the <see cref="IndexFileName"/> file.
    /// </summary>
    public string IndexFilePath => _rootPath + IndexFileName;

    /// <summary>
    /// Gets the utf-8 Json index of this folder: the "Alive" and "Deprecated" profile versions, each one
    /// grouped by branch and ordered from the latest to the oldest. All the files are read.
    /// <para>
    /// This is what <see cref="Save"/> writes to <see cref="IndexFilePath"/>. The index is a projection of
    /// the profile files and nothing here ever reads it back: it exists for whoever looks at the folder
    /// from the outside.
    /// </para>
    /// </summary>
    /// <returns>The utf-8 Json bytes.</returns>
    public byte[] CreateIndexUtf8Bytes()
    {
        LoadAll();
        var buffer = new ArrayBufferWriter<byte>();
        using( var w = new Utf8JsonWriter( buffer, _indexOptions ) )
        {
            w.WriteStartObject();
            WriteGroups( w, "Alive", Current().Where( p => !p.IsDeprecated ) );
            WriteGroups( w, "Deprecated", Current().Where( p => p.IsDeprecated ) );
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();

        IEnumerable<PublishedProfile> Current() => _profiles.Values.Where( i => i.Current != null ).Select( i => i.Current! );

        static void WriteGroups( Utf8JsonWriter w, string setName, IEnumerable<PublishedProfile> profiles )
        {
            // Ordinal order puts "(stable)" first: '(' precedes every letter a branch name can start with.
            var groups = new SortedDictionary<string, List<SVersion>>( StringComparer.Ordinal )
            {
                { StableGroupName, new List<SVersion>() }
            };
            foreach( var p in profiles )
            {
                // A profile's version is a Conformant SVersion (GetProfilePath, that placed its file,
                // throws otherwise), so BranchName is never null here.
                var branchName = p.Version.BranchName;
                Throw.DebugAssert( branchName != null );
                var name = branchName.Length == 0 ? StableGroupName : branchName;
                if( !groups.TryGetValue( name, out var versions ) )
                {
                    groups.Add( name, versions = new List<SVersion>() );
                }
                versions.Add( p.Version );
            }
            w.WriteStartObject( setName );
            // A group exists only because a version landed in it, so a branch with no version is absent by
            // construction. "(stable)" is the exception: it is seeded above and appears even when empty.
            foreach( var (name, versions) in groups )
            {
                w.WriteStartArray( name );
                versions.Sort( static ( v1, v2 ) => v2.CompareTo( v1 ) );
                foreach( var v in versions )
                {
                    w.WriteStringValue( v.ToString() );
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
    }

    // Called by Save once the profile files are up to date: the index reflects them, so it is written last
    // and only when at least one of them actually changed.
    void WriteIndex() => File.WriteAllBytes( IndexFilePath, CreateIndexUtf8Bytes() );
}
