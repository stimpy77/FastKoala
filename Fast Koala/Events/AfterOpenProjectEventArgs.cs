namespace Wijits.FastKoala.Events
{
    public class AfterOpenProjectEventArgs
    {
        public Microsoft.VisualStudio.Shell.Interop.IVsHierarchy Hierarchy { get; set; }

        public bool Added { get; set; }

        public EnvDTE.Project Project { get; set; }
    }
}
