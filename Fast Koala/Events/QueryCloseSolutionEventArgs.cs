using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wijits.FastKoala.Events
{
    public class QueryCloseSolutionEventArgs
    {
        private EnvDTE.Solution solution;
        private object pfCancel;

        public QueryCloseSolutionEventArgs(EnvDTE.Solution solution, ref int pfCancel)
        {
            // TODO: Complete member initialization
            this.solution = solution;
            this.pfCancel = pfCancel;
        }

        public EnvDTE.Solution Solution
        {
            get { return solution; }
        }

        public bool Cancel
        {
            get { return ((int) pfCancel) == 1; }
            set { pfCancel = value; }
        }
    }
}
