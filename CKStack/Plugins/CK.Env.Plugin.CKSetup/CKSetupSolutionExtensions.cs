using CK.Env.DependencyModel;

namespace CK.Env
{
    public static class CKSetupSolutionExtensions
    {
        internal sealed class Marker { }
        internal readonly static Marker _marker = new Marker();

        public static bool UseCKSetup( this ISolution s ) => s.Tag<Marker>() != null;
    }
}
