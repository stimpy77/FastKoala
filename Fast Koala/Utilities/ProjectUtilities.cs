using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.Shell.Interop;
using IServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace Wijits.FastKoala.Utilities
{
    public static class ProjectUtilities
    {
        public static bool IsType(this Project project, Guid projectType)
        {
            var projectKindString = project.Kind.Replace("{", "").Replace("}", "");
            Guid kindGuid;
            if (string.IsNullOrWhiteSpace(projectKindString) || !Guid.TryParse(projectKindString, out kindGuid))
                return false;
            return kindGuid == projectType;
        }

        public static string GetDirectory(this Project project)
        {
            return Directory.GetParent(project.FullName).FullName;
        }

        public static bool IsSourceControlled(this Project project)
        {
            return project.DTE.SourceControl.IsItemUnderSCC(project.FullName);
        }
        public static SourceControlBindings GetSourceControlBindings(this Project project)
        {
            SourceControl2 sourceControl = (SourceControl2)project.DTE.SourceControl;
            return sourceControl.GetBindings(project.FullName);
        }

        public static string GetDirectory(this Solution solution)
        {
            return Directory.GetParent(solution.FullName).FullName;
        }

        public static bool IsSourceControlled(this Solution solution)
        {
            return solution.DTE.SourceControl.IsItemUnderSCC(solution.FullName);
        }

        public static string GetConfigFile(this Project project)
        {
            ProjectItem mitem;
            return (mitem = project.ProjectItems.Cast<ProjectItem>()
                .SingleOrDefault(item =>
                    (item.Name ?? "").ToLower() == "web.config" ||
                    (item.Name ?? "").ToLower() == "app.config")
                ) != null ? mitem.FileNames[0] : null;
        }

        public static object GetPropertyValue(this Project project, string propertyName)
        {
            var property = project.Properties.Cast<Property>().SingleOrDefault(p => p.Name == propertyName);
            return property == null ? null : property.Value;
        }

        private static readonly Dictionary<string, ProjectRootElement> LoadedProjectRoots
            = new Dictionary<string, ProjectRootElement>();
        private static readonly Dictionary<string, FileSystemWatcher> ProjectSaveWatchers
            = new Dictionary<string, FileSystemWatcher>();

        public static ProjectRootElement GetProjectRoot(this Project project)
        {
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
                    var ms = new MemoryStream();
                    var sw = new StreamWriter(ms);
                    projectRoot.Save(sw);
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

        public static async Task SaveProjectRoot(this Project project)
        {
            var projectName = project.Name;
            var dte = project.DTE;
            var projectFullPath = (new FileInfo(project.FullName).FullName);
            dte.UnloadProject(project);
            await SaveProjectRoot(dte, projectFullPath);
            dte.ReloadJustUnloadedProject();
        }

        public static async Task SaveProjectRoot(DTE dte, string projectFullName)
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
            await dte.CheckOutFileForEditIfSourceControlled(projectFullPath);
            root.Save(projectFullPath);
        }

        public static string GetProjectTypeGuids(this Project project)
        {

            string projectTypeGuids = "";
            object service = null;
            IVsSolution solution = null;
            IVsHierarchy hierarchy = null;
            IVsAggregatableProject aggregatableProject = null;
            int result = 0;

            service = GetService(project.DTE, typeof(IVsSolution));
            solution = (IVsSolution)service;

            result = solution.GetProjectOfUniqueName(project.UniqueName, out hierarchy);

            if (result == 0)
            {
                aggregatableProject = (IVsAggregatableProject)hierarchy;
                result = aggregatableProject.GetAggregateProjectTypeGuids(out projectTypeGuids);
            }

            return projectTypeGuids;

        }

        private static object GetService(object serviceProvider, Type type)
        {
            return GetService(serviceProvider, type.GUID);
        }

        private static object GetService(object serviceProviderObject, Guid guid)
        {

            object service = null;
            IServiceProvider serviceProvider = null;
            IntPtr serviceIntPtr = new IntPtr();
            int hr = 0;
            Guid SIDGuid;
            Guid IIDGuid;

            SIDGuid = guid;
            IIDGuid = SIDGuid;
            serviceProvider = (IServiceProvider)serviceProviderObject;
            hr = serviceProvider.QueryService(ref SIDGuid, ref IIDGuid, out serviceIntPtr);

            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
            else if (!serviceIntPtr.Equals(IntPtr.Zero))
            {
                service = Marshal.GetObjectForIUnknown(serviceIntPtr);
                Marshal.Release(serviceIntPtr);
            }

            return service;
        }
    }
}
