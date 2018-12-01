using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Wijits.FastKoala.BuildScriptInjections;
using Wijits.FastKoala.Events;
using Wijits.FastKoala.Logging;
using Wijits.FastKoala.SourceControl;
using Wijits.FastKoala.Transformations;
using Wijits.FastKoala.Utilities;

namespace Wijits.FastKoala
{
    /// <summary>
    ///     This is the class that implements the package exposed by this assembly.
    ///     The minimum requirement for a class to be considered a valid package for Visual Studio
    ///     is to implement the IVsPackage interface and register itself with the shell.
    ///     This package uses the helper classes defined inside the Managed Package Framework (MPF)
    ///     to do it: it derives from the Package class that provides the implementation of the
    ///     IVsPackage interface and uses the registration attributes defined in the framework to
    ///     register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    [Guid(GuidList.guidFastKoalaPkgString)]
    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Local")]
    public sealed class FastKoalaPackage : Package
    {
        private DocumentEvents _documentEvents;
        private BuildEvents _buildEvents;
        private SolutionEventsWrapper _solutionEventsHandler;
        private uint _solutionEventsCookie;
        private bool shownErrorMsg = false;

        public const string FastKoalaMaintainerEmail = "jon@wijits.com";

        /////////////////////////////////////////////////////////////////////////////
        // Overridden Package Implementation

        #region Package Members

        /// <summary>
        ///     Initialization of the package; this method is called right after the package is sited, so this is the place
        ///     where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", ToString()));
            base.Initialize();
            VsEnvironment.Initialize(this);

            SubscribeDteEvents();

            // Add our command handlers for menu (commands must exist in the .vsct file)
            var mcs = GetService(typeof (IMenuCommandService)) as OleMenuCommandService;
            if (null == mcs) return;

            var enableBuildTimeTransformationsCmd = new CommandID(GuidList.guidFastKoalaProjItemMenuCmdSet,
                (int) PkgCmdIDList.cmdidEnableBuildTimeTransformationsProjItem);
            var enableBuildTimeTransformationsMenuItem =
                new OleMenuCommand(EnableBuildTimeTransformationsMenuItem_Invoke, enableBuildTimeTransformationsCmd);
            enableBuildTimeTransformationsMenuItem.BeforeQueryStatus +=
                EnableBuildTimeTransformationsMenuItem_BeforeQueryStatus;
            mcs.AddCommand(enableBuildTimeTransformationsMenuItem);

            var enableBuildTimeTransformationsProjCmd = new CommandID(GuidList.guidFastKoalaProjMenuCmdSet,
                (int) PkgCmdIDList.cmdidEnableBuildTimeTransformationsProj);
            var enableBuildTimeTransformationsProjectMenuItem =
                new OleMenuCommand(EnableBuildTimeTransformationsMenuItem_Invoke,
                    enableBuildTimeTransformationsProjCmd);
            enableBuildTimeTransformationsProjectMenuItem.BeforeQueryStatus +=
                EnableBuildTimeTransformationsMenuItemProject_BeforeQueryStatus;
            mcs.AddCommand(enableBuildTimeTransformationsProjectMenuItem);

            var addMissingTransformationsCmd = new CommandID(GuidList.guidFastKoalaProjItemMenuCmdSet,
                (int)PkgCmdIDList.cmdidAddMissingTransformsProjItem);
            var addMissingTransformationsMenuItem =
                new OleMenuCommand(AddMissingTransformationsMenuItem_Invoke, addMissingTransformationsCmd);
            addMissingTransformationsMenuItem.BeforeQueryStatus +=
                AddMissingTransformationsMenuItem_BeforeQueryStatus;
            mcs.AddCommand(addMissingTransformationsMenuItem);

            var addPowerShellScriptCmd = new CommandID(GuidList.guidFastKoalaProjAddCmdSet,
                (int)PkgCmdIDList.cmdIdFastKoalaAddPowerShellScript);
            var addPowerShellScriptMenuItem =
                new OleMenuCommand(AddPowerShellScriptMenuItem_Invoke, addPowerShellScriptCmd);
            addPowerShellScriptMenuItem.BeforeQueryStatus +=
                AddPowerShellScriptMenuItem_BeforeQueryStatus;
            mcs.AddCommand(addPowerShellScriptMenuItem);

            var addMSBuildScriptCmd = new CommandID(GuidList.guidFastKoalaProjAddCmdSet,
                (int)PkgCmdIDList.cmdIdFastKoalaAddMSBuildScript);
            var addMSBuildScriptMenuItem =
                new OleMenuCommand(AddMSBuildScriptMenuItem_Invoke, addMSBuildScriptCmd);
            addMSBuildScriptMenuItem.BeforeQueryStatus +=
                AddMSBuildScriptMenuItem_BeforeQueryStatus;
            mcs.AddCommand(addMSBuildScriptMenuItem);

            var addNodeJSScriptCmd = new CommandID(GuidList.guidFastKoalaProjAddCmdSet,
                (int)PkgCmdIDList.cmdIdFastKoalaAddNodeJSScript);
            var addNodeJSScriptMenuItem =
                new OleMenuCommand(AddNodeJSScriptMenuItem_Invoke, addNodeJSScriptCmd);
            addNodeJSScriptMenuItem.BeforeQueryStatus +=
                AddNodeJSScriptMenuItem_BeforeQueryStatus;
            mcs.AddCommand(addNodeJSScriptMenuItem);
        }

        private void SubscribeDteEvents()
        {
            (_documentEvents ?? (_documentEvents = Dte.Events.DocumentEvents))
                .DocumentOpened += OnDocumentOpened;
            
            (_buildEvents ?? (_buildEvents = Dte.Events.BuildEvents))
                .OnBuildBegin += BuildEventsOnOnBuildBegin;
            _buildEvents.OnBuildDone += BuildEventsOnOnBuildDone;
            (_solutionEvents ?? (_solutionEvents = Dte.Events.SolutionEvents))
                .ProjectRenamed += OnProjectRenamed;

            // credit: http://www.mztools.com/articles/2014/MZ2014024.aspx
            var _solution = GetService(typeof(SVsSolution)) as IVsSolution;
            if (_solution != null)
            {
                _solutionEventsHandler = new SolutionEventsWrapper();
                _solution.AdviseSolutionEvents(_solutionEventsHandler, out _solutionEventsCookie);

                _solutionEventsHandler.AfterLoadProject += OnAfterLoadProject;
                _solutionEventsHandler.AfterOpenProject += OnAfterOpenProject;
                _solutionEventsHandler.BeforeCloseProject += OnBeforeCloseProject;
            }
        }

        private void OnAfterLoadProject(object sender, AfterLoadProjectEventArgs afterLoadProjectEventArgs)
        {
            var project = afterLoadProjectEventArgs.Project;
            MonitorConfigFileChanges(project);
        }

        private void OnProjectRenamed(Project project, string oldName)
        {
            if (_configWatchers.ContainsKey(oldName))
            {
                var cfgwatcher = _configWatchers[oldName];
                cfgwatcher.Dispose();
                _configWatchers.Remove(oldName);
                _configWatchers[project.Name] = new ConfigWatcher(project);
            }
        }

        Dictionary<string, ConfigWatcher> _configWatchers
            = new Dictionary<string, ConfigWatcher>();

        private SolutionEvents _solutionEvents;
        private DateTime _lastModifiedNotification;

        private void OnAfterOpenProject(object sender, AfterOpenProjectEventArgs afterOpenProjectEventArgs)
        {
            var project = afterOpenProjectEventArgs.Project;
            MonitorConfigFileChanges(project);
        }

        private void MonitorConfigFileChanges(Project project)
        {
            var projectName = project.Name;
            if (_configWatchers.ContainsKey(projectName)) return;
            var configWatcher = new ConfigWatcher(project);
            _configWatchers[projectName] = configWatcher;
            configWatcher.AppConfigFileChanged += OnAppConfigFileChanged;
        }

        private object appConfigFileChangedMessageLock = new object();
        private Cursor previousCursor;
        private bool _building;
        private void OnAppConfigFileChanged(object sender, AppConfigFileChangedEventArgs appConfigFileChangedEventArgs)
        {
//#if !DEBUG
            try
            {
//#endif
                if (_building || ((ConfigWatcher)sender).IsBuilding) return;
                var project = appConfigFileChangedEventArgs.Project;
                string projectFullName;
                try { projectFullName = project.FullName; } catch { return; }
                if (!project.IsAvailable() || !File.Exists(projectFullName)) return;
                var fileInfo = new FileInfo(appConfigFileChangedEventArgs.AppConfigFile);
                var projectProperties = new ProjectProperties(project);
                if (projectProperties.InlineAppCfgTransforms != true)
                    return;
                var baseFileFullPath = appConfigFileChangedEventArgs.AppConfigFile;
                // $(MSBuildProjectDirectory)\$(ConfigDir)\$(AppCfgType).Base.config
                var tmpBaseFileFullPath = Path.Combine(project.GetDirectory(),
                    projectProperties.ConfigDir, projectProperties.AppCfgType + ".Base.config");
                if (File.Exists(tmpBaseFileFullPath)) baseFileFullPath = tmpBaseFileFullPath;
                lock (appConfigFileChangedMessageLock)
                {
                    var tmpGenFilePath = fileInfo.FullName + ".tmp";
                    var modifiedGeneratedConfigFileContents = File.ReadAllText(fileInfo.FullName);
                    if (!string.IsNullOrEmpty(baseFileFullPath) &&
                        baseFileFullPath != appConfigFileChangedEventArgs.AppConfigFile
                        && DateTime.Now - _lastModifiedNotification > TimeSpan.FromSeconds(15)
                        && Regex.Replace(modifiedGeneratedConfigFileContents, @"\s", "") != Regex.Replace(File.ReadAllText(baseFileFullPath), @"\s", ""))
                    {
                        // 1. re-run TransformXml
                        var xfrmFile = Path.Combine(project.GetDirectory(), projectProperties.ConfigDir,
                            projectProperties.AppCfgType + "."
                            + project.ConfigurationManager.ActiveConfiguration.ConfigurationName + ".config");
                        if (!File.Exists(xfrmFile))
                        {
                            File.Copy(baseFileFullPath, tmpGenFilePath, true);
                        }
                        else
                        {
                            var xfrm = new MSBuildXmlTransformer
                            {
                                Source = baseFileFullPath,
                                Transform = xfrmFile,
                                Destination = tmpGenFilePath
                            };
                            xfrm.Execute();
                        }
                        // 2. load transformxml output as string
                        var generatedConfigFileContents = File.ReadAllText(tmpGenFilePath);
                        // 3. delete tmp
                        File.Delete(tmpGenFilePath);
                        // 4. compare
                        if (generatedConfigFileContents == modifiedGeneratedConfigFileContents) return;
                        var baseFileRelativePath = FileUtilities.GetRelativePath(
                            Directory.GetParent(project.GetDirectory()).FullName, baseFileFullPath, trimDotSlash: true);
                        MessageBox.Show(GetNativeWindow(),
                            "The " + fileInfo.Name + " file has been modified, but "
                            + "this is a generated file. You will need to immediately identify the changes that were made and "
                            + "propagate them over to " + baseFileRelativePath, fileInfo.Name, MessageBoxButtons.OK,
                            MessageBoxIcon.Exclamation);
                        _lastModifiedNotification = DateTime.Now;
                        Dte.ExecuteCommand("Tools.DiffFiles", "\"" + fileInfo.FullName + "\" \"" + baseFileFullPath + "\"");
                        _building = false;
                    }
                }
//#if !DEBUG
            }
            catch (Exception e)
            {
                Dte.GetLogger().LogError(e.GetType().Name + " - " + e.ToString());
            }
//#endif
        }

        private void OnBeforeCloseProject(object sender, BeforeCloseProjectEventArgs beforeCloseProjectEventArgs)
        {
            var project = beforeCloseProjectEventArgs.Project;
            var projectName = project.Name;
            if (_configWatchers.ContainsKey(projectName))
            {
                _configWatchers[projectName].Dispose();
                _configWatchers.Remove(projectName);
            }
        }

        private void BuildEventsOnOnBuildBegin(vsBuildScope scope, vsBuildAction action)
        {
            BeginPackageBusy(false);
        }

        private void BuildEventsOnOnBuildDone(vsBuildScope scope, vsBuildAction action)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                System.Threading.Thread.Sleep(1000);
                EndPackageBusy(false);
            });
        }

        private async void OnDocumentOpened(Document document)
        {
            // make Web.config / App.config read-only in editor if it was generated
            try
            {
                if (document.ProjectItem == null) return;
                var fileFullName = document.FullName;
                var fileInfo = new FileInfo(fileFullName);
                if (fileInfo.Extension.Replace(".", "").ToLower() != "config") return;
                var project = GetSelectedProject();
                if (project != null)
                {
                    var transforms = await GetTransformationsEnabler(project);
                    if (transforms == null) return;
                    var properties = transforms.ProjectProperties;
                    if (transforms.HasBuildTimeTransformationsEnabled && 
                        properties.InlineAppCfgTransforms == true &&
                        fileInfo.Name.ToLower() == properties.AppCfgType.ToLower() + ".config")
                    {
                        document.ReadOnly = true;
                    }
                }
            }
            catch (Exception exception)
            {
#if DEBUG
                // Hi. Something went wrong while making 
                // Web.config / App.config read-only in 
                // the editor if it was generated. Maybe
                // add some escape paths?
                if (System.Diagnostics.Debugger.IsAttached)
                    throw; // up
#endif
            }
        }

        private async Task<BuildTimeTransformationsEnabler> GetTransformationsEnabler(Project project, bool quick = false)
        {
            string projectName = null;
            string projectFullName = null;
            try
            {
                projectName = project.Name;
                projectFullName = project.FullName;
            }
            catch { }
            if (string.IsNullOrEmpty(projectName) || string.IsNullOrEmpty(projectFullName))
                return null;
            var logger = Dte.GetLogger();
            var io = quick ? new NonSccBasicFileSystem() : await VsFileSystemManipulatorFactory.GetFileSystemManipulatorForEnvironment(project);
            var nativeWindow = GetNativeWindow();
            Debug.Assert(project != null, "project != null");
            return new BuildTimeTransformationsEnabler(project, logger, io, nativeWindow);
        }

        private IWin32Window GetNativeWindow()
        {
            return NativeWindow.FromHandle(System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle);
        }

        private DTE Dte
        {
            get { return (DTE) GetService(typeof (DTE)); }
        }

        private Project GetSelectedProject()
        {
            try
            {
                var monitorSelection = GetGlobalService(typeof (SVsShellMonitorSelection)) as IVsMonitorSelection;
                IVsMultiItemSelect multiItemSelect = null;
                var hierarchyPtr = IntPtr.Zero;
                var selectionContainerPtr = IntPtr.Zero;
                uint itemid;
                var hr = monitorSelection.GetCurrentSelection(out hierarchyPtr, out itemid, out multiItemSelect,
                    out selectionContainerPtr);
                if (ErrorHandler.Failed(hr) || hierarchyPtr == IntPtr.Zero || itemid == VSConstants.VSITEMID_NIL)
                {
                    // there is no selection
                    return null;
                }
                var hierarchy = Marshal.GetObjectForIUnknown(hierarchyPtr) as IVsHierarchy;
                if (hierarchy == null) return null;
                object objProj;
                hierarchy.GetProperty(itemid, (int) __VSHPROPID.VSHPROPID_ExtObject, out objProj);
                var projectItem = objProj as ProjectItem;
                var project = objProj as Project;

                return project ?? (projectItem != null ? projectItem.ContainingProject : null);
            }
            catch (Exception e)
            {
                Dte.GetLogger().LogWarn("Couldn't find selected project. " + e.Message);
                return null;
            }
        }

        #endregion

        #region Add PowerShell Script
        private void AddPowerShellScriptMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            // anything goes?
        }

        private async void AddPowerShellScriptMenuItem_Invoke(object sender, EventArgs e)
        {
            try
            {
                // get the menu that fired the event
                var menuCommand = sender as OleMenuCommand;
                if (menuCommand == null) return;

                IVsHierarchy hierarchy;
                uint itemid;

                var project = GetSelectedProject();
                var projectFolder = project.GetDirectory();
                if (project == null) return;

                var isProject = (!IsSingleProjectItemSelection(out hierarchy, out itemid));
                string containerDirectory;
                if (!isProject)
                {
                    // Get the file path
                    string itemFullPath = null;
                    ((IVsProject) hierarchy).GetMkDocument(itemid, out itemFullPath);
                    containerDirectory = new DirectoryInfo(itemFullPath).FullName;
                }
                else containerDirectory = projectFolder;

                BeginPackageBusy();

                var psBuildScriptSupportInjector = await GetNewPowerShellBuildScriptSupportInjector();
                if (await psBuildScriptSupportInjector.AddPowerShellScript(containerDirectory))
                    LogSuccess();
                else LogCancelOrAbort();
                EndPackageBusy();
            }
            catch (Exception exception)
            {
#if DEBUG
                // what the heck just happened?
                if (System.Diagnostics.Debugger.IsAttached) throw;
#endif
                LogAndPromptUnhandledError(exception);
            }
            
        }

        private void BeginPackageBusy(bool withCursor = true)
        {
            if (withCursor)
            {
                if (previousCursor == null) previousCursor = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
            }
            foreach (var watcher in _configWatchers)
            {
                watcher.Value.IsBuilding = true;
            }
        }

        private void EndPackageBusy(bool withCursor = true)
        {
            _building = false;
            foreach (var watcher in _configWatchers)
            {
                watcher.Value.IsBuilding = _building;
            }
            if (withCursor)
            {
                Cursor.Current = previousCursor;
                previousCursor = null;
            }
        }
        #endregion


#region Add .targets file
        private void AddMSBuildScriptMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            // anything goes?
        }

        private async void AddMSBuildScriptMenuItem_Invoke(object sender, EventArgs e)
        {
            try
            {
                // get the menu that fired the event
                var menuCommand = sender as OleMenuCommand;
                if (menuCommand == null) return;

                IVsHierarchy hierarchy;
                uint itemid;

                var project = GetSelectedProject();
                var projectFolder = project.GetDirectory();
                if (project == null) return;

                var isProject = (!IsSingleProjectItemSelection(out hierarchy, out itemid));
                string containerDirectory;
                if (!isProject)
                {
                    // Get the file path
                    string itemFullPath = null;
                    ((IVsProject)hierarchy).GetMkDocument(itemid, out itemFullPath);
                    containerDirectory = new DirectoryInfo(itemFullPath).FullName;
                }
                else containerDirectory = projectFolder;

                BeginPackageBusy();

                var targBuildScriptSupportInjector = await GetNewTargetsBuildScriptSupportInjector();
                if (await targBuildScriptSupportInjector.AddProjectInclude(containerDirectory))
                    LogSuccess();
                else LogCancelOrAbort();
                EndPackageBusy();
            }
            catch (Exception exception)
            {
#if DEBUG
                // what the heck just happened?
                if (System.Diagnostics.Debugger.IsAttached) throw;
#endif
                LogAndPromptUnhandledError(exception);
            }
        }

        private async Task<TargetsScriptInjector> GetNewTargetsBuildScriptSupportInjector()
        {
            var logger = Dte.GetLogger();
            var project = GetSelectedProject();
            var result = new TargetsScriptInjector(project,
                await VsFileSystemManipulatorFactory.GetFileSystemManipulatorForEnvironment(project),
                logger, GetNativeWindow());
            return result;
        }
#endregion

#region Add NodeJS file
        private void AddNodeJSScriptMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            var menuCommand = sender as OleMenuCommand;
            if (menuCommand == null) return;

            // anything goes?
            
            //(temporarily hide)
            //menuCommand.Visible = false;
            //menuCommand.Enabled = false;
        }

        private async void AddNodeJSScriptMenuItem_Invoke(object sender, EventArgs e)
        {
            try
            {
                // get the menu that fired the event
                var menuCommand = sender as OleMenuCommand;
                if (menuCommand == null) return;

                IVsHierarchy hierarchy;
                uint itemid;

                var project = GetSelectedProject();
                var projectFolder = project.GetDirectory();
                if (project == null) return;

                var isProject = (!IsSingleProjectItemSelection(out hierarchy, out itemid));
                string containerDirectory;
                if (!isProject)
                {
                    // Get the file path
                    string itemFullPath = null;
                    ((IVsProject)hierarchy).GetMkDocument(itemid, out itemFullPath);
                    containerDirectory = new DirectoryInfo(itemFullPath).FullName;
                }
                else containerDirectory = projectFolder;

                BeginPackageBusy();

                var nodeJSBuildScriptSupportInjector = await GetNewNodeJSBuildScriptSupportInjector();
                if (await nodeJSBuildScriptSupportInjector.AddNodeJSScript(containerDirectory))
                    LogSuccess();
                else LogCancelOrAbort();

                EndPackageBusy();
            }
            catch (Exception exception)
            {
#if DEBUG
                // what the heck just happened?
                if (System.Diagnostics.Debugger.IsAttached) throw;
#endif
                LogAndPromptUnhandledError(exception);
            }
        }

#endregion

#region EnableBuildTimeTransformations

        /// <summary>
        /// User should've right-clicked on a .config file; determine whether to show 
        /// "Enable build-time transformations" menu item
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void EnableBuildTimeTransformationsMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            try
            {
                // get the menu that fired the event
                var menuCommand = sender as OleMenuCommand;
                if (menuCommand == null) return;

                // start by assuming that the menu will not be shown
                menuCommand.Visible = false;
                menuCommand.Enabled = false;

                IVsHierarchy hierarchy = null;
                uint itemid;

                if (!IsSingleProjectItemSelection(out hierarchy, out itemid)) return;
                // Get the file path
                string itemFullPath = null;
                ((IVsProject) hierarchy).GetMkDocument(itemid, out itemFullPath);
                FileInfo transformFileInfo = null;
                try
                {
                    transformFileInfo = new FileInfo(itemFullPath);
                }
                catch
                {
                    return;
                }

                // then check if the file is named 'web.config'
                var isConfig = Regex.IsMatch(transformFileInfo.Name, @"[Web|App](\.\w+)?\.config",
                    RegexOptions.IgnoreCase);

                // if not leave the menu hidden
                if (!isConfig) return;
                var project = GetSelectedProject();
                if (project == null) return;

                var transformationsEnabler = await GetTransformationsEnabler(project, quick: true);
                if (transformationsEnabler == null) return;
                if (!transformationsEnabler.CanEnableBuildTimeTransformations)
                    return;

                menuCommand.Visible = true;
                menuCommand.Enabled = true;
            }
            catch (Exception exception)
            {
#if DEBUG
                // what the heck just happened?
                if (System.Diagnostics.Debugger.IsAttached) throw;
#endif
                LogUnhandledError(exception);
            }
        }

        /// <summary>
        /// User right-clicked on a project; determine whether to show "Enable build-time transformations" menu item
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void EnableBuildTimeTransformationsMenuItemProject_BeforeQueryStatus(object sender, EventArgs e)
        {
            try
            {
                // get the menu that fired the event
                var menuCommand = sender as OleMenuCommand;
                if (menuCommand == null) return;

                menuCommand.Visible = false;
                menuCommand.Enabled = false;

                var project = GetSelectedProject();
                if (project == null) return;

                var transformationsEnabler = await GetTransformationsEnabler(project);
                if (transformationsEnabler == null) return;
                if (!transformationsEnabler.CanEnableBuildTimeTransformations)
                    return;

                menuCommand.Visible = true;
                menuCommand.Enabled = true;

            }
            catch (Exception exception)
            {
#if DEBUG
                // what the heck just happened?
                if (System.Diagnostics.Debugger.IsAttached) throw;
#endif
                LogUnhandledError(exception);
            }
        }

        /// <summary>
        /// User clicked on "Enable build-time transformations"
        /// </summary>
        private async void EnableBuildTimeTransformationsMenuItem_Invoke(object sender, EventArgs e)
        {
            var project = GetSelectedProject();
            if (project == null) return;
            var transformationsEnabler = await GetTransformationsEnabler(project);
            if (transformationsEnabler == null) return;

            BeginPackageBusy();

            try
            {
                if (await transformationsEnabler.EnableBuildTimeConfigTransformations())
                    LogSuccess();
                else LogCancelOrAbort();
                
            }
            catch (Exception exception)
            {
#if DEBUG
                // what the heck just happened?
                if (System.Diagnostics.Debugger.IsAttached) throw;
#endif
                LogAndPromptUnhandledError(exception);
            }

            EndPackageBusy();

        }

#endregion

#region AddMissingTransformations

        private async void AddMissingTransformationsMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            try { 
                // get the menu that fired the event
                var menuCommand = sender as OleMenuCommand;
                if (menuCommand == null) return;

                // start by assuming that the menu will not be shown
                menuCommand.Visible = false;
                menuCommand.Enabled = false;

                IVsHierarchy hierarchy = null;
                var itemid = VSConstants.VSITEMID_NIL;

                if (!IsSingleProjectItemSelection(out hierarchy, out itemid)) return;
                // Get the file path
                string itemFullPath = null;
                ((IVsProject)hierarchy).GetMkDocument(itemid, out itemFullPath);
                FileInfo transformFileInfo = null;
                try
                {
                    transformFileInfo = new FileInfo(itemFullPath);
                }
                catch
                {
                    return;
                }

                // then check if the file is named 'web.config'
                var isConfig = Regex.IsMatch(transformFileInfo.Name, @"[Web|App](\.\w+)?\.config",
                    RegexOptions.IgnoreCase);

                // if not leave the menu hidden
                if (!isConfig) return;

                var project = GetSelectedProject();
                if (project == null) return;
                var transformationsEnabler = await GetTransformationsEnabler(project);
                if (transformationsEnabler == null) return;
                if (transformationsEnabler.HasMissingTransforms)
                {
                    menuCommand.Visible = true;
                    menuCommand.Enabled = true;
                }

            }
            catch (Exception exception)
            {
#if DEBUG
                // what the heck just happened?
                if (System.Diagnostics.Debugger.IsAttached) throw;
#endif
                LogAndPromptUnhandledError(exception);
            }
        }

        private async void AddMissingTransformationsMenuItem_Invoke(object sender, EventArgs e)
        {
            try
            {
                var project = GetSelectedProject();
                if (project == null) return;
                var transformationsEnabler = await GetTransformationsEnabler(project);
                if (transformationsEnabler == null) return;
                BeginPackageBusy();
                await transformationsEnabler.AddMissingTransforms();
                EndPackageBusy();
            }
            catch (Exception exception)
            {
#if DEBUG
                // what the heck just happened?
                if (System.Diagnostics.Debugger.IsAttached) throw;
#endif
                LogAndPromptUnhandledError(exception);
            }
        }

#endregion

        // source: http://www.diaryofaninja.com/blog/2014/02/18/who-said-building-visual-studio-extensions-was-hard
        private static bool IsSingleProjectItemSelection(out IVsHierarchy hierarchy, out uint itemid)
        {
            hierarchy = null;
            itemid = VSConstants.VSITEMID_NIL;
            var hr = VSConstants.S_OK;

            var monitorSelection = GetGlobalService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            var solution = GetGlobalService(typeof(SVsSolution)) as IVsSolution;
            if (monitorSelection == null || solution == null)
            {
                return false;
            }

            IVsMultiItemSelect multiItemSelect = null;
            var hierarchyPtr = IntPtr.Zero;
            var selectionContainerPtr = IntPtr.Zero;

            try
            {
                hr = monitorSelection.GetCurrentSelection(out hierarchyPtr, out itemid, out multiItemSelect,
                    out selectionContainerPtr);

                if (ErrorHandler.Failed(hr) || hierarchyPtr == IntPtr.Zero || itemid == VSConstants.VSITEMID_NIL)
                {
                    // there is no selection
                    return false;
                }

                // multiple items are selected
                if (multiItemSelect != null) return false;

                // there is a hierarchy root node selected, thus it is not a single item inside a project

                if (itemid == VSConstants.VSITEMID_ROOT) return false;

                hierarchy = Marshal.GetObjectForIUnknown(hierarchyPtr) as IVsHierarchy;
                if (hierarchy == null) return false;

                var guidProjectID = Guid.Empty;

                if (ErrorHandler.Failed(solution.GetGuidOfProject(hierarchy, out guidProjectID)))
                {
                    return false; // hierarchy is not a project inside the Solution if it does not have a ProjectID Guid
                }

                // if we got this far then there is a single project item selected
                return true;
            }
            finally
            {
                if (selectionContainerPtr != IntPtr.Zero)
                {
                    Marshal.Release(selectionContainerPtr);
                }

                if (hierarchyPtr != IntPtr.Zero)
                {
                    Marshal.Release(hierarchyPtr);
                }
            }
        }

        private void LogUnhandledError(Exception exception)
        {
            var logger = Dte.GetLogger();
            logger.LogError("Unhandled " + exception.GetType().Name + " (" + exception.GetType().FullName + "):\r\n"
                + exception.StackTrace
                + "\r\nPlease forward this log to " + FastKoalaMaintainerEmail);
        }

        private void LogAndPromptUnhandledError(Exception exception)
        {
            var logger = Dte.GetLogger();
            logger.LogError("Unhandled " + exception.GetType().Name + " (" + exception.GetType().FullName + "):\r\n"
                + exception.StackTrace
                + "\r\nPlease forward this log to " + FastKoalaMaintainerEmail);
            if (shownErrorMsg) return;
            shownErrorMsg = true;
            var response = MessageBox.Show(GetNativeWindow(),
                "Uh, something weird happened in Fast Koala, might you please do us all a favor and send the "
                + "contents of the output window to " + FastKoalaMaintainerEmail + "?", "Fast Koala", MessageBoxButtons.YesNo,
                MessageBoxIcon.Error);
            logger.LogInfo("[Your response: '" + response.ToString() + "'] .. " 
                + (response == DialogResult.No ? "*snif*" : "Thanks"));
        }

        private void LogCancelOrAbort()
        {
            VsEnvironment.Dte.GetLogger().LogWarn("Action was canceled or aborted.");
        }

        private void LogSuccess()
        {
            VsEnvironment.Dte.GetLogger().LogInfo("Done.");
        }

        private async Task<PSBuildScriptSupportInjector> GetNewPowerShellBuildScriptSupportInjector()
        {
            var logger = Dte.GetLogger();
            var project = GetSelectedProject();
            var result = new PSBuildScriptSupportInjector(project, 
                await VsFileSystemManipulatorFactory.GetFileSystemManipulatorForEnvironment(project),
                logger, GetNativeWindow());
            return result;
        }
        private async Task<NodeJSBuildScriptSupportInjector> GetNewNodeJSBuildScriptSupportInjector()
        {
            var logger = Dte.GetLogger();
            var project = GetSelectedProject();
            var result = new NodeJSBuildScriptSupportInjector(project,
                await VsFileSystemManipulatorFactory.GetFileSystemManipulatorForEnvironment(project),
                logger, GetNativeWindow());
            return result;
        }
    }
}