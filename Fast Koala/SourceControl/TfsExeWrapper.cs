using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TeamFoundation.VersionControl;
 
using Wijits.FastKoala.Logging;
using Wijits.FastKoala.Utilities;

namespace Wijits.FastKoala.SourceControl
{
    public class TfsExeWrapper : ISccBasicFileSystem
    {
        private string _tfexe;
        private string _workingDirectory;
        private ILogger _logger;

        public TfsExeWrapper(string workingDirectory, ILogger logger)
        {
            var vsPath = Process.GetCurrentProcess().Modules[0].FileName;
            _tfexe = Path.Combine(Directory.GetParent(vsPath).FullName, "tf.exe");
            _workingDirectory = workingDirectory;
            _logger = logger;
        }

        public async Task<bool> ItemIsUnderSourceControl(string filename)
        {
            var vp = VsEnvironment.GetService<IVersionControlProvider>();
            if (vp != null)
            {
                try
                {
                    bool isBound;
                    vp.IsFileBoundToSCC(filename, out isBound);
                    return isBound;
                }
                catch
                {
                }
            }
            var statusOutput = TaskResult = await TfExec("status \"" + filename + "\"");
            if (statusOutput.StartsWith("ERROR")) return false;
            return true;
        }

        /// <summary>
        /// Get the VersionControlExt extensibility object.
        /// </summary>
        public static object GetVersionControlExt(IServiceProvider serviceProvider)
        {
            if (serviceProvider != null)
            {
                DTE2 dte = serviceProvider.GetService(typeof (SDTE)) as DTE2;
                if (dte != null)
                {

                    object ret = dte.GetObject("Microsoft.VisualStudio.TeamFoundation.VersionControl.VersionControlExt");
                    var t = ret.GetType();
                    var a = t.Assembly;
                    var f = a.Location;
                    return ret;
                }
            }

            return null;
        }

        public async Task<bool> AddIfProjectIsSourceControlled(EnvDTE.Project project, string filename)
        {
            if (!(await ItemIsUnderSourceControl(project.FullName))) return false;
            TaskResult = await TfExec("add \"" + filename + "\"");
            return true;
        }

        public async Task Move(string source, string destination)
        {
            TaskResult = await TfExec("move \"" + source + "\" \"" + destination + "\"");
        }

        public async Task Delete(string filename)
        {
            TaskResult = await TfExec("delete \"" + filename + "\"");
        }

        public async Task Checkout(string filename)
        {
            TaskResult = await TfExec("checkout \"" + filename + "\"");
        }

        public async Task UndoMove(string filename, string originalPath)
        {
            var contents = (new FileInfo(filename)).Length < 1048576 ? File.ReadAllBytes(filename) : null;
            TaskResult = await TfExec("undo \"" + filename + "\"");
            if (!string.IsNullOrEmpty(originalPath) && File.Exists(filename) && !File.Exists(originalPath))
            {
                // uhps. we undid an edit and not a move. restore and move ...
                if (contents != null)
                {
                    await Checkout(filename);
                    File.WriteAllBytes(filename, contents);
                }
                TaskResult = await TfExec("move \"" + filename + "\" \"" + originalPath + "\"");
            }
        }

        public string TaskResult { get; private set; }

        private readonly object sblock = new object();

        private async Task<string> TfExec(string args)
        {
            var sb = new StringBuilder();
            var startinfo = new ProcessStartInfo(_tfexe, args)
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
            if (_logger != null) _logger.LogInfo("» tf " + args);
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
