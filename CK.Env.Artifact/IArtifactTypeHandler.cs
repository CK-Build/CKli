using CK.Core;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Root abstraction that creates <see cref="IArtifactRepository"/> and <see cref="IArtifactFeed"/> of a certain kind.
    /// These handlers are registered in <see cref="ArtifactCenter"/>.
    /// </summary>
    public interface IArtifactTypeHandler
    {
        /// <summary>!
        /// Creates a repository from a <see cref="XElementReader"/> or returns null if the element
        /// cannot be handled.
        /// This must throw on error by using <see cref="XElementReader.ThrowXmlException(string)"/> so
        /// that position in the xml is visible.
        /// </summary>
        /// <param name="r">The element reader. The Element's <see cref="XElement.Name"/> may be ignored (or used).</param>
        /// <returns>The repository info or null.</returns>
        IArtifactRepository CreateRepository( in XElementReader r );

        /// <summary>
        /// Creates a source from a <see cref="XElementReader"/> or returns null if the element cannot be handled.
        /// This must throw on error by using <see cref="XElementReader.ThrowXmlException(string)"/> so
        /// that position in the xml is visible.
        /// </summary>
        /// <param name="r">The element reader. The Element's <see cref="XElement.Name"/> may be ignored (or used).</param>
        /// <param name="repositories">The repositories already initialized.</param>
        /// <param name="feeds">The feeds already initialized.</param>
        /// <returns>The artifact feed or null.</returns>
        IArtifactFeed CreateFeedFromXML( IActivityMonitor m, in XElementReader r, IReadOnlyList<IArtifactRepository> repositories, IReadOnlyList<IArtifactFeed> feeds );

    }
}
