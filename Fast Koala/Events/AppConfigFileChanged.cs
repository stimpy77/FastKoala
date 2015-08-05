using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.PlatformUI;

namespace Wijits.FastKoala.Events
{
    public class AppConfigFileChangedEventArgs
    {
        public AppConfigFileChangedEventArgs(EnvDTE.Project project, string appConfig)
        {
            Project = project;
            AppConfigFile = appConfig;
        }

        public string AppConfigFile { get; set; }

        public EnvDTE.Project Project { get; set; }
    }
}
