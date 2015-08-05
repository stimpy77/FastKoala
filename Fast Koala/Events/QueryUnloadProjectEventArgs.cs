using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wijits.FastKoala.Events
{
    public class QueryUnloadProjectEventArgs
    {
        public EnvDTE.Project Project { get; set; }

        public bool Cancel { get; set; }
    }
}
