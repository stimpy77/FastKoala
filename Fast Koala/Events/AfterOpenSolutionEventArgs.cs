using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wijits.FastKoala.Events
{
    public class AfterOpenSolutionEventArgs
    {
        public bool NewSolution { get; set; }

        public EnvDTE.Solution Solution { get; set; }
    }
}
