using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Wijits.FastKoala.Utilities
{
    public static class ProjectUtilities
    {
        public static bool IsType(this Project project, Guid projectType)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            var projectKindString = project.Kind.Replace("{", "").Replace("}", "");
            Guid kindGuid;
            if (string.IsNullOrWhiteSpace(projectKindString) || !Guid.TryParse(projectKindString, out kindGuid))
                return false;
            return kindGuid == projectType;
        }

        public static string GetDirectory(this Project project)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            var projectPath = project.FullName;
            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
            {
                // failover to solution .. not a clean strategy but might work around odd exceptions
                projectPath = project.DTE.Solution.FullName; 
            }
            return Directory.GetParent(projectPath).FullName;
        }

        public static bool IsSourceControlled(this Project project)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return project.DTE.SourceControl.IsItemUnderSCC(project.FullName);
        }
        public static SourceControlBindings GetSourceControlBindings(this Project project)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            SourceControl2 sourceControl = (SourceControl2)project.DTE.SourceControl;
            return sourceControl.GetBindings(project.FullName);
        }

        public static string GetDirectory(this Solution solution)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return string.IsNullOrEmpty(solution.FullName) 
                ? solution.FullName 
                : Directory.GetParent(solution.FullName).FullName;
        }

        public static bool IsSourceControlled(this Solution solution)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return solution.DTE.SourceControl.IsItemUnderSCC(solution.FullName);
        }

        public static string GetConfigFile(this Project project)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            ProjectItem mitem;
            return (mitem = project.ProjectItems.Cast<ProjectItem>()
                .SingleOrDefault(item => {
                    Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                    return (item.Name ?? "").ToLower() == "web.config" ||
(item.Name ?? "").ToLower() == "app.config";
                })
                ) != null ? mitem.FileNames[0] : null;
        }

        public static object GetPropertyValue(this Project project, string propertyName)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            var property = project.Properties.Cast<Property>().SingleOrDefault(p => p.Name == propertyName);
            return property == null ? null : property.Value;
        }

        private static readonly Dictionary<string, ProjectRootElement> LoadedProjectRoots
            = new Dictionary<string, ProjectRootElement>();
        private static readonly Dictionary<string, FileSystemWatcher> ProjectSaveWatchers
            = new Dictionary<string, FileSystemWatcher>();

        public static ProjectRootElement GetProjectRoot(this Project project)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            lock (LoadedProjectRoots)
            {
                var projectFileInfo = new FileInfo(project.FullName);
                var projectFullPath = projectFileInfo.FullName;
                var projectDirectory = project.GetDirectory();
                var projectFilename = projectFileInfo.Name;
                if (LoadedProjectRoots.ContainsKey(projectFullPath)) return LoadedProjectRoots[projectFullPath];

                // rather than use ProjectRootElement.Open we're using stream because there is some internal caching
                ProjectRootElement projectRoot;
                using (var fs = new FileStream(projectFullPath, FileMode.Open))
                {
                    var xr = XmlReader.Create(fs);
                    projectRoot = ProjectRootElement.Create(xr);

                    // clearing dirty flag
                    using (var ms = new MemoryStream())
                    {
                        using (var sw = new StreamWriter(ms)) { 
                            projectRoot.Save(sw);
                        }
                    }
                }

                // remember to throw out cache if the project is ever overwritten
                lock (ProjectSaveWatchers)
                {
                    var fsw = new FileSystemWatcher(projectDirectory, projectFilename);
                    fsw.Changed += (sender, args) =>
                    {
                        lock (LoadedProjectRoots)
                        {
                            if (LoadedProjectRoots.ContainsKey(projectFullPath))
                                LoadedProjectRoots.Remove(projectFullPath);
                            if (ProjectSaveWatchers.ContainsKey(projectFullPath))
                                ProjectSaveWatchers.Remove(projectFullPath);
                        }
                    };
                    fsw.EnableRaisingEvents = true;
                    ProjectSaveWatchers[projectFullPath] = fsw;
                    LoadedProjectRoots[projectFullPath] = projectRoot;
                    return projectRoot;
                }
            }
        }

        public static async Task<Project> SaveProjectRootAsync(this Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var projectFullPath = new FileInfo(project.FullName).FullName; // get Windows-"formal" abs path
            using (new IdioticProjectUnloaderBecauseMicrosoftsVSSDKIsntStable(project))
            {
                await SaveProjectRootAsync(projectFullPath);
            }
            //VsEnvironment.Dte.SleepAndCycleMessagePump();
            return await VsEnvironment.Dte.GetProjectByFullNameAsync(projectFullPath);
        }

        public static async Task SaveProjectRootAsync(string projectFullName)
        {
            var projectFullPath = (new FileInfo(projectFullName).FullName);
            ProjectRootElement root;
            lock (LoadedProjectRoots)
            {
                if (!LoadedProjectRoots.ContainsKey(projectFullPath))
                {
                    return;
                }
                root = LoadedProjectRoots[projectFullPath];
            }
            await VsEnvironment.Dte.CheckOutFileForEditIfSourceControlledAsync(projectFullPath);
            root.Save(projectFullPath);
        }

        // source: http://www.mztools.com/articles/2007/mz2007016.aspx
        // along with [VsEnvironment.]GetService(GUID)
        public static async Task<string> GetProjectTypeGuidsAsync(this Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var projectTypeGuids = "";
            IVsHierarchy hierarchy;
            var service = await project.DTE.GetServiceAsync(typeof(IVsSolution).GUID);
            var solution = (IVsSolution)service;
            var result = solution.GetProjectOfUniqueName(project.UniqueName, out hierarchy);
            if (result != 0) return projectTypeGuids;
            var aggregatableProject = (IVsAggregatableProject)hierarchy;
            aggregatableProject.GetAggregateProjectTypeGuids(out projectTypeGuids);
            return projectTypeGuids;
        }

        // credit: https://mhusseini.wordpress.com/2012/09/28/convert-ivshierarchy-to-projectitem-or-project/
        public static EnvDTE.Project GetDteProject(this IVsHierarchy vsHierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // VSITEMID_ROOT gets the current project. 
            var itemid = VSConstants.VSITEMID_ROOT;
            object objProj;
            vsHierarchy.GetProperty(itemid, (int)__VSHPROPID.VSHPROPID_ExtObject, out objProj);
            return objProj as EnvDTE.Project;
        }

        public static bool IsAvailable(this EnvDTE.Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // test for project availability
            var isAvailable = true;
            try
            {
                project.Properties.Cast<Property>().ToList().ForEach(p => { });
            }
            catch
            {
                isAvailable = false;
            }
            return isAvailable;
        }
    }
}
