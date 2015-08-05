using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wijits.FastKoala.Events
{
    public class BeforeCloseSolutionEventArgs
    {
        public EnvDTE.Solution Solution { get; set; }
    }
}
