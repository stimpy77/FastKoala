﻿using System;
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

        public static async Task<ISccBasicFileSystem> GetFileSystemManipulatorForEnvironmentAsync(Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = project.DTE;
            //if (!project.IsSourceControlled() && !dte.Solution.IsSourceControlled())
            //{
            //    return new NonSccBasicFileSystem();
            //}
            var detectedSccSystem = await DetectSccSystemAsync(project);
            ISccBasicFileSystem result;
            switch (detectedSccSystem)
            {
                case "tfs":
                    result = new TfsExeWrapper(project.GetDirectory(), await dte.GetLoggerAsync());
                    break;
                case "git":
                    result = new GitExeWrapper(project.GetDirectory(), await dte.GetLoggerAsync());
                    break;
                case "hg": // not yet implemented
                    //result = null;
                    result = new NonSccBasicFileSystem();
                    break;
                case "svn": // not yet implemented
                    //result = null;
                    result = new NonSccBasicFileSystem();
                    break;
                case null:
                    result = new NonSccBasicFileSystem();
                    break;
                default: // not implemented
                    //result = null;
                    result = new NonSccBasicFileSystem();
                    break;
            }
            return result;
        }

        private static async Task<string> DetectSccSystemAsync(Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            // Did I mention? VS SDK API for source control detection and support is really, really horrible.

            var tfs = new TfsExeWrapper(project.GetDirectory(), await VsEnvironment.Dte.GetLoggerAsync());
            var projectFilePath = project.FullName;
            if (string.IsNullOrWhiteSpace(projectFilePath)) return null;
            if (!projectFilePath.Contains("://") && File.Exists(projectFilePath))
            {
                try
                {
                    if (await tfs.ItemIsUnderSourceControl(projectFilePath))
                    {
                        return "tfs";
                    }
                }
                catch
                {
                    //var logger = VsEnvironment.Dte.GetLogger();
                    //logger.LogError(
                    System.Diagnostics.Debug.WriteLine(
                        "Something went wrong when checking to see if this file is under source control: "
                                    + projectFilePath, "Error");
                    var fileInfo = new FileInfo(projectFilePath);
                    var sb = new StringBuilder();
                    sb.AppendLine("Exists: " + fileInfo.Exists);
                    sb.AppendLine("IsReadOnly: " + fileInfo.IsReadOnly);
                    sb.AppendLine("Length: " + fileInfo.Length);
                    sb.AppendLine("Name: " + fileInfo.Name);
                    sb.AppendLine("Attributes: " + fileInfo.Attributes.ToString());
                    //logger.LogError("File details ...\r\n" + sb.ToString());
                    System.Diagnostics.Debug.WriteLine(sb.ToString());
                    //throw; //return "tfs"; // whatever
                }
            }
            var sccdir = project.DTE.Solution.GetDirectory();
            if (string.IsNullOrEmpty(sccdir)) sccdir = project.GetDirectory();
            return DetectSccSystem(sccdir);
        }

        private static string DetectSccSystem(string directory)
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
