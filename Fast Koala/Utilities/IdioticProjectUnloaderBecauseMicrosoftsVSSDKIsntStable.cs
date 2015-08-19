using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;

namespace Wijits.FastKoala.Utilities
{
    public class IdioticProjectUnloaderBecauseMicrosoftsVSSDKIsntStable : IDisposable
    {
        private bool unloaded;
        private DTE dte;
        private string projectName;
        private string projectUniqueName;

        public IdioticProjectUnloaderBecauseMicrosoftsVSSDKIsntStable(Project project)
        {
            dte = project.DTE;
            projectName = project.Name;
            projectUniqueName = project.UniqueName;
            try
            {
                project.Select();
            }
            catch { }
            try
            {
                dte.UnloadProject(project);
                unloaded = true;
            }
            catch
            {
                project = dte.ReloadSolutionAndReturnProject(project);
                try
                {
                    dte.UnloadProject(project);
                    unloaded = true;
                }
                catch
                {
                    unloaded = false;
                }
            }

        }
        public void Dispose()
        {
            try
            {
                if (unloaded)
                    dte.ReloadJustUnloadedProject();
                else dte.ReloadProject(projectName);
            }
            catch
            {
                dte.ReloadSolutionAndReturnProject(projectName, projectUniqueName);
            }
        }
    }
}
