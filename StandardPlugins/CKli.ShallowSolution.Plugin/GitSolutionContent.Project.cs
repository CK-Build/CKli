using CK.Core;
using System.Linq;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

public partial class GitSolutionContent
{
    /// <summary>
    /// Minimal project file.
    /// </summary>
    public sealed class Project
    {
        readonly NormalizedPath _path;
        readonly string _name;
        readonly XElement _root;
        bool _packableKnown;
        bool? _isPackable;

        internal Project( NormalizedPath path, XElement root )
        {
            _path = path;
            _name = path.LastPart[0..^7];
            _root = root;
        }

        /// <summary>
        /// Gets the path to the ".csproj" file in the solution.
        /// </summary>
        public NormalizedPath Path => _path;

        /// <summary>
        /// Gets the project name.
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Gets whether this project is packable: its <see cref="Name"/> is the produced package name.
        /// <para>
        /// This is null when no &lt;IsPackable&gt; element can be found.
        /// </para>
        /// </summary>
        public bool? IsPackable
        {
            get
            {
                if( !_packableKnown )
                {
                    _packableKnown = true;
                    _isPackable = (bool?)_root.Elements( XNames.PropertyGroup )
                                              .SelectMany( g => g.Elements( XNames.IsPackable ) )
                                              .FirstOrDefault();
                }
                return _isPackable;
            }
        }

    }


}

