using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using EnvDTE;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Wijits.FastKoala.Logging;
using Wijits.FastKoala.SourceControl;
using Wijits.FastKoala.Utilities;
using ProjectItem = EnvDTE.ProjectItem;

namespace Wijits.FastKoala.Transformations
{
    public class BuildTimeTransformationsEnabler
    {
        private readonly EnvDTE.DTE _dte;
        private string _projectName;
        private string _projectUniqueName;
        private readonly ILogger _logger;
        private ISccBasicFileSystem _io;
        private IWin32Window _ownerWindow;

        public BuildTimeTransformationsEnabler(EnvDTE.Project project, ILogger logger, ISccBasicFileSystem io, IWin32Window ownerWindow)
        {
            _dte = project.DTE;
            Project = project;
            ProjectProperties = new ProjectProperties(project);
            _logger = logger;
            _io = io;
            _ownerWindow = ownerWindow;
        }

        public async Task<bool> EnableBuildTimeConfigTransformations()
        {
            if (!CanEnableBuildTimeTransformations) return false;

            var dialogResult = MessageBox.Show(_ownerWindow,
                "Are you sure you want to enable build-time transformations?",
                "Enable build-time transformations? (Confirmation)", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (dialogResult == DialogResult.Cancel) return false;

            _logger.LogInfo("Enabling config transformations.");
            Environment.CurrentDirectory = Project.GetDirectory();

            if (!Project.Saved) Project.Save();

            // 1. determine if web.config vs app.config
            var originalConfigFile = Project.GetConfigFile();
            if (string.IsNullOrEmpty(ProjectProperties.AppCfgType))
            {
                Guid? projectKind = !string.IsNullOrEmpty(Project.Kind) ? Guid.Parse(Project.Kind) : (Guid?)null;
                var webProjectTypes = new[]
                    {
                        ProjectTypes.WebSite,
                        ProjectTypes.AspNetMvc10,
                        ProjectTypes.AspNetMvc20,
                        ProjectTypes.AspNetMvc30,
                        ProjectTypes.AspNetMvc40,
                        ProjectTypes.WebApplication
                    };
                var projectTypes = Project.GetProjectTypeGuids().Split(';').Select(Guid.Parse);
                if (projectKind.HasValue && (webProjectTypes.Contains(projectKind.Value) ||
                    projectTypes.Any(t => webProjectTypes.Contains(t))))
                {
                    ProjectProperties.AppCfgType = "Web";
                }
                else ProjectProperties.AppCfgType = "App";
            }
            if (string.IsNullOrEmpty(originalConfigFile))
            {
                originalConfigFile = Path.Combine(Project.GetDirectory(), ProjectProperties.AppCfgType + ".config");
            }
            var originalConfigFileName = new FileInfo(originalConfigFile).Name;
            
            // 2. determine if need to use inline transformations or bin transformations
            //  >> if web or clickonce, inline is mandatory
            var inlineTransformations = ProjectProperties.InlineTransformations ?? originalConfigFileName.ToLower() == "web.config" ? true : false;
            if (!inlineTransformations && !string.IsNullOrEmpty(ProjectProperties.GetPropertyValue("PublishUrl")))
                inlineTransformations = true;
            ProjectProperties.InlineTransformations = inlineTransformations;

            bool prepresult;

            // in nested:
            // 3. if inline transformations, determine config folder
            // 4. if inline transformations, move and/or create web.config and related transforms to config folder
            // 4b. update project XML for moved files
            // 4c. inject warning xml to base
            if (inlineTransformations) prepresult = await PrepEnableInlineBuildTimeConfigTransformations();

            // create missing web.config and related transforms to config folder
            // also make sure that the project has the proper task added (as with the case of app.config non-clickonce)
            else prepresult = await PrepEnableBuildTimeConfigTransformationsForBin();

            if (prepresult == false) return false;
            ProjectProperties.BuildTimeTransformsEnabled = true;

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
            await Project.SaveProjectRoot();

            return true;
        }

        private void EnsureTransformXmlTarget()
        {
            var projectRoot = Project.GetProjectRoot();
            // ReSharper disable once SimplifyLinqExpression
            if (!projectRoot.Targets.Any(t => t.Name == "TransformOnBuild"))
            {
                var transformOnBuildTarget = projectRoot.AddTarget("TransformOnBuild");
                transformOnBuildTarget.BeforeTargets = "Build";
                var propertyGroup = transformOnBuildTarget.AddPropertyGroup();
                var outputTypeExtension_exe = propertyGroup.AddProperty("outputTypeExtension", "exe");
                outputTypeExtension_exe.Condition = "'$(OutputType)' == 'exe' or '$(OutputType)' == 'winexe'";
                var outputTypeExtension_dll = propertyGroup.AddProperty("outputTypeExtension", "dll");
                outputTypeExtension_dll.Condition = "'$(OutputType)' == 'Library'";

                var trWeb = transformOnBuildTarget.AddTask("TransformXml");
                trWeb.Condition = "'$(AppCfgType)' == 'Web'";
                trWeb.SetParameter("Source", @"$(ConfigDir)\Web.Base.config");
                trWeb.SetParameter("Transform", @"$(ConfigDir)\Web.$(Configuration).config");
                trWeb.SetParameter("Destination", @"Web.config");
                var trClickOnce = transformOnBuildTarget.AddTask("TransformXml");
                trClickOnce.Condition = "'$(AppCfgType)' == 'App' and $(InlineTransformations) == true";
                trClickOnce.SetParameter("Source", @"$(ConfigDir)\App.Base.config");
                trClickOnce.SetParameter("Transform", @"$(ConfigDir)\App.$(Configuration).config");
                trClickOnce.SetParameter("Destination", @"App.config");
                var trBinOut = transformOnBuildTarget.AddTask("TransformXml");
                trBinOut.Condition = "'$(AppCfgType)' == 'App' and $(InlineTransformations) != true";
                trBinOut.SetParameter("Source", @"App.config");
                trBinOut.SetParameter("Transform", @"App.$(Configuration).config");
                trBinOut.SetParameter("Destination", @"$(OutDir)$(AssemblyName).$(outputTypeExtension).config");
            }
            
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
                // ReSharper disable once SimplifyLinqExpression
                var projectRoot = Project.GetProjectRoot();
                if (!projectRoot.UsingTasks.Any(ut => ut.TaskName == "TransformXml"))
                {
                    projectRoot.AddUsingTask("TransformXml",
                        @"$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\Web\Microsoft.Web.Publishing.Tasks.dll",
                        null);
                }
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

        private async Task<bool> PrepEnableBuildTimeConfigTransformationsForBin()
        {
            // create missing web.config and related transforms to config folder
            // also make sure that the project has the proper usingtask added (as with the case of app.config non-clickonce)

            var appcfgtype = ProjectProperties.AppCfgType;
            var cfgfilename = appcfgtype + ".config";
            var baseConfigFile = Path.Combine(Project.GetDirectory(), cfgfilename);
            if (string.IsNullOrEmpty(Project.GetConfigFile()))
            {
                baseConfigFile = Path.Combine(Project.GetDirectory(), ProjectProperties.AppCfgType + ".config");
                WriteFromManifest(@"Transforms\{0}.config", appcfgtype, "App", baseConfigFile);
                await _io.AddIfProjectIsSourceControlled(Project, baseConfigFile);
                AddItemToProject(baseConfigFile);
            }
            foreach (var cfg in Project.ConfigurationManager.Cast<Configuration>().ToList())
            {
                var cfgname = cfg.ConfigurationName;
                var xfrmname = appcfgtype + "." + cfgname + ".config";
                var xfrmpath = xfrmname;
                var xfrmfullpath = Path.Combine(Project.GetDirectory(), xfrmpath);
                if (!File.Exists(xfrmpath))
                {
                    _logger.LogInfo("Creating " + xfrmname);
                    WriteFromManifest(@"Transforms\Web.{0}.config", cfgname, "Release", xfrmpath);
                    var item = AddItemToProject(xfrmpath);
                    item.AddMetadata("DependentUpon", cfgfilename);
                    await _io.AddIfProjectIsSourceControlled(Project, cfgfilename);
                }
            }

            return true;
        }

        private void WriteFromManifest(string resourceName, string resourcePlaceholderValue,
            string resourcePlaceholderDefault, string targetPath)
        {
            resourceName = typeof(FastKoalaPackage).Namespace + @".Resources." + resourceName.Replace("\\", ".");
            var assembly = typeof (BuildTimeTransformationsEnabler).Assembly;
            using (var stream = assembly.GetManifestResourceStream(string.Format(resourceName, resourcePlaceholderValue))
                             ?? assembly.GetManifestResourceStream(string.Format(resourceName, resourcePlaceholderDefault)))
            {
                var parentDirectory = Directory.GetParent(targetPath).FullName;
                if (!Directory.Exists(parentDirectory)) Directory.CreateDirectory(parentDirectory);
                using (var fileStream = File.OpenWrite(targetPath))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }

        private async Task<bool> PrepEnableInlineBuildTimeConfigTransformations()
        {

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
            if (string.IsNullOrEmpty(baseConfig))
            {
                // create the App/Web.config file
                var cfgfilename = string.Format(@"{0}.config", ProjectProperties.AppCfgType);
                var baseConfigFile = string.Format(@"{0}.{1}.config", ProjectProperties.AppCfgType, ProjectProperties.CfgBaseName);
                var baseConfigPath = string.Format(@"{0}\{1}", ProjectProperties.ConfigDir, baseConfigFile);
                baseConfigFullPath = Path.Combine(Project.GetDirectory(), baseConfigPath);
                _logger.LogInfo("Creating " + baseConfigPath);
                WriteFromManifest(@"Transforms\{0}.config", ProjectProperties.AppCfgType, "Web", baseConfigFullPath);
                await _io.AddIfProjectIsSourceControlled(Project, baseConfigFullPath);
                AddItemToProject(baseConfigPath);


                foreach (var cfg in Project.ConfigurationManager.Cast<Configuration>().ToList())
                {
                    var cfgname = cfg.ConfigurationName;
                    var xfrmname = ProjectProperties.AppCfgType + "." + cfgname + ".config";
                    var xfrmpath = Path.Combine(ProjectProperties.ConfigDir, xfrmname);
                    var xfrmFullPath = Path.Combine(Project.GetDirectory(), xfrmpath);
                    if (!File.Exists(xfrmpath))
                    {
                        _logger.LogInfo("Creating " + xfrmname);
                        WriteFromManifest(@"Transforms\Web.{0}.config", cfgname, "Release", xfrmpath);
                        var item = AddItemToProject(xfrmpath);
                        item.AddMetadata("DependentUpon", baseConfigFile);
                        await _io.AddIfProjectIsSourceControlled(Project, xfrmFullPath);
                    }
                }
            }
            else
            {
                // move the App/Web.config file
                var oldcfgfile = string.Format("{0}.config", ProjectProperties.AppCfgType);
                var cfgfullpath = Path.Combine(Project.GetDirectory(), oldcfgfile);
                var newBaseConfigFile = string.Format(@"{0}.{1}.config", ProjectProperties.AppCfgType, ProjectProperties.CfgBaseName);
                var newBaseConfigPath = string.Format(@"{0}\{1}", ProjectProperties.ConfigDir, newBaseConfigFile);
                var newBaseConfigFullPath = baseConfigFullPath = Path.Combine(Project.GetDirectory(), newBaseConfigPath);
                _logger.LogInfo("Moving " + oldcfgfile + " to " + newBaseConfigPath);
                var parentDir = Directory.GetParent(newBaseConfigFullPath).FullName;
                if (!Directory.Exists(parentDir)) Directory.CreateDirectory(parentDir);
                await _io.Move(cfgfullpath, newBaseConfigFullPath);

                // and update the proejct manifest reference to the file
                var configItem = Project.GetProjectRoot().Items.SingleOrDefault(item => item.Include.ToLower() == oldcfgfile.ToLower());
                if (configItem == null) AddItemToProject(newBaseConfigPath);
                else configItem.Include = newBaseConfigPath;
                AddItemToProject(oldcfgfile); // needs to be in the project manifest, but not in source control

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
                        var oldXfrmContent = File.ReadAllText(oldxfrmpath);
                        var defaultXfrmContent = GetManifestResourceStreamContent(@"Transforms\" +
                                                             (oldxfrmname.Replace(".config", ".Default.config")));
                        var isUnmodifiedDefault = oldXfrmContent == defaultXfrmContent;
                        if (isUnmodifiedDefault)
                        {
                            await _io.Delete(oldxfrmpath);
                            replaceXfrm = true;
                        }
                        else
                        {
                            _logger.LogInfo("Moving " + xfrmname + " to " + xfrmpath);
                            await _io.Move(oldxfrmpath, xfrmFullPath);
                        }
                    }
                    if (replaceXfrm || (!File.Exists(xfrmpath) && !File.Exists(oldxfrmpath)))
                    {
                        _logger.LogInfo("Creating " + xfrmpath);
                        WriteFromManifest(@"Transforms\Web.{0}.config", cfgname, "Release", xfrmpath);
                        if (!replaceXfrm)
                        {
                            var item = AddItemToProject(xfrmpath);
                            item.AddMetadata("DependentUpon", newBaseConfigFile);
                        }
                        await _io.AddIfProjectIsSourceControlled(Project, xfrmFullPath);
                    }

                    // and update the proejct manifest reference to the file
                    var xfrmitem = Project.GetProjectRoot().Items.SingleOrDefault(item => item.Include.ToLower() == xfrmname.ToLower());
                    if (xfrmitem == null) AddItemToProject(xfrmpath);
                    else xfrmitem.Include = xfrmpath;
                    var metadata = xfrmitem.Metadata.SingleOrDefault(m => m.Name == "DependentUpon")
                                   ?? xfrmitem.AddMetadata("DependentUpon", newBaseConfigFile);
                    metadata.Value = newBaseConfigFile;
                }
            }

            // 4c. inject warning xml to base
            InjectBaseConfigWarningComment(baseConfigFullPath);

            return true;
        }

        private string GetManifestResourceStreamContent(string resourceName)
        {
            resourceName = typeof(FastKoalaPackage).Namespace + @".Resources." + resourceName.Replace("\\", ".");
            var assembly = typeof(BuildTimeTransformationsEnabler).Assembly;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                using (var sr = new StreamReader(stream))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        private void InjectBaseConfigWarningComment(string baseConfigFullPath)
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
            if (xml.DocumentElement.ChildNodes.Count > 0)
                xml.DocumentElement.InsertBefore(commentXmlNode, xml.DocumentElement.FirstChild);
            else xml.DocumentElement.AppendChild(commentXmlNode);
            _io.Checkout(baseConfigFullPath);
            xml.Save(baseConfigFullPath);
        }

        private ProjectItemElement AddItemToProject(string itemRelativePath)
        {
            var projectRoot = Project.GetProjectRoot();
            var itemGroup = projectRoot.ItemGroups.LastOrDefault(ig => string.IsNullOrEmpty(ig.Condition));
            if (itemGroup == null) itemGroup = projectRoot.AddItemGroup();
            return itemGroup.AddItem("None", itemRelativePath);
        }

        public EnvDTE.Project Project
        {
            get
            {
                return _dte.GetProjectByUniqueName(_projectUniqueName)
                    ?? _dte.GetProjectByName(_projectName);
            }
            set
            {
                _projectName = value.Name;
                _projectUniqueName = value.UniqueName;
            }
        }

        public bool CanEnableBuildTimeTransformations
        {
            get
            {
                return !HasBuildTimeTransformationsEnabled &&
                       !Project.IsType(ProjectTypes.WebSite) &&
                       !string.IsNullOrWhiteSpace(Project.FullName) &&
                       !Project.FullName.Contains("://") &&
                       File.Exists(Project.FullName);
            }
        }

        public bool HasBuildTimeTransformationsEnabled
        {
            get
            {
                if (Project.IsType(ProjectTypes.WebSite) ||
                    string.IsNullOrWhiteSpace(Project.FullName) ||
                    Project.FullName.Contains("://") ||
                    !File.Exists(Project.FullName))
                {
                    return false;
                }
                return ProjectProperties.BuildTimeTransformsEnabled;
            }
        }

        public ProjectProperties ProjectProperties { get; set; }
    }
}
