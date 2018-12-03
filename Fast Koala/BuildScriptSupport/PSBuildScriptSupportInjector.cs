﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using EnvDTE;
using Wijits.FastKoala.BuildScriptSupport;
using Wijits.FastKoala.Logging;
using Wijits.FastKoala.SourceControl;
using Wijits.FastKoala.Utilities;
// ReSharper disable LocalizableElement
// ReSharper disable SimplifyLinqExpression

namespace Wijits.FastKoala.BuildScriptInjections
{
    public class PSBuildScriptSupportInjector
    {
        private ILogger _logger;
        private string _projectName;
        private readonly ProjectProperties _projectProperties;
        private readonly IWin32Window _ownerWindow;
        private string _projectUniqueName;
        private DTE _dte;
        private ISccBasicFileSystem _io;

        public PSBuildScriptSupportInjector(EnvDTE.Project project, ISccBasicFileSystem io, ILogger logger, IWin32Window ownerWindow)
        {
            _dte = project.DTE;
            _projectUniqueName = project.UniqueName;
            _io = io;
            _projectName = project.Name;
            _projectProperties = new ProjectProperties(project);
            _logger = logger;
            _ownerWindow = ownerWindow;
        }
        
        public async Task<bool> AddPowerShellScript(string containerDirectory, 
            string scriptFile = null, bool? invokeAfter = null)
        {
            if (!(await EnsureProjectHasPowerShellEnabled())) return false;
            _logger.LogInfo("Begin adding PowerShell script");
            invokeAfter = invokeAfter ?? true;
            if (string.IsNullOrWhiteSpace(containerDirectory))
                containerDirectory = Project.GetDirectory();

            if (string.IsNullOrWhiteSpace(scriptFile))
            {
                _logger.LogInfo("Prompting for file name");
                var dialog = new AddBuildScriptNamePrompt(containerDirectory, ".ps1");
                var dialogResult = dialog.ShowDialog(VsEnvironment.OwnerWindow);
                if (dialogResult == DialogResult.Cancel) return false;
                scriptFile = dialog.FileName;
                _logger.LogInfo("File name chosen: " + scriptFile);
                invokeAfter = dialog.InvokeAfter;
                _logger.LogInfo("Selected invoke sequence: Invoke" + (dialog.InvokeAfter ? "After" : "Before") + " (..the build)");
            }
            var scriptFileName = scriptFile;
            if (!scriptFile.Contains(":") && !scriptFile.StartsWith("\\\\"))
                scriptFile = Path.Combine(containerDirectory, scriptFile);

            if (Project == null) return false;

            File.WriteAllText(scriptFile, @"# MSBuild project properties are exposed at build-time. Example:
Write-Output ""`$MSBuildProjectDirectory = `""$MSBuildProjectDirectory`""""");
            var addedItem = Project.ProjectItems.AddFromFile(scriptFile);
            addedItem.Properties.Item("ItemType").Value
                = invokeAfter.Value ? "InvokeAfter" : "InvokeBefore";
            _logger.LogInfo("PowerShell script added to project: " + scriptFileName);
            await Task.Run(() =>
            {
                System.Threading.Thread.Sleep(250);
                _dte.ExecuteCommand("File.OpenFile", "\"" + scriptFile + "\"");
            });
            return true;
        }

        private async Task<bool> EnsureProjectHasPowerShellEnabled()
        {
            if (!_projectProperties.PowerShellBuildEnabled)
                return await InjectPowerShellScriptSupport() != null;
            return true;
        }

        private async Task<EnvDTE.Project> InjectPowerShellScriptSupport()
        {
            _logger.LogInfo("Injecting PowerShell rich execution support to project");
            if (!Project.Saved || !Project.DTE.Solution.Saved || 
                string.IsNullOrEmpty(Project.FullName) || string.IsNullOrEmpty(Project.DTE.Solution.FullName))
            {
                var saveDialogResult = MessageBox.Show(_ownerWindow, "Save pending changes to solution?",
                    "Save pending changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (saveDialogResult == DialogResult.OK || saveDialogResult == DialogResult.Yes)
                    _dte.SaveAll();
            }
            if (!Project.Saved || !Project.DTE.Solution.Saved ||
                string.IsNullOrEmpty(Project.FullName) || string.IsNullOrEmpty(Project.DTE.Solution.FullName))
            {
                var saveDialogResult = MessageBox.Show(_ownerWindow,
                    "Pending changes need to be saved. Please save the project and solution before adding PowerShell build scripts, then retry.",
                    "Aborted", MessageBoxButtons.OKCancel, MessageBoxIcon.Asterisk);
                if (saveDialogResult != DialogResult.Cancel) _dte.SaveAll();
                _logger.LogInfo("Project or solution is not saved. Aborting.");
                return null;
            }

            var projRoot = Project.GetProjectRoot();
            if (projRoot.UsingTasks.Any(ut => ut.TaskName == "InvokePowerShell"))
                return Project;
            var inlineTaskVersionDep_group = projRoot.AddPropertyGroup();
            var inlineTaskVersionDep_v12 = inlineTaskVersionDep_group.AddProperty("InlineTaskVersionDep", "v$(MSBuildToolsVersion)");
            inlineTaskVersionDep_v12.Condition = "'$(MSBuildToolsVersion)' == '12'";
            var inlineTaskVersionDep_v14 = inlineTaskVersionDep_group.AddProperty("InlineTaskVersionDep", "Core");
            inlineTaskVersionDep_v14.Condition = "'$(MSBuildToolsVersion)' == '14'";
            var inlineTaskVersionDep_v15 = inlineTaskVersionDep_group.AddProperty("InlineTaskVersionDep", "Core");
            inlineTaskVersionDep_v15.Condition = "'$(MSBuildToolsVersion)' == '15'";
            var usingTask = projRoot.AddUsingTask("InvokePowerShell",
                @"$(MSBuildToolsPath)\Microsoft.Build.Tasks.$(InlineTaskVersionDep).dll", null);
            usingTask.TaskFactory = "CodeTaskFactory";
            var utParameters = usingTask.AddParameterGroup();

            var utParamsScriptFile = utParameters.AddParameter("ScriptFile");
            utParamsScriptFile.ParameterType = "System.String";
            utParamsScriptFile.Required = "true";
            if (!projRoot.PropertyGroups.Any(pg => pg.Label == "InvokeBeforeAfter"))
            {
                var invokeBeforeAfterGroup = projRoot.AddItemGroup();
                invokeBeforeAfterGroup.Label = "InvokeBeforeAfter";
                var invokeBefore = invokeBeforeAfterGroup.AddItem("AvailableItemName", "InvokeBefore");
                invokeBefore.AddMetadata("Visible", "false");
                var invokeAfter = invokeBeforeAfterGroup.AddItem("AvailableItemName", "InvokeAfter");
                invokeAfter.AddMetadata("Visible", "false");
            }
            var psScriptsBefore = projRoot.AddTarget("PSScriptsBefore");
            psScriptsBefore.BeforeTargets = "Build";
            var invokeBeforeExec = psScriptsBefore.AddTask("InvokePowerShell");
            invokeBeforeExec.SetParameter("ScriptFile", "%(InvokeBefore.Identity)");
            invokeBeforeExec.Condition = "'@(InvokeBefore)' != ''";
            var psScriptsAfter = projRoot.AddTarget("PSScriptsAfter");
            psScriptsAfter.AfterTargets = "Build";
            var invokeAfterExec = psScriptsAfter.AddTask("InvokePowerShell");
            invokeAfterExec.SetParameter("ScriptFile", "%(InvokeAfter.Identity)");
            invokeAfterExec.Condition = "'@(InvokeAfter)' != ''";

            _projectProperties.PowerShellBuildEnabled = true;
            var projectRootPath = Project.FullName;
            Project = await Project.SaveProjectRoot();
            
            VsEnvironment.Dte.UnloadProject(Project);

            var projXml = File.ReadAllText(projectRootPath);
            // ReSharper disable StringIndexOfIsCultureSpecific.1
            // ReSharper disable StringIndexOfIsCultureSpecific.2
            var injectLocation = projXml.IndexOf("</UsingTask",
                projXml.IndexOf("<UsingTask TaskName=\"InvokePowerShell\""));
            projXml = projXml.Substring(0, injectLocation)
                      + @"
        <Task>
        <Reference Include=""Microsoft.Build"" />
        <Reference Include=""System.Management.Automation"" />
        <Reference Include=""System.Xml"" />
        <Using Namespace=""System.Management.Automation"" />
        <Using Namespace=""System.Management.Automation.Runspaces"" />
        <Using Namespace=""Microsoft.Build.Evaluation"" />
        <Code Type=""Fragment"" Language=""cs""><![CDATA[
        if (!ScriptFile.ToLower().EndsWith("".ps1"")) return true;
        var envdir = Environment.CurrentDirectory;
        BuildEngine.LogMessageEvent(new BuildMessageEventArgs(""Executing with PowerShell: "" + ScriptFile, """", """", MessageImportance.High));
        Project project = ProjectCollection.GlobalProjectCollection.GetLoadedProjects(BuildEngine.ProjectFileOfTaskNode).FirstOrDefault()
            ?? new Project(BuildEngine.ProjectFileOfTaskNode);
        if (!ScriptFile.Contains("":"") && !ScriptFile.StartsWith(""\\\\""))
            ScriptFile = project.DirectoryPath + ""\\"" + ScriptFile;
        var pwd = Directory.GetParent(ScriptFile).FullName;
        var runspaceConfig = RunspaceConfiguration.Create();
        using (Runspace runspace = RunspaceFactory.CreateRunspace(runspaceConfig)) 
        { 
            runspace.Open();
            var vars = new System.Text.StringBuilder();
            foreach (ProjectProperty evaluatedProperty in project.AllEvaluatedProperties)
            {
                var name = evaluatedProperty.Name;
                var value = evaluatedProperty.EvaluatedValue;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                {
                    if (value.ToLower() == ""true"" || value.ToLower() == ""false"") {
                        vars.AppendLine(""$"" + name + "" = $"" + value);
                    } else {
                        vars.AppendLine(""$"" + name + "" = @\""\r\n"" + value.Replace(""\"""", ""\""\"""") + ""\r\n\""@"");
                    }
                }
            }
            using (RunspaceInvoke scriptInvoker = new RunspaceInvoke(runspace)) 
            { 
                using (Pipeline pipeline = runspace.CreatePipeline()) {
                    scriptInvoker.Invoke(vars.ToString()); 
                    scriptInvoker.Invoke(""cd \"""" + pwd + ""\"""");
                    var fileName = ScriptFile.Substring(project.DirectoryPath.Length + 1);
                    pipeline.Commands.AddScript(""& \"""" + ScriptFile + ""\""""); 
                    try {
                        var results = pipeline.Invoke();
                        var sbout = new StringBuilder();
                        foreach (var result in results)
                        {
                            sbout.AppendLine(result.ToString());
                        }
                        BuildEngine.LogMessageEvent(new BuildMessageEventArgs(sbout.ToString(), """", """", MessageImportance.High));
                    } catch (Exception e) {
                        BuildEngine.LogErrorEvent(new BuildErrorEventArgs("""", """", ScriptFile, 0, 0, 0, 0, e.ToString(), """", """", DateTime.Now));
                        throw;
                    }
                }
            } 
        }
        Environment.CurrentDirectory = envdir;
        return true;
    ]]></Code>
        </Task>"
                      + projXml.Substring(injectLocation);

            // Due to a glitch (?) in VS's flavor of MSBuild, we have to move <UsingTask>'s to before various <Import>s.
            if (projXml.Contains("<Import"))
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(projXml.Replace(" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"", ""));
                var task = xmlDoc.SelectSingleNode("/Project/UsingTask[@TaskName=\"InvokePowerShell\"]");
                xmlDoc.SelectSingleNode("Project").InsertBefore(task, xmlDoc.SelectNodes("/Project/Import")[0]);
                projXml = xmlDoc.OuterXml;
                projXml.Insert(projXml.IndexOf(">", projXml.IndexOf("<Project")), " xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"");
            }

            if (await _io.ItemIsUnderSourceControl(projectRootPath)) await _io.Checkout(projectRootPath);
            File.WriteAllText(projectRootPath, projXml);
            _logger.LogInfo("Project file is now updated.");
            VsEnvironment.Dte.ReloadJustUnloadedProject();
            return VsEnvironment.Dte.GetProjectByFullName(projectRootPath);
        }

        public Project Project
        {
            get
            {
                return _dte.GetProjectByUniqueName(_projectUniqueName)
                       ?? _dte.GetProjectByName(_projectName);
            }
            set
            {
                if (value == null) return;
                _projectName = value.Name;
                _projectUniqueName = value.UniqueName;
            }
        }
    }
}
