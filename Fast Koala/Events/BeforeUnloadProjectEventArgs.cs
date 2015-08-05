using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wijits.FastKoala.Events
{
    public class BeforeUnloadProjectEventArgs
    {
        public EnvDTE.Project Project { get; set; }

        public Microsoft.VisualStudio.Shell.Interop.IVsHierarchy RealHierarchy { get; set; }

        public Microsoft.VisualStudio.Shell.Interop.IVsHierarchy StubHierarchy { get; set; }
    }
}
