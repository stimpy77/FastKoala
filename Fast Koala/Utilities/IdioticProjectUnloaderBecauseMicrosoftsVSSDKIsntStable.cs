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
        private Action _projectReselecter;

        public IdioticProjectUnloaderBecauseMicrosoftsVSSDKIsntStable(Project project)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            dte = project.DTE;
            projectName = project.Name;
            projectUniqueName = project.UniqueName;
            var t = Initialize(project);
            t.ConfigureAwait(true);
            t.Wait();
        }

        private async Task Initialize(Project project)
        {
            try
            {
                _projectReselecter = await project.SelectAsync();
            }
            catch { }
            try
            {
                await dte.UnloadProjectAsync(project);
                unloaded = true;
            }
            catch
            {
                project = await dte.ReloadSolutionAndReturnProjectAsync(project);
                try
                {
                    await dte.UnloadProjectAsync(project);
                    unloaded = true;
                }
                catch
                {
                    unloaded = false;
                }
            }
        }
        public async void Dispose()
        {
            Task t;
            try
            {
                if (unloaded)
                {
                    _projectReselecter();
                    await dte.ReloadProjectAsync();
                }
                else await dte.ReloadProjectAsync(projectName);
            }
            catch(Exception e)
            {
                try
                {
                    await dte.ReloadProjectAsync(projectName);
                }
                catch
                {
                    try
                    {
                        System.Windows.Forms.MessageBox.Show(
                            "Unable to reload the project automatically. Please reload it manually (right-click and reload).",
                            "Fast Koala", System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Exclamation);
                        await dte.ReloadSolutionAndReturnProjectAsync(projectName, projectUniqueName);
                    }
                    catch { }
                }
            }
        }
    }
}
