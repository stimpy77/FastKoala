using System;
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
    public class TargetsScriptInjector
    {
        private ILogger _logger;
        private string _projectName;
        private readonly ProjectProperties _projectProperties;
        private readonly IWin32Window _ownerWindow;
        private string _projectUniqueName;
        private DTE _dte;
        private ISccBasicFileSystem _io;

        public TargetsScriptInjector(EnvDTE.Project project, ISccBasicFileSystem io, ILogger logger, IWin32Window ownerWindow)
        {
            _dte = project.DTE;
            _projectUniqueName = project.UniqueName;
            _io = io;
            _projectName = project.Name;
            _projectProperties = new ProjectProperties(project);
            _logger = logger;
            _ownerWindow = ownerWindow;
        }
        
        public async Task<bool> AddProjectInclude(string containerDirectory, string scriptFile = null)
        {
            if (!Project.Saved)
            {
                var saveDialogResult = MessageBox.Show(_ownerWindow, "Save pending changes to solution?",
                    "Save pending changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (saveDialogResult == DialogResult.OK || saveDialogResult == DialogResult.Yes)
                    _dte.SaveAll();
            }
            if (!Project.Saved || string.IsNullOrEmpty(Project.FullName))
            {
                var saveDialogResult = MessageBox.Show(_ownerWindow, "Pending changes need to be saved. Please save the project before adding project imports, then retry.", "Save first",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Asterisk);
                if (saveDialogResult != DialogResult.Cancel) _dte.SaveAll();
                return false;
            }
            _logger.LogInfo("Begin adding project import file");
            if (string.IsNullOrWhiteSpace(containerDirectory))
                containerDirectory = Project.GetDirectory();

            if (string.IsNullOrWhiteSpace(scriptFile))
            {
                _logger.LogInfo("Prompting for file name");
                var dialog = new AddBuildScriptNamePrompt(containerDirectory, ".targets")
                {
                    HideInvokeBeforeAfter = true
                };
                var dialogResult = dialog.ShowDialog(VsEnvironment.OwnerWindow);
                if (dialogResult == DialogResult.Cancel) return false;
                scriptFile = dialog.FileName;
                _logger.LogInfo("File name chosen: " + scriptFile);
                dialogResult = MessageBox.Show(_ownerWindow, @"        !! IMPORTANT !!

You must not move, rename, or remove this file once it has been added to the project. By adding this file you are extending the project file itself. If you must change the filename or location, you must update the project XML directly where <Import> references it.",
                    "This addition is permanent", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (dialogResult == DialogResult.Cancel) return false;
            }
            var scriptFileName = scriptFile;
            if (!scriptFile.Contains(":") && !scriptFile.StartsWith("\\\\"))
                scriptFile = Path.Combine(containerDirectory, scriptFile);
            var scriptFileRelativePath = FileUtilities.GetRelativePath(Project.GetDirectory(), scriptFile);
            var scriptFileShortName = new FileInfo(scriptFile).Name;
            if (scriptFileShortName.Contains("."))
                scriptFileShortName = scriptFileShortName.Substring(0, scriptFileShortName.LastIndexOf("."));

            if (Project == null) return false;

            File.WriteAllText(scriptFile, @"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
    <Target Name=""" + scriptFileShortName + @"""><!-- consider adding attrib BeforeTargets=""Build"" or AfterTargets=""Build"" -->

        <!-- my tasks here -->
        <Message Importance=""high"" Text=""" + scriptFileShortName + @".targets output: ProjectGuid = $(ProjectGuid)"" />

    </Target>
</Project>");
            var projRoot = Project.GetProjectRoot();
            var import = projRoot.AddImport(scriptFileRelativePath);
            import.Condition = "Exists('" + scriptFileRelativePath + "')";
            Project = await Project.SaveProjectRoot();
            var addedItem = Project.ProjectItems.AddFromFile(scriptFile);
            addedItem.Properties.Item("ItemType").Value = "None";
            _logger.LogInfo("Project include file added to project: " + scriptFileName);
            Task.Run(() =>
            {
                System.Threading.Thread.Sleep(250);
                _dte.ExecuteCommand("File.OpenFile", "\"" + scriptFile + "\"");
            });
            return true;
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
