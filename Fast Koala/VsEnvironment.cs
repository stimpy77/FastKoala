using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using Wijits.FastKoala.Logging;
using Thread = System.Threading.Thread;

namespace Wijits.FastKoala
{
    public static class VsEnvironment
    {
        public static IWin32Window OwnerWindow
        {
            get { return NativeWindow.FromHandle(new IntPtr(Dte.MainWindow.HWnd)); }
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
            return (T) ServiceProvider.GetService(typeof (T));
        }

        public static object GetService(this DTE dte, Type type)
        {
            return ServiceProvider.GetService(type);
        }

        // source: http://www.mztools.com/articles/2007/mz2007016.aspx
        // supports GetProjectTypeGuids above
        public static object GetService(this DTE serviceProviderObject, Guid guid)
        {
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

        public static T GetService<T>(this DTE dte)
        {
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

        public static ILogger GetLogger(this DTE dte)
        {
            return new DteLogger(dte);
        }

        public static Project GetProjectByName(this DTE dte, string projectName)
        {
            return dte.Solution.Cast<Project>().SingleOrDefault(project => project.Name == projectName);
        }

        public static Project GetProjectByUniqueName(this DTE dte, string projectUniqueName)
        {
            return dte.Solution.Cast<Project>().SingleOrDefault(project => project.UniqueName == projectUniqueName);
        }

        public static Project ReloadProject(this DTE dte, Project project)
        {
            var projectUniqueName = project.UniqueName;
            var projectName = project.Name;
            SelectProject(dte, project);
            try
            {
                dte.ExecuteCommand("Project.UnloadProject");
                Thread.Sleep(200);
                dte.ExecuteCommand("Project.ReloadProject");
                Thread.Sleep(200);
                return GetProjectByName(dte, projectName);
            }
            catch
            {
                try
                {
                    SleepAndCycleMessagePump(dte);
                    dte.ExecuteCommand("Project.UnloadProject");
                    SleepAndCycleMessagePump(dte);
                    dte.ExecuteCommand("Project.ReloadProject");
                    SleepAndCycleMessagePump(dte);
                    return GetProjectByUniqueName(dte, projectUniqueName)
                           ?? GetProjectByName(dte, projectName);
                }
                catch
                {
                    return ReloadSolutionAndReturnProject(dte, project);
                }
            }
        }

        public static void UnloadProject(this DTE dte, Project project)
        {
            SelectProject(dte, project);
            dte.ExecuteCommand("Project.UnloadProject");
        }

        public static void ReloadJustUnloadedProject(this DTE dte)
        {
            dte.ExecuteCommand("Project.ReloadProject");
        }

        // ReSharper disable once UnusedParameter.Local
        private static void SleepAndCycleMessagePump(this DTE dte, int sleepMs = 250)
        {
            Thread.Sleep(sleepMs/2);
            Application.DoEvents();
            Thread.Sleep(sleepMs/2);
        }

        public static Project ReloadProject(this DTE dte, string projectName)
        {
            if (!ValidateIsUniqueProjectName(projectName))
            {
                return null;
            }
            return ReloadProject(dte, GetProjectByName(dte, projectName));
        }

        public static void Select(this Project project)
        {
            SelectProject(project.DTE, project);
        }

        public static void SelectProject(DTE dte, Project project)
        {
            var solutionExplorer = dte.Windows.Item(Constants.vsWindowKindSolutionExplorer);
            solutionExplorer.Activate();
            var solutionHierarchy = solutionExplorer.Object as UIHierarchy;
            Debug.Assert(solutionHierarchy != null, "solutionHierarchy != null");
            var proj = FindUiHierarchyItem(solutionHierarchy.UIHierarchyItems, project);
            try
            {
                proj.Select(vsUISelectionType.vsUISelectionTypeSelect);
            }
            catch
            {
                // ignored
            }
        }

        public static UIHierarchyItem FindUiHierarchyItem(UIHierarchyItems items, Project item)
        {
            foreach (UIHierarchyItem child in items)
            {
                if (child.Object == item) return child;
                var result = FindUiHierarchyItem(child.UIHierarchyItems, item);
                if (result != null) return result;
            }
            return null;
        }

        public static Project ReloadSolutionAndReturnProject(this DTE dte, Project project)
        {
            var projectUniqueName = project.UniqueName;
            var projectName = project.Name;
            return ReloadSolutionAndReturnProject(dte, projectName, projectUniqueName);
        }

        public static Project ReloadSolutionAndReturnProject(this DTE dte, string projectName, string projectUniqueName)
        {
            SaveAll(dte);
            var slnPath = dte.Solution.FullName;
            dte.Solution.Close();
            dte.Solution.Open(slnPath);
            try
            {
                SleepAndCycleMessagePump(dte);
                return GetProjectByUniqueName(dte, projectUniqueName)
                       ?? GetProjectByName(dte, projectName);
            }
            catch
            {
                SleepAndCycleMessagePump(dte, 500);
                return GetProjectByUniqueName(dte, projectUniqueName)
                       ?? GetProjectByName(dte, projectName);
            }
        }

        public static void SaveAll(this DTE dte)
        {
            dte.ExecuteCommand("File.SaveAll");
        }

        private static bool ValidateIsUniqueProjectName(string projectName)
        {
            return Dte.Solution.Cast<Project>().Count(p => p.Name == projectName) == 1;
        }

        public static async Task CheckOutFileForEditIfSourceControlled(this DTE dte, string filename)
        {
            await Task.Run(() =>
            {
                if (dte.SourceControl.IsItemUnderSCC(filename))
                {
                    dte.SourceControl.CheckOutItem(filename);
                }
            });
        }
    }
}