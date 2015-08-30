using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    public class NodeJSBuildScriptSupportInjector
    {
        private ILogger _logger;
        private string _projectName;
        private readonly ProjectProperties _projectProperties;
        private readonly IWin32Window _ownerWindow;
        private string _projectUniqueName;
        private DTE _dte;
        private ISccBasicFileSystem _io;

        public NodeJSBuildScriptSupportInjector(EnvDTE.Project project, ISccBasicFileSystem io, ILogger logger, IWin32Window ownerWindow)
        {
            _dte = project.DTE;
            _projectUniqueName = project.UniqueName;
            _io = io;
            _projectName = project.Name;
            _projectProperties = new ProjectProperties(project);
            _logger = logger;
            _ownerWindow = ownerWindow;
        }
        
        public async Task<bool> AddNodeJSScript(string containerDirectory, 
            string scriptFile = null, bool? invokeAfter = null)
        {
            if (!(await EnsureProjectHasNodeJSEnabled())) return false;
            _logger.LogInfo("Begin adding NodeJS script");
            invokeAfter = invokeAfter ?? true;
            if (string.IsNullOrWhiteSpace(containerDirectory))
                containerDirectory = Project.GetDirectory();

            if (string.IsNullOrWhiteSpace(scriptFile))
            {
                _logger.LogInfo("Prompting for file name");
                var dialog = new AddBuildScriptNamePrompt(containerDirectory, ".js");
                dialog.InvokeAfter = false;
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

            File.WriteAllText(scriptFile, @"(function() {

  // MSBuild project properties are exposed at build-time. Example:
  console.log(""msbuild.properties['ProjectGuid'] = \"""" + msbuild.properties['ProjectGuid'] + ""\"""");

})();");
            var addedItem = Project.ProjectItems.AddFromFile(scriptFile);
            addedItem.Properties.Item("ItemType").Value
                = invokeAfter.Value ? "InvokeAfter" : "InvokeBefore";
            _logger.LogInfo("NodeJS script added to project: " + scriptFileName);
            Task.Run(() =>
            {
                System.Threading.Thread.Sleep(250);
                _dte.ExecuteCommand("File.OpenFile", "\"" + scriptFile + "\"");
            });
            return true;
        }

        private async Task<bool> EnsureProjectHasNodeJSEnabled()
        {
            if (!_projectProperties.NodeJSBuildEnabled)
                return await InjectNodeJSScriptSupport() != null;
            return true;
        }

        private async Task<EnvDTE.Project> InjectNodeJSScriptSupport()
        {
            _logger.LogInfo("Injecting NodeJS rich execution support to project");
            if (!Project.Saved || !Project.DTE.Solution.Saved || 
                string.IsNullOrEmpty(Project.FullName) || string.IsNullOrEmpty(Project.DTE.Solution.FullName))
            {
                _logger.LogInfo("Project or solution is not saved. Aborting.");
                MessageBox.Show(_ownerWindow,
                    "Please save the project and solution before adding NodeJS build scripts.",
                    "Aborted", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return null;
            }

            var projRoot = Project.GetProjectRoot();
            if (projRoot.UsingTasks.Any(ut => ut.TaskName == "InvokeNodeJS"))
                return Project;
            var usingTask = projRoot.AddUsingTask("InvokeNodeJS",
                @"$(MSBuildToolsPath)\Microsoft.Build.Tasks.v$(MSBuildToolsVersion).dll", null);
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
            var psScriptsBefore = projRoot.AddTarget("NodeJSScriptsBefore");
            psScriptsBefore.BeforeTargets = "Build";
            var invokeBeforeExec = psScriptsBefore.AddTask("InvokeNodeJS");
            invokeBeforeExec.SetParameter("ScriptFile", "%(InvokeBefore.Identity)");
            invokeBeforeExec.Condition = "'@(InvokeBefore)' != ''";
            var psScriptsAfter = projRoot.AddTarget("NodeJSScriptsAfter");
            psScriptsAfter.AfterTargets = "Build";
            var invokeAfterExec = psScriptsAfter.AddTask("InvokeNodeJS");
            invokeAfterExec.SetParameter("ScriptFile", "%(InvokeAfter.Identity)");
            invokeAfterExec.Condition = "'@(InvokeAfter)' != ''";

            _projectProperties.NodeJSBuildEnabled = true;
            var projectRootPath = Project.FullName;
            Project = await Project.SaveProjectRoot();
            
            VsEnvironment.Dte.UnloadProject(Project);

            var projXml = File.ReadAllText(projectRootPath);
            // ReSharper disable StringIndexOfIsCultureSpecific.1
            // ReSharper disable StringIndexOfIsCultureSpecific.2
            var injectLocation = projXml.IndexOf("</UsingTask",
                projXml.IndexOf("<UsingTask TaskName=\"InvokeNodeJS\""));
            projXml = projXml.Substring(0, injectLocation)
                      + @"
        <Task>
          <Reference Include=""Microsoft.Build"" />
          <Reference Include=""System.Xml"" />
          <Using Namespace=""Microsoft.Build.Evaluation"" />
          <Using Namespace=""System.Text"" />
          <Using Namespace=""System.Text.RegularExpressions"" />
          <Using Namespace=""System.Threading"" />
          <Using Namespace=""System.IO"" />
          <Using Namespace=""System.Diagnostics"" />
          <Code Type=""Fragment"" Language=""cs""><![CDATA[

        if (!ScriptFile.ToLower().EndsWith("".js"")) return true;
        var runSuccess = true;
		BuildEngine.LogMessageEvent(new BuildMessageEventArgs(""Executing as NodeJS REPL: "" + ScriptFile, """", """", MessageImportance.High));
        Project project = ProjectCollection.GlobalProjectCollection.GetLoadedProjects(BuildEngine.ProjectFileOfTaskNode).FirstOrDefault()
            ?? new Project(BuildEngine.ProjectFileOfTaskNode);
        if (!ScriptFile.Contains("":"") && !ScriptFile.StartsWith(""\\\\""))
        ScriptFile = project.DirectoryPath + ""\\"" + ScriptFile;
        var vars = new System.Text.StringBuilder();
        vars.AppendLine(""var msbuild = { properties: {} };"");
        foreach (ProjectProperty evaluatedProperty in project.AllEvaluatedProperties)
        {
            if (!evaluatedProperty.IsEnvironmentProperty)
            {
                var name = evaluatedProperty.Name;
                var value = evaluatedProperty.EvaluatedValue;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        if (value.ToLower() == ""true"" || value.ToLower() == ""false"") {
                            vars.AppendLine(""msbuild.properties[\"""" + name + ""\""] = "" + value + "";"");
                        } else {
                            vars.AppendLine(""msbuild.properties[\"""" + name + ""\""] = \"""" + value.Replace(""\\"", ""\\\\"").Replace(""\"""", ""\\\"""").Replace(""\r"", """").Replace(""\n"", ""\\n"") + ""\"";"");
                        }
                    }
                }
            }
        }
		var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = ""node.exe"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				Arguments = ""-i""
			}
		};

		var loadingScript = false;
		process.OutputDataReceived += (sender, args) =>
		{
			if (!string.IsNullOrWhiteSpace(args.Data) && args.Data != ""> "" && args.Data != ""undefined"" && loadingScript)
			{
				var data = args.Data;
				while (data.StartsWith(""> "") || data.StartsWith(""... "")) {
                    if (data.StartsWith(""> "")) data = data.Substring(2);
                    if (data.StartsWith(""... "")) data = data.Substring(4);
                }
                if (data == ""undefined"") return;
				if (Regex.IsMatch(data, ""\\w*Error: "")) {
					BuildEngine.LogErrorEvent(new BuildErrorEventArgs("""", """", ScriptFile, 0, 0, 0, 0, data, """", """", DateTime.Now));
                    process.EnableRaisingEvents = false; // block stack trace; sorry, no support for JS stack trace when using REPL
                    runSuccess = false;
				} else {
                    if (runSuccess) { // block stack trace; sorry, no support for JS stack trace when using REPL

					    BuildEngine.LogMessageEvent(new BuildMessageEventArgs(data, """", """", MessageImportance.High));
                    }
				}
			}
		};
		process.ErrorDataReceived += (sender, args) =>
		{
			var data = args.Data;
            if (string.IsNullOrWhiteSpace(data)) return;
			BuildEngine.LogErrorEvent(new BuildErrorEventArgs("""", """", ScriptFile, 0, 0, 0, 0, data, """", """", DateTime.Now));
            runSuccess = false;
		};
		
		process.Start();
		process.EnableRaisingEvents = true;
		process.BeginOutputReadLine();		
		process.StandardInput.WriteLine(vars.ToString());
		Thread.Sleep(250);
		loadingScript = true;
		process.StandardInput.WriteLine("".load "" + ScriptFile.Replace(""\\"", ""\\\\""));
		process.StandardInput.WriteLine(""process.exit()"");
		process.WaitForExit();
		
        return runSuccess;
    ]]></Code>
        </Task>"
                      + projXml.Substring(injectLocation);
            if (await _io.ItemIsUnderSourceControl(projectRootPath)) await _io.Checkout(projectRootPath);
            File.WriteAllText(projectRootPath, projXml);
            _logger.LogInfo("Project file is now updated.");
            VsEnvironment.Dte.ReloadJustUnloadedProject();
            Project = VsEnvironment.Dte.GetProjectByFullName(projectRootPath);
            return Project;
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
