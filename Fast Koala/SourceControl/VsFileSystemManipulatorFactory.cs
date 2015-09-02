using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using Wijits.FastKoala.Utilities;

namespace Wijits.FastKoala.SourceControl
{
    public class VsFileSystemManipulatorFactory
    {
        // FYI: VS SDK API for source control detection and support is really, really horrible.

        public static async Task<ISccBasicFileSystem> GetFileSystemManipulatorForEnvironment(Project project)
        {
            var dte = project.DTE;
            //if (!project.IsSourceControlled() && !dte.Solution.IsSourceControlled())
            //{
            //    return new NonSccBasicFileSystem();
            //}
            var detectedSccSystem = await DetectSccSystem(project);
            ISccBasicFileSystem result;
            switch (detectedSccSystem)
            {
                case "tfs":
                    result = new TfsExeWrapper(project.GetDirectory(), dte.GetLogger());
                    break;
                case "git":
                    result = new GitExeWrapper(project.GetDirectory(), dte.GetLogger());
                    break;
                case "hg": // not yet implemented
                    result = null;
                    break;
                case "svn": // not yet implemented
                    result = null;
                    break;
                case null:
                    result = new NonSccBasicFileSystem();
                    break;
                default: // not implemented
                    result = null;
                    break;
            }
            return result;
        }

        private static async Task<string> DetectSccSystem(Project project)
        {
            // Did I mention? VS SDK API for source control detection and support is really, really horrible.

            var tfs = new TfsExeWrapper(project.GetDirectory(), VsEnvironment.Dte.GetLogger());
            var projectFilePath = project.FullName;
            if (string.IsNullOrWhiteSpace(projectFilePath)) return null;
            try
            {
                if (File.Exists(projectFilePath) && await tfs.ItemIsUnderSourceControl(projectFilePath))
                {
                    return "tfs";
                }
            }
            catch
            {
                var logger = VsEnvironment.Dte.GetLogger();
                logger.LogError("Something went wrong when checking to see if this file is under source control: " 
                    + projectFilePath);
                var fileInfo = new FileInfo(projectFilePath);
                var sb = new StringBuilder();
                sb.AppendLine("Exists: " + fileInfo.Exists);
                sb.AppendLine("IsReadOnly: " + fileInfo.IsReadOnly);
                sb.AppendLine("Length: " + fileInfo.Length);
                sb.AppendLine("Name: " + fileInfo.Name);
                sb.AppendLine("Attributes: " + fileInfo.Attributes.ToString());
                logger.LogError("File details ...\r\n" + sb.ToString());
                throw; //return "tfs"; // whatever
            }
            var sccdir = project.DTE.Solution.GetDirectory();
            if (string.IsNullOrEmpty(sccdir)) sccdir = project.GetDirectory();
            return await DetectSccSystem(sccdir);
        }

        private static async Task<string> DetectSccSystem(string directory)
        {

            var gitDirExists = Directory.Exists(Path.Combine(directory, ".git"));
            var hgDirExists = Directory.Exists(Path.Combine(directory, ".hg"));
            var svnDirExists = Directory.Exists(Path.Combine(directory, ".svn"));
            if (gitDirExists) return "git";
            if (hgDirExists) return "hg";
            if (svnDirExists) return "svn";
            return null;
        }
    }
}
