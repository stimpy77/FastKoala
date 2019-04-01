﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using EnvDTE;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.Shell;
using Wijits.FastKoala.Logging;
using Wijits.FastKoala.SourceControl;
using Wijits.FastKoala.Utilities;
using Task = System.Threading.Tasks.Task;

namespace Wijits.FastKoala.Transformations
{
    public class BuildTimeTransformationsEnabler
    {
        private readonly DTE _dte;
        private readonly ISccBasicFileSystem _io;
        private readonly ILogger _logger;
        private readonly IWin32Window _ownerWindow;
        private string _projectName;
        private string _projectUniqueName;

        public BuildTimeTransformationsEnabler(Project project, ILogger logger, ISccBasicFileSystem io,
            IWin32Window ownerWindow)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte = project.DTE;
            Project = project;
            ProjectProperties = new ProjectProperties(project);
            _logger = logger;
            _io = io;
            _ownerWindow = ownerWindow;
        }

        /// <remarks>Menu item entry point</remarks>
        public async Task<bool> EnableBuildTimeConfigTransformationsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!Project.Saved || !Project.DTE.Solution.Saved ||
                string.IsNullOrEmpty(Project.FullName) || string.IsNullOrEmpty(Project.DTE.Solution.FullName))
            {
                var saveDialogResult = MessageBox.Show(_ownerWindow, "Save pending changes to solution?",
                    "Save pending changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (saveDialogResult == DialogResult.OK || saveDialogResult == DialogResult.Yes)
                    await _dte.SaveAllAsync();
            }
            if (!Project.Saved || string.IsNullOrEmpty(Project.FullName))
            {
                var saveDialogResult = MessageBox.Show(_ownerWindow,
                    "Pending changes need to be saved. Please save the project and solution before enabling build-time transformations, then retry.",
                    "Aborted", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                if (saveDialogResult != DialogResult.Cancel) await _dte.SaveAllAsync();
                return false;
            }

            if (!CanEnableBuildTimeTransformations) return false;

            var message = "Are you sure you want to enable build-time config transformations? This will introduce changes to your project file.";
            var title = "Enable build-time config transformations? (Confirmation)";
            if (ProjectIsWebType || ProjectLooksLikeClickOnce)
            {
                title = "Enable inline build-time transformations? (Confirmation)";
                message = "This action will cause your " + (ProjectProperties.AppCfgType ?? DetermineAppCfgType())
                          + ".config to be regenerated in your design-time environment every time you build. "
                          + "Are you sure you want to enable inline build-time config transformations? Doing so will introduce changes to your project file.";
            }
            var dialogResult = MessageBox.Show(_ownerWindow,
                message, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (dialogResult == DialogResult.Cancel) return false;

            _logger.LogInfo("Enabling config transformations.");
            Environment.CurrentDirectory = Project.GetDirectory();

            // 1. determine if web.config vs app.config
            if (string.IsNullOrEmpty(ProjectProperties.AppCfgType))
            {
                ProjectProperties.AppCfgType = ProjectIsWebType ? "Web" : "App";
            }

            // 2. determine if need to use inline transformations or bin transformations
            //  >> if web or clickonce, inline is mandatory
            var inlineTransformations = ProjectProperties.InlineAppCfgTransforms
                                        ?? (ProjectProperties.InlineAppCfgTransforms =
                                            (ProjectIsWebType || ProjectLooksLikeClickOnce)).Value;
            if (!inlineTransformations && ProjectLooksLikeClickOnce)
                // we're converting existing build-time transforms to 'inline'
                inlineTransformations = (bool)(ProjectProperties.InlineAppCfgTransforms = true);

            bool prepresult;

            // in nested:
            // 3. if inline transformations, determine config folder
            // 4. if inline transformations, move and/or create web.config and related transforms to config folder
            // 4b. update project XML for moved files
            // 4c. inject warning xml to base
            if (inlineTransformations) prepresult = await PrepEnableInlineBuildTimeConfigTransformationsAsync();

            // create missing web.config and related transforms to config folder
            // also make sure that the project has the proper task added (as with the case of app.config non-clickonce)
            else prepresult = await PrepEnableBuildTimeConfigTransformationsForBinAsync();

            if (prepresult == false) return false;
            ProjectProperties.BuildTimeAppCfgTransformsEnabled = true;

            // 5. inject target definition to project
            /* web app (this should already be in the project so don't add it):
             * <Import Project="$(VSToolsPath)\WebApplications\Microsoft.WebApplication.targets" 
                    Condition="'$(VSToolsPath)' != ''" /> 
             */
            /* non-web app:
             *  <UsingTask TaskName="TransformXml"
                   AssemblyFile="$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\Web\Microsoft.Web.Publishing.Tasks.dll"/>
             */
            EnsureTransformXmlTaskInProject();

            // ensure target is invoked on build
            // >> two parts for this:
            // >> 6) add a target that invokes the TransformXml task
            EnsureTransformXmlTarget();

            // >> 7) ensure that the target gets invoked (either with Before/AfterBuild or DefaultTargets)


            // 8. save changes and reload project
            await Project.SaveProjectRootAsync();

            return true;
        }

        /// <remarks>Menu item entry point</remarks>
        public async Task AddMissingTransformsAsync()
        {
            var baseConfigFile = GetBaseConfigPath();
            if (baseConfigFile == null) return;
            await AddMissingTransformsAsync(baseConfigFile);
            await Project.SaveProjectRootAsync();
        }

        private string DetermineAppCfgType()
        {
            return ProjectIsWebType ? "Web" : "App";
        }

        private void EnsureTransformXmlTarget()
        {
            var projectRoot = Project.GetProjectRoot();
            // ReSharper disable once SimplifyLinqExpression
            if (projectRoot.Targets.Any(t => t.Name == "TransformOnBuild")) return;

            var transformOnBuildTarget = projectRoot.AddTarget("TransformOnBuild");
            transformOnBuildTarget.BeforeTargets = "PrepareForBuild";
            var propertyGroup = transformOnBuildTarget.AddPropertyGroup();
            // ReSharper disable InconsistentNaming
            var outputTypeExtension_exe = propertyGroup.AddProperty("outputTypeExtension", "exe");
            outputTypeExtension_exe.Condition = "'$(OutputType)' == 'exe' or '$(OutputType)' == 'winexe'";
            var outputTypeExtension_dll = propertyGroup.AddProperty("outputTypeExtension", "dll");
            outputTypeExtension_dll.Condition = "'$(OutputType)' == 'Library'";
            // ReSharper enable InconsistentNaming

            var trWeb = transformOnBuildTarget.AddTask("TransformXml");
            trWeb.Condition = @"'$(AppCfgType)' == 'Web' and Exists('$(ConfigDir)\Web.Base.config') and Exists('$(ConfigDir)\Web.$(Configuration).config')";
            trWeb.SetParameter("Source", @"$(ConfigDir)\Web.Base.config");
            trWeb.SetParameter("Transform", @"$(ConfigDir)\Web.$(Configuration).config");
            trWeb.SetParameter("Destination", @"Web.config");
            var trClickOnce = transformOnBuildTarget.AddTask("TransformXml");
            trClickOnce.Condition = @"'$(AppCfgType)' == 'App' and $(InlineAppCfgTransforms) == true and Exists('$(ConfigDir)\App.Base.config') and Exists('$(ConfigDir)\App.$(Configuration).config')";
            trClickOnce.SetParameter("Source", @"$(ConfigDir)\App.Base.config");
            trClickOnce.SetParameter("Transform", @"$(ConfigDir)\App.$(Configuration).config");
            trClickOnce.SetParameter("Destination", @"App.config");
            var trPrepBinOut = transformOnBuildTarget.AddTask("MakeDir");
            trPrepBinOut.Condition = "'$(AppCfgType)' == 'App' and $(InlineAppCfgTransforms) != true and Exists('App.config') and !Exists('$(OutDir)')";
            trPrepBinOut.SetParameter("Directories", "$(OutDir)");
            var trBinOut = transformOnBuildTarget.AddTask("TransformXml");
            trBinOut.Condition = "'$(AppCfgType)' == 'App' and $(InlineAppCfgTransforms) != true and Exists('App.config') and Exists('App.$(Configuration).config')";
            trBinOut.SetParameter("Source", @"App.config");
            trBinOut.SetParameter("Transform", @"App.$(Configuration).config");
            trBinOut.SetParameter("Destination", @"$(OutDir)$(AssemblyName).$(outputTypeExtension).config");
            var trWebAlt = transformOnBuildTarget.AddTask("Copy");
            trWebAlt.Condition = @"'$(AppCfgType)' == 'Web' and Exists('$(ConfigDir)\Web.Base.config') and !Exists('$(ConfigDir)\Web.$(Configuration).config')";
            trWebAlt.SetParameter("SourceFiles", @"$(ConfigDir)\Web.Base.config");
            trWebAlt.SetParameter("DestinationFiles", @"Web.config");
            var trClickOnceAlt = transformOnBuildTarget.AddTask("Copy");
            trClickOnceAlt.Condition = @"'$(AppCfgType)' == 'App' and $(InlineAppCfgTransforms) == true and Exists('$(ConfigDir)\App.Base.config') and !Exists('$(ConfigDir)\App.$(Configuration).config')";
            trClickOnceAlt.SetParameter("SourceFiles", @"$(ConfigDir)\App.Base.config");
            trClickOnceAlt.SetParameter("DestinationFiles", @"App.config");
            var trBinOutAlt = transformOnBuildTarget.AddTask("Copy");
            trBinOutAlt.Condition = "'$(AppCfgType)' == 'App' and $(InlineAppCfgTransforms) != true and Exists('App.config') and !Exists('App.$(Configuration).config')";
            trBinOutAlt.SetParameter("SourceFiles", @"App.config");
            trBinOutAlt.SetParameter("DestinationFiles", @"$(OutDir)$(AssemblyName).$(outputTypeExtension).config");
        }

        private void EnsureTransformXmlTaskInProject()
        {
            if (ProjectProperties.AppCfgType.ToLower() == "web")
            {
                // we're covered
                /* web app (this should already be in the project so don't add it):
                 * <Import Project="$(VSToolsPath)\WebApplications\Microsoft.WebApplication.targets" 
                        Condition="'$(VSToolsPath)' != ''" /> 
                 */
            }
            else
            {
                /* non-web app:
                 *  <UsingTask TaskName="TransformXml"
                       AssemblyFile="$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\Web\Microsoft.Web.Publishing.Tasks.dll"/>
                 */
                // ReSharper disable SimplifyLinqExpression
                var projectRoot = Project.GetProjectRoot();
                if (!projectRoot.UsingTasks.Any(ut => ut.TaskName == "TransformXml"))
                {
                    projectRoot.AddUsingTask("TransformXml",
                        @"$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\Web\Microsoft.Web.Publishing.Tasks.dll",
                        null);
                }
                // ReSharper enable SimplifyLinqExpression
            }
        }

        private string PromptConfigDir()
        {
            var defaultConfigDir = ProjectProperties.ConfigDir;
            if (string.IsNullOrEmpty(defaultConfigDir) || defaultConfigDir == "." || defaultConfigDir == ".\\")
                defaultConfigDir = "App_Config";
            var cfgprompt = new ConfigDirPrompt {ConfigDir = defaultConfigDir};
            var dialogResult = cfgprompt.ShowDialog(_ownerWindow);
            return dialogResult != DialogResult.Cancel ? cfgprompt.ConfigDir : null;
        }

        private async Task<bool> PrepEnableBuildTimeConfigTransformationsForBinAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            // create missing web.config and related transforms to config folder
            // also make sure that the project has the proper usingtask added (as with the case of app.config non-clickonce)

            var appcfgtype = ProjectProperties.AppCfgType;
            var cfgfilename = appcfgtype + ".config";
            var baseConfigFileFullPath = Path.Combine(Project.GetDirectory(), cfgfilename);
            var baseConfigFileRelPath = FileUtilities.GetRelativePath(Project.GetDirectory(), baseConfigFileFullPath);
            if (string.IsNullOrEmpty(Project.GetConfigFile()))
            {
                baseConfigFileFullPath = Path.Combine(Project.GetDirectory(), ProjectProperties.AppCfgType + ".config");
                FileUtilities.WriteFileFromAssemblyResourceManifest(@"Transforms\{0}.config", appcfgtype, "App", baseConfigFileFullPath);
                if (await _io.ItemIsUnderSourceControl(Project.FullName))
                {
                    await _io.Add(baseConfigFileFullPath);
                }
                AddItemToProject("None", baseConfigFileRelPath);
            }
            await AddMissingTransformsAsync(baseConfigFileFullPath);

            return true;
        }

        private string GetBaseConfigPath()
        {
            var baseConfigPath = Project.GetConfigFile();
            if (ProjectProperties.InlineAppCfgTransforms == true)
            {
                baseConfigPath = Path.Combine(Project.GetDirectory(), ProjectProperties.ConfigDir,
                    ProjectProperties.AppCfgType + "." + ProjectProperties.CfgBaseName + ".config");
                if (!File.Exists(baseConfigPath))
                {
                    return null;
                }
            }
            return baseConfigPath;
        }

        private async Task AddMissingTransformsAsync(string baseConfigFile)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var appcfgtype = ProjectProperties.AppCfgType;
            var baseFileInfo = new FileInfo(baseConfigFile);
            var cfgfilename = baseFileInfo.Name;
            foreach (var cfg in Project.ConfigurationManager.Cast<Configuration>().ToList())
            {
                var cfgname = cfg.ConfigurationName;
                var xfrmname = appcfgtype + "." + cfgname + ".config";
                var xfrmFullPath = 
                    Path.Combine(Project.GetDirectory(), baseFileInfo.DirectoryName, xfrmname);
                var xfrmRelPath = FileUtilities.GetRelativePath(Project.GetDirectory(), xfrmFullPath);
                if (!File.Exists(xfrmFullPath))
                {
                    _logger.LogInfo("Creating " + xfrmRelPath);
                    FileUtilities.WriteFileFromAssemblyResourceManifest(@"Transforms\Web.{0}.config", cfgname, "Release", xfrmFullPath);
                    if (await _io.ItemIsUnderSourceControl(Project.FullName))
                    {
                        await _io.Add(xfrmFullPath);
                    }
                }
                var projectItem = Project.GetProjectRoot().Items.SingleOrDefault(item => item.Include == xfrmRelPath)
                                  ?? AddItemToProject("None", xfrmRelPath);
                // ReSharper disable once SimplifyLinqExpression
                if (!projectItem.HasMetadata ||
                    !projectItem.Metadata.Any(m => m.Name == "DependentUpon"))
                {
                    projectItem.AddMetadata("DependentUpon", cfgfilename);
                }
                else if (projectItem.Metadata.Single(m => m.Name == "DependentUpon").Value != cfgfilename)
                {
                    projectItem.Metadata.Single(m => m.Name == "DependentUpon").Value = cfgfilename;
                }
            }
        }

        private async Task<bool> PrepEnableInlineBuildTimeConfigTransformationsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            // 3. if inline transformations, determine config folder
            if (string.IsNullOrEmpty(ProjectProperties.ConfigDir))
            {
                if (string.IsNullOrEmpty(ProjectProperties.ConfigDir = PromptConfigDir()))
                    return false;
            }

            // 4. if inline transformations, move and/or create web.config and related transforms to config folder
            // 4b. update project XML for moved files
            var baseConfig = Project.GetConfigFile();
            string baseConfigFullPath;
            string baseConfigRelPath;
            if (string.IsNullOrEmpty(baseConfig))
            {
                // create the App/Web.config file
                var baseConfigFile = string.Format(@"{0}.{1}.config", ProjectProperties.AppCfgType,
                    ProjectProperties.CfgBaseName);
                baseConfigRelPath = string.Format(@"{0}\{1}", ProjectProperties.ConfigDir, baseConfigFile);
                baseConfigFullPath = Path.Combine(Project.GetDirectory(), baseConfigRelPath);
                _logger.LogInfo("Creating " + baseConfigRelPath);
                FileUtilities.WriteFileFromAssemblyResourceManifest(@"Transforms\{0}.config", ProjectProperties.AppCfgType, "Web", baseConfigFullPath);
                if (await _io.ItemIsUnderSourceControl(Project.FullName))
                {
                    await _io.Add(baseConfigFullPath);
                }
                AddItemToProject("None", baseConfigRelPath);

                foreach (var cfg in Project.ConfigurationManager.Cast<Configuration>().ToList())
                {
                    var cfgname = cfg.ConfigurationName;
                    var xfrmname = ProjectProperties.AppCfgType + "." + cfgname + ".config";
                    var xfrmpath = Path.Combine(ProjectProperties.ConfigDir, xfrmname);
                    var xfrmFullPath = Path.Combine(Project.GetDirectory(), xfrmpath);
                    if (!File.Exists(xfrmpath))
                    {
                        _logger.LogInfo("Creating " + xfrmname);
                        FileUtilities.WriteFileFromAssemblyResourceManifest(@"Transforms\Web.{0}.config", cfgname, "Release", xfrmpath);
                        if (await _io.ItemIsUnderSourceControl(Project.FullName))
                        {
                            await _io.Add(xfrmFullPath);
                        }
                        var item = AddItemToProject("None", xfrmpath);
                        item.AddMetadata("DependentUpon", baseConfigFile);
                    }
                }
            }
            else
            {
                // move the App/Web.config file
                var oldcfgfile = string.Format("{0}.config", ProjectProperties.AppCfgType);
                var cfgfullpath = Path.Combine(Project.GetDirectory(), oldcfgfile);
                var newBaseConfigFile = string.Format(@"{0}.{1}.config", 
                    ProjectProperties.AppCfgType, ProjectProperties.CfgBaseName);
                baseConfigRelPath = string.Format(@"{0}\{1}", ProjectProperties.ConfigDir, newBaseConfigFile);
                var newBaseConfigFullPath = baseConfigFullPath = Path.Combine(Project.GetDirectory(), baseConfigRelPath);
                _logger.LogInfo("Moving " + oldcfgfile + " to " + baseConfigRelPath);
                var parentDir = Directory.GetParent(newBaseConfigFullPath).FullName;
                if (!Directory.Exists(parentDir)) Directory.CreateDirectory(parentDir);
                await _io.Move(cfgfullpath, newBaseConfigFullPath);
                if (!File.Exists(newBaseConfigFullPath)) File.Move(cfgfullpath, newBaseConfigFullPath);

                // and update the proejct manifest reference to the file
                var configItem = Project.GetProjectRoot().Items
                    .SingleOrDefault(item => item.Include.ToLower() == oldcfgfile.ToLower());
                if (configItem == null) AddItemToProject("None", baseConfigRelPath);
                else configItem.Include = baseConfigRelPath;
                AddItemToProject("Content", oldcfgfile); // needs to be in the project manifest, but not in source control

                foreach (var cfg in Project.ConfigurationManager.Cast<Configuration>().ToList())
                {
                    // move the App/Web.config transform file
                    var cfgname = cfg.ConfigurationName;
                    var xfrmname = ProjectProperties.AppCfgType + "." + cfgname + ".config";
                    var oldxfrmpath = Path.Combine(Project.GetDirectory(), xfrmname);
                    var xfrmpath = Path.Combine(ProjectProperties.ConfigDir, xfrmname);
                    var xfrmFullPath = Path.Combine(Project.GetDirectory(), xfrmpath);
                    var oldxfrmname = (new FileInfo(oldxfrmpath)).Name;
                    var replaceXfrm = false;
                    if (!File.Exists(xfrmpath) && File.Exists(oldxfrmpath))
                    {
                        // check to see if it's the Visual Studio default (unmodified)
                        // if so, replace it
                        // if not, move it
                        var isUnmodifiedDefault = false;
                        if (ProjectProperties.AppCfgType.ToLower() == "web")
                        {
                            var oldXfrmContent = File.ReadAllText(oldxfrmpath);
                            var defaultXfrmContent = GetManifestResourceStreamContent(
                                @"Transforms\" + (oldxfrmname.Replace(".config", ".Default.config")));
                            isUnmodifiedDefault = oldXfrmContent == defaultXfrmContent;
                        }
                        if (isUnmodifiedDefault)
                        {
                            await _io.Delete(oldxfrmpath);
                            replaceXfrm = true;
                        }
                        else
                        {
                            _logger.LogInfo("Moving " + xfrmname + " to " + xfrmpath);
                            await _io.Move(oldxfrmpath, xfrmFullPath);
                            if (!File.Exists(xfrmFullPath)) File.Move(oldxfrmpath, xfrmFullPath);
                        }
                    }
                    if (replaceXfrm || (!File.Exists(xfrmpath) && !File.Exists(oldxfrmpath)))
                    {
                        _logger.LogInfo("Creating " + xfrmpath);
                        FileUtilities.WriteFileFromAssemblyResourceManifest(@"Transforms\Web.{0}.config", cfgname, "Release", xfrmpath);
                        if (await _io.ItemIsUnderSourceControl(Project.FullName))
                        {
                            await _io.Add(xfrmFullPath);
                        }
                        if (!replaceXfrm)
                        {
                            var item = AddItemToProject("None", xfrmpath);
                            item.AddMetadata("DependentUpon", newBaseConfigFile);
                        }
                    }

                    // and update the proejct manifest reference to the file
                    var prjroot = Project.GetProjectRoot();
                    var xfrmitem = prjroot.Items.SingleOrDefault(item => item.Include.ToLower() == xfrmname.ToLower())
                                ?? prjroot.Items.SingleOrDefault(item => item.Include.ToLower() == xfrmpath.ToLower());
                    if (xfrmitem == null) xfrmitem = AddItemToProject("None", xfrmpath);
                    else xfrmitem.Include = xfrmpath;
                    var metadata = xfrmitem.Metadata.SingleOrDefault(m => m.Name == "DependentUpon")
                                   ?? xfrmitem.AddMetadata("DependentUpon", newBaseConfigFile);
                    metadata.Value = newBaseConfigFile;
                }
            }

            // 4c. inject warning xml to base
            if (File.Exists(baseConfigFullPath))
                await InjectBaseConfigWarningCommentAsync(baseConfigFullPath);
            else _logger.LogWarn("Unexpected missing base file: " + baseConfigFullPath);

            // add <AppConfigBaseFileFullPath>
            var projectRoot = Project.GetProjectRoot();
            if (!projectRoot.Properties.Any(property => property.Name == "AppConfigBaseFileFullPath"))
            {
                var appConfigBaseRelativePath = projectRoot.AddProperty("AppConfigBaseFilePath", @"App.config");
                appConfigBaseRelativePath.Condition = @"Exists('$(MSBuildProjectDirectory)\App.config')";
                var webConfigBaseRelativePath = projectRoot.AddProperty("AppConfigBaseFilePath", @"Web.config");
                webConfigBaseRelativePath.Condition = @"Exists('$(MSBuildProjectDirectory)\Web.config')";
                var baseConfigBaseRelativePath = projectRoot.AddProperty("AppConfigBaseFilePath", 
                    @"$(ConfigDir)\$(AppCfgType).Base.config");
                baseConfigBaseRelativePath.Condition = @"'$(InlineAppCfgTransforms)' == 'true'";
                
                projectRoot.AddProperty("AppConfigBaseFileFullPath",
                    @"$(MSBuildProjectDirectory)\$(AppConfigBaseFilePath)");
            }

            // add ignore for source control
            var appcfgfilter = ProjectProperties.AppCfgType + ".config";
            var comment = string.Format("{0} is a transient file generated at build-time. See instead {1}", 
                appcfgfilter, baseConfigRelPath);
            await _io.AddItemToIgnoreList(appcfgfilter, comment);

            return true;
        }

        private string GetManifestResourceStreamContent(string resourceName)
        {
            resourceName = typeof (FastKoalaPackage).Namespace + @".Resources." + resourceName.Replace("\\", ".");
            var assembly = typeof (BuildTimeTransformationsEnabler).Assembly;
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using (stream)
            {
                using (var sr = new StreamReader(stream))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        private async Task InjectBaseConfigWarningCommentAsync(string baseConfigFullPath)
        {
            var xml = new XmlDocument();
            xml.Load(baseConfigFullPath);
            var commentXml = string.Format(@"

    !! WARNING !!
    Do not modify the {0}.config file directly. Always edit the configurations in
    - {1}\{0}.{2}.config
    - {1}\{0}.[Debug|Release].config

", ProjectProperties.AppCfgType, ProjectProperties.ConfigDir, ProjectProperties.CfgBaseName);
            var commentXmlNode = xml.CreateComment(commentXml);
            Debug.Assert(xml.DocumentElement != null, "xml.DocumentElement != null");
            if (xml.DocumentElement.ChildNodes.Count > 0)
                xml.DocumentElement.InsertBefore(commentXmlNode, xml.DocumentElement.FirstChild);
            else xml.DocumentElement.AppendChild(commentXmlNode);
            await _io.Checkout(baseConfigFullPath);
            xml.Save(baseConfigFullPath);
        }

        private ProjectItemElement AddItemToProject(string buildAction, string itemRelativePath)
        {
            var projectRoot = Project.GetProjectRoot();
            var itemGroup = projectRoot.ItemGroups.LastOrDefault(ig => string.IsNullOrEmpty(ig.Condition))
                            ?? projectRoot.AddItemGroup();
            return itemGroup.AddItem(buildAction, itemRelativePath);
        }

        public Project Project
        {
            get
            {
                var t = _dte.GetProjectByUniqueNameAsync(_projectUniqueName);
                t.ConfigureAwait(true);
                var result = t.Result;
                if (result == null)
                {
                    t = _dte.GetProjectByNameAsync(_projectName);
                    t.ConfigureAwait(true);
                    result = t.Result;
                }
                return result;
            }
            set
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (value == null) return;
                _projectName = value.Name;
                _projectUniqueName = value.UniqueName;
            }
        }

        public bool CanEnableBuildTimeTransformations
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (Project == null) return false;
                // ASP.NET 5 projects (not supported) seem to be using the .xproj file extension
                if (Project.FullName.ToLower().EndsWith(".xproj")) return false;

                var defaultResult = !HasBuildTimeTransformationsEnabled &&
                       !Project.IsType(ProjectTypes.WebSite) &&
                       !string.IsNullOrWhiteSpace(Project.FullName) &&
                       !Project.FullName.Contains("://") &&
                       File.Exists(Project.FullName);

                if (defaultResult) return defaultResult;
                if (!HasBuildTimeTransformationsEnabled) return false;

                var ptypeTask = Project.GetProjectTypeGuidsAsync();
                ptypeTask.ConfigureAwait(true);
                var pttype = ptypeTask.Result;
                var presult = pttype
                            .Split(';')
                            .Select(pt => new Guid(pt))
                            .Any(
                                pt =>
                                    new[]
                                    {
                                        ProjectTypes.WindowsCSharp,
                                        ProjectTypes.WindowsVB,
                                        ProjectTypes.WindowsPresentationFoundation
                                    }.Contains(pt));
                return (presult) &&
                        ProjectLooksLikeClickOnce &&
                        ProjectProperties.InlineAppCfgTransforms != true;
            }
        }

        public bool HasBuildTimeTransformationsEnabled
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (Project.IsType(ProjectTypes.WebSite) ||
                    string.IsNullOrWhiteSpace(Project.FullName) ||
                    Project.FullName.Contains("://") ||
                    !File.Exists(Project.FullName))
                {
                    return false;
                }
                return ProjectProperties.BuildTimeAppCfgTransformsEnabled;
            }
        }

        public ProjectProperties ProjectProperties { get; set; }

        public bool ProjectIsWebType
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var projectKind = !string.IsNullOrEmpty(Project.Kind) ? Guid.Parse(Project.Kind) : (Guid?) null;
                var webProjectTypes = new[]
                {
                    ProjectTypes.WebSite,
                    ProjectTypes.AspNetMvc10,
                    ProjectTypes.AspNetMvc20,
                    ProjectTypes.AspNetMvc30,
                    ProjectTypes.AspNetMvc40,
                    ProjectTypes.WebApplication
                };
                var ptypesTask = Project.GetProjectTypeGuidsAsync();
                ptypesTask.ConfigureAwait(true);
                var ptypes = ptypesTask.Result;
                var projectTypes = ptypes.Split(';').Select(Guid.Parse);
                return projectKind.HasValue && (webProjectTypes.Contains(projectKind.Value) ||
                                                projectTypes.Any(t => webProjectTypes.Contains(t)));
            }
        }

        public bool ProjectLooksLikeClickOnce
        {
            get { return !string.IsNullOrEmpty(ProjectProperties.GetPropertyValue("IsWebBootstrapper")); }
        }

        public bool HasMissingTransforms
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (!HasBuildTimeTransformationsEnabled) return false;
                var baseConfigPath = GetBaseConfigPath();
                var baseConfigInfo = new FileInfo(baseConfigPath);
                var configDir = baseConfigInfo.DirectoryName;
                foreach (var cfg in Project.ConfigurationManager.Cast<Configuration>().ToList())
                {
                    var cfgname = cfg.ConfigurationName;
                    Debug.Assert(configDir != null, "configDir != null");
                    var cfgfile = Path.Combine(configDir, (ProjectProperties.AppCfgType ?? DetermineAppCfgType())
                                                          + "." + cfgname
                                                          + ".config");
                    if (!File.Exists(cfgfile)) return true;
                    var relativePath = FileUtilities.GetRelativePath(Project.GetDirectory(), cfgfile);
                    if (Project.GetProjectRoot().Items.SingleOrDefault(item => item.Include == relativePath) == null)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}