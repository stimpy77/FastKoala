namespace Wijits.FastKoala.Events
{
    public class AfterLoadProjectEventArgs
    {
        public EnvDTE.Project Project { get; set; }

        public Microsoft.VisualStudio.Shell.Interop.IVsHierarchy RealHierarchy { get; set; }

        public Microsoft.VisualStudio.Shell.Interop.IVsHierarchy StubHierarchy { get; set; }
    }
}
