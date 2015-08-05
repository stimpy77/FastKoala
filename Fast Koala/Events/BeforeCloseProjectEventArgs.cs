using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wijits.FastKoala.Events
{
    public class BeforeCloseProjectEventArgs
    {
        public EnvDTE.Project Project { get; set; }

        public Microsoft.VisualStudio.Shell.Interop.IVsHierarchy Hierarchy { get; set; }

        public bool Removed { get; set; }
    }
}
