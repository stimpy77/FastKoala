using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Wijits.FastKoala.Logging;
using Constants = EnvDTE.Constants;
using Task = System.Threading.Tasks.Task;
using Thread = System.Threading.Thread;

namespace Wijits.FastKoala
{
    public static class VsEnvironment
    {
        public static IWin32Window OwnerWindow
        {
            get
            {
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                return NativeWindow.FromHandle(new IntPtr(Dte.MainWindow.HWnd));
            }
        }

        public static DTE Dte
        {
            get { return GetService<DTE>(); }
        }

        public static IServiceProvider ServiceProvider { get; private set; }

        public static void Initialize(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public static T GetService<T>()
        {
            try
            {
                return (T) ServiceProvider.GetService(typeof (T));
            }
            catch
            {
                return default(T);
            }
        }

        public static object GetService(this DTE dte, Type type)
        {
            return ServiceProvider.GetService(type);
        }

        static CancellationToken DisposalToken
        {
            get { return CancellationToken.None; }
        }

        // source: http://www.mztools.com/articles/2007/mz2007016.aspx
        // supports GetProjectTypeGuids above
        public static async Task<object> GetServiceAsync(this DTE serviceProviderObject, Guid guid)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            object service = null;
            IntPtr serviceIntPtr;
            var sidGuid = guid;
            var iidGuid = sidGuid;
            // ReSharper disable once SuspiciousTypeConversion.Global
            var serviceProvider = (Microsoft.VisualStudio.OLE.Interop.IServiceProvider) serviceProviderObject;
            var hr = serviceProvider.QueryService(ref sidGuid, ref iidGuid, out serviceIntPtr);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            else if (!serviceIntPtr.Equals(IntPtr.Zero))
            {
                service = Marshal.GetObjectForIUnknown(serviceIntPtr);
                Marshal.Release(serviceIntPtr);
            }
            return service;
        }

        public static async Task<T> GetServiceAsync<T>(this DTE dte)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var ret = ServiceProvider.GetService(typeof (T));
            try
            {
                return (T) ret;
            }
            catch
            {
                return default(T);
            }
        }

        public static async Task<ILogger> GetLoggerAsync(this DTE dte)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            return new DteLogger(dte);
        }

        public static async Task<Project> GetProjectByNameAsync(this DTE dte, string projectName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var ret = dte.Solution.Projects.Cast<Project>().SingleOrDefault(project => project.Name == projectName) ??
                      await FindProjectByUniqueNameAsync(dte, projectName);
            return ret;
        }

        public static async Task<Project> GetProjectByUniqueNameAsync(this DTE dte, string projectUniqueName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var ret = dte.Solution.Projects.Cast<Project>()
                .SingleOrDefault(project => project.UniqueName == projectUniqueName) ??
                      await FindProjectByUniqueNameAsync(dte, projectUniqueName);
            return ret;
        }

        // source: http://www.wwwlicious.com/2011/03/29/envdte-getting-all-projects-html/
        public static async Task<IList<Project>> ProjectsAsync(this DTE dte)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            Projects projects = dte.Solution.Projects;
            List<Project> list = new List<Project>();
            var item = projects.GetEnumerator();
            while (item.MoveNext())
            {
                var project = item.Current as Project;
                if (project == null)
                {
                    continue;
                }

                if (project.Kind == ProjectKindsInternal.vsProjectKindSolutionFolder)
                {
                    list.AddRange(await GetSolutionFolderProjectsAsync(project));
                }
                else
                {
                    list.Add(project);
                }
            }

            return list;
        }

        // source: http://www.wwwlicious.com/2011/03/29/envdte-getting-all-projects-html/
        private static async Task<IEnumerable<Project>> GetSolutionFolderProjectsAsync(Project solutionFolder)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var list = new List<Project>();
            for (var i = 1; i <= solutionFolder.ProjectItems.Count; i++)
            {
                var subProject = solutionFolder.ProjectItems.Item(i).SubProject;
                if (subProject == null)
                {
                    continue;
                }

                // If this is another solution folder, do a recursive call, otherwise add
                if (subProject.Kind == ProjectKindsInternal.vsProjectKindSolutionFolder)
                {
                    list.AddRange(await GetSolutionFolderProjectsAsync(subProject));
                }
                else
                {
                    list.Add(subProject);
                }
            }
            return list;
        }

        private static async Task<Project> FindProjectByUniqueNameAsync(this DTE dte, string projectUniqueName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            return (await ProjectsAsync(dte)).FirstOrDefault(project => project.UniqueName == projectUniqueName);
        }

        public static async Task<Project> FindProjectByNameAsync(this DTE dte, string projectName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            return (await ProjectsAsync(dte)).FirstOrDefault(project => project.Name == projectName);
        }

        public static async Task<Project> GetProjectByFullNameAsync(this DTE dte, string projectFullName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            Project result = null;
            Project _default = null;
            dte.Solution.Projects.Cast<Project>().ToList().ForEach(project =>
            {
                var projFullName = "";
                try
                {
                    projFullName = project.FullName;
                }
                catch
                {
                    _default = project;
                }
                if ((projFullName ?? "").ToLower() == projectFullName.ToLower()) result = project;
            });
            return result ?? _default;
        }

        public static async Task<Project> ReloadProjectAsync(this DTE dte, Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var projectUniqueName = project.UniqueName;
            var projectName = project.Name;
            await SelectProjectAsync(dte, project);
            try
            {
                dte.ExecuteCommand("Project.UnloadProject");
                Thread.Sleep(200);
                dte.ExecuteCommand("Project.ReloadProject");
                Thread.Sleep(200);
                return await GetProjectByNameAsync(dte, projectName);
            }
            catch
            {
                try
                {
                    await SleepAndCycleMessagePumpAsync(dte);
                    dte.ExecuteCommand("Project.UnloadProject");
                    await SleepAndCycleMessagePumpAsync(dte);
                    dte.ExecuteCommand("Project.ReloadProject");
                    await SleepAndCycleMessagePumpAsync(dte);
                    return await GetProjectByUniqueNameAsync(dte, projectUniqueName)
                           ?? await GetProjectByNameAsync(dte, projectName);
                }
                catch
                {
                    return await ReloadSolutionAndReturnProjectAsync(dte, project);
                }
            }
        }

        public static async Task UnloadProjectAsync(this DTE dte, Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            await SelectProjectAsync(dte, project);
            dte.ExecuteCommand("Project.UnloadProject");
        }

        public static async Task ReloadProjectAsync(this DTE dte)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            dte.ExecuteCommand("Project.ReloadProject");
        }

        // ReSharper disable once UnusedParameter.Local
        public static async Task SleepAndCycleMessagePumpAsync(this DTE dte, int sleepMs = 250)
        {
            Thread.Sleep(sleepMs/2);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            Application.DoEvents();
            Thread.Sleep(sleepMs/2);
        }

        public static async Task<Project> ReloadProjectAsync(this DTE dte, string projectName)
        {
            if (!await ValidateIsUniqueProjectNameAsync(projectName))
            {
                return null;
            }
            return await ReloadProjectAsync(dte, await GetProjectByNameAsync(dte, projectName));
        }

        /// <summary>
        /// Selects the given project in the Solution hierarchy and returns an Action to do it again.
        /// </summary>
        /// <param name="project"></param>
        /// <returns></returns>
        public static async Task<Action> SelectAsync(this Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            return await SelectProjectAsync(project.DTE, project);
        }

        public static async Task<Action> SelectProjectAsync(DTE dte, Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

            var solutionExplorer = dte.Windows.Item(Constants.vsWindowKindSolutionExplorer);
            solutionExplorer.Activate();
            var solutionHierarchy = solutionExplorer.Object as UIHierarchy;
            Debug.Assert(solutionHierarchy != null, "solutionHierarchy != null");
            var proj = await FindUiHierarchyItemAsync(solutionHierarchy.UIHierarchyItems, project);
            var action = new Action(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    proj.Select(vsUISelectionType.vsUISelectionTypeSelect);
                }
                catch
                {
                    // ignored
                }
            });
            action();
            return action;
        }

        public static async Task<UIHierarchyItem> FindUiHierarchyItemAsync(UIHierarchyItems items, Project item)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

            foreach (UIHierarchyItem child in items)
            {
                if (child.Object == item) return child;
                var result = await FindUiHierarchyItemAsync(child.UIHierarchyItems, item);
                if (result != null) return result;
            }
            return null;
        }

        public static async Task<Project> ReloadSolutionAndReturnProjectAsync(this DTE dte, Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var projectUniqueName = project.UniqueName;
            var projectName = project.Name;
            return await ReloadSolutionAndReturnProjectAsync(dte, projectName, projectUniqueName);
        }

        public static async Task<Project> ReloadSolutionAndReturnProjectAsync(this DTE dte, string projectName, string projectUniqueName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

            await SaveAllAsync(dte);
            var slnPath = dte.Solution.FullName;
            dte.Solution.Close();
            dte.Solution.Open(slnPath);
            try
            {
                await SleepAndCycleMessagePumpAsync(dte);
                return await GetProjectByUniqueNameAsync(dte, projectUniqueName)
                       ?? await GetProjectByNameAsync(dte, projectName);
            }
            catch
            {
                await SleepAndCycleMessagePumpAsync(dte, 500);
                return await GetProjectByUniqueNameAsync(dte, projectUniqueName)
                       ?? await GetProjectByNameAsync(dte, projectName);
            }
        }

        public static async Task SaveAllAsync(this DTE dte)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            dte.ExecuteCommand("File.SaveAll");
        }

        private static async Task<bool> ValidateIsUniqueProjectNameAsync(string projectName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            return Dte.Solution.Cast<Project>().Count(p => p.Name == projectName) == 1;
        }

        public static async Task CheckOutFileForEditIfSourceControlledAsync(this DTE dte, string filename)
        {
            await Task.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                if (dte.SourceControl.IsItemUnderSCC(filename))
                {
                    dte.SourceControl.CheckOutItem(filename);
                }
            });
        }
    }
}