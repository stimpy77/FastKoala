using System;
using System.Linq;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Wijits.FastKoala.Utilities;
using Thread = System.Threading.Thread;
using System.Threading.Tasks;
using Wijits.FastKoala.Logging;

namespace Wijits.FastKoala
{
    public static class VsEnvironment
    {
        private static IServiceProvider _serviceProvider;

        public static IWin32Window OwnerWindow
        {
            get { return NativeWindow.FromHandle(new IntPtr(Dte.MainWindow.HWnd)); }
        }

        public static DTE Dte
        {
            get { return GetService<DTE>(); }
        }

        public static void Initialize(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public static T GetService<T>()
        {
            return (T) _serviceProvider.GetService(typeof (T));
        }

        public static object GetService(this DTE dte, Type type)
        {
            return _serviceProvider.GetService(type);
        }

        public static T GetService<T>(this DTE dte)
        {
            var ret = _serviceProvider.GetService(typeof (T));
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
                dte.ExecuteCommand("Project.UnloadProject", "");
                System.Threading.Thread.Sleep(200);
                dte.ExecuteCommand("Project.ReloadProject", "");
                System.Threading.Thread.Sleep(200);
                return GetProjectByName(dte, projectName);
            }
            catch
            {
                try
                {
                    SleepAndCycleMessagePump(dte);
                    dte.ExecuteCommand("Project.UnloadProject", "");
                    SleepAndCycleMessagePump(dte);
                    dte.ExecuteCommand("Project.ReloadProject", "");
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
            dte.ExecuteCommand("Project.UnloadProject", "");
        }

        public static void ReloadJustUnloadedProject(this DTE dte)
        {
            dte.ExecuteCommand("Project.ReloadProject", "");
        }

        private static void SleepAndCycleMessagePump(this DTE dte, int sleepMS = 250)
        {
            Thread.Sleep(sleepMS/2);
            Application.DoEvents();
            Thread.Sleep(sleepMS/2);
        }

        public static Project ReloadProject(this DTE dte, string projectName)
        {
            if (!ValidateIsUniqueProjectName(projectName))
            {
                return null;
            }
            return ReloadProject(dte, GetProjectByName(dte, projectName));
        }

        private static void SelectProject(DTE dte, Project project)
        {
            var solutionExplorer = dte.Windows.Item(Constants.vsWindowKindSolutionExplorer);
            solutionExplorer.Activate();
            var solutionHierarchy = solutionExplorer.Object as UIHierarchy;
            var dte2 = dte as DTE2;
            var se = dte2.ToolWindows.SolutionExplorer;
            var proj = findUIHierarchyItem(solutionHierarchy.UIHierarchyItems, project);
            try { proj.Select(vsUISelectionType.vsUISelectionTypeSelect);}
            catch { }
        }

        public static UIHierarchyItem findUIHierarchyItem(UIHierarchyItems items, Project item)
        {
            foreach (UIHierarchyItem child in items)
            {
                if (child.Object == item) return child;
                var result = findUIHierarchyItem(child.UIHierarchyItems, item);
                if (result != null) return result;
            }
            return null;
        }



        public static Project ReloadSolutionAndReturnProject(this DTE dte, Project project)
        {
            var projectUniqueName = project.UniqueName;
            var projectName = project.Name;
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

        public static async Task CheckOutFileForEditIfSourceControlled(this EnvDTE.DTE dte, string filename)
        {
            await Task.Run(() =>
            {
                if (dte.SourceControl.IsItemUnderSCC(filename))
                {
                    dte.SourceControl.CheckOutItem(filename);
                }
            });
        }

        public static IServiceProvider ServiceProvider
        {
            get { return _serviceProvider; }
        }
    }
}