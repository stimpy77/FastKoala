using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TeamFoundation.VersionControl;
using Wijits.FastKoala.Logging;
using Wijits.FastKoala.Utilities;
using Process = System.Diagnostics.Process;

namespace Wijits.FastKoala.SourceControl
{
    public class GitExeWrapper : ISccBasicFileSystem
    {
        private string _gitexe;
        private string _workingDirectory;
        private ILogger _logger;

        public GitExeWrapper(string workingDirectory, ILogger logger)
        {
            _gitexe = FileUtilities.GetFullPath("git.exe") ?? "git";
            _workingDirectory = workingDirectory;
            _logger = logger;
        }

        public async Task<bool> ItemIsUnderSourceControl(string filename)
        {
            try
            {
                var status = await this.GitStatus(filename);
                if (status.StartsWith("ERROR") || status.StartsWith("??"))
                    return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> AddIfProjectIsSourceControlled(EnvDTE.Project project, string filename)
        {
            if (!(await ItemIsUnderSourceControl(project.FullName))) return false;
            TaskResult = await GitExec("add \"" + filename + "\"");
            return true;
        }

        public async Task Move(string source, string destination)
        {
            TaskResult = await GitExec("mv \"" + source + "\" \"" + destination + "\"");
        }

        public async Task Delete(string filename)
        {
            TaskResult = await GitExec("rm \"" + filename + "\"");
        }

        public async Task Checkout(string filename)
        {
            // Checkout has a different meaning in git; if a branch has already been cloned
            // and the branch "checked out" then there's no need to check out a file. Git
            // does not lock files until they are individually checked out like TFS does.
            await Task.Run(() => { });
        }

        public async Task UndoMove(string filename, string originalPath)
        {
            // git has no concept of undoing a move unless the entire branch is reset/restored
            // so we must just move back manually
            if (string.IsNullOrEmpty(originalPath)) 
                throw new InvalidOperationException("OriginalPath not provided");
            await GitExec("mv \"" + filename + "\" \"" + originalPath + "\"");
        }

        public string TaskResult { get; private set; }

        public async Task<string> GitStatus(string filename)
        {
            TaskResult = (await GitExec("status \"" + filename + "\" -z")).Trim();
            var tr = Regex.Replace(TaskResult, @"\s+", " ");
            if (string.IsNullOrWhiteSpace(tr))
            {
                var tr2 = await GitExec("status \"" + filename + "\"");
                if (string.IsNullOrWhiteSpace(tr2))
                    return "ERROR " + tr2;
            }
            return tr.Split(' ')[0];
        }

        private readonly object sblock = new object();

        private async Task<string> GitExec(string args)
        {
            return await Exec(_gitexe, args);
        }

        private async Task<string> Exec(string exe, string args)
        {
            var sb = new StringBuilder();
            var startinfo = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _workingDirectory
            };
            var ret = new Process
            {
                StartInfo = startinfo,
                EnableRaisingEvents = true
            };
            ret.OutputDataReceived += (sender, eventArgs) =>
            {
                lock (sblock)
                {
                    var data = eventArgs.Data;
                    if (string.IsNullOrEmpty(data)) return;
                    sb.AppendLine(data);
                    Debug.Write(data);
                }
            };
            ret.ErrorDataReceived += (sender, eventArgs) =>
            {
                lock (sblock)
                {
                    var data = eventArgs.Data;
                    if (string.IsNullOrWhiteSpace(data)) return;
                    sb.AppendLine("ERROR: " + data);
                    Debug.Write(data);
                }
            };
            Debug.Assert(ret != null, "ret != null");
            if (_logger != null) _logger.LogInfo("» git " + args);
            ret.Start();
            ret.BeginOutputReadLine();
            ret.BeginErrorReadLine();
            await ret.WaitForExitAsync();
            lock (sblock)
            {
                return sb.ToString();
            }

        }
    }
}
