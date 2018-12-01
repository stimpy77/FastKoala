using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell.Interop;
 
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
            if (!File.Exists(_tfexe))
                _tfexe = Path.Combine(Directory.GetParent(vsPath).FullName, @"CommonExtensions\Microsoft\TeamFoundation\Team Explorer\tf.exe");
            if (!File.Exists(_tfexe)) _tfexe = null;
            _workingDirectory = workingDirectory;
            _logger = logger;
        }

        public async Task<bool> ItemIsUnderSourceControl(string filename)
        {
            try
            {
                var statusOutput = TaskResult = await TfExec("info \"" + filename + "\"");
                if (statusOutput.StartsWith("No items match")) return false;
                return true;
            }
            catch (FileNotFoundException e /* ?? .. trying to find a reported error #35 */)
            {
                if (_tfexe != null) // TFS wouldn't have worked anyway
                {
                    _logger.LogWarn(e.ToString());
                }
                return false;
            }

        }

        public async Task<bool> Add(string filename)
        {
            this._logger.LogInfo("(The next command may take a minute or two.)");
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
            this._logger.LogInfo("(The next command may take a minute or two.)");
            TaskResult = await TfExec("checkout \"" + filename + "\"");
        }

        public async Task AddItemToIgnoreList(string relativeIgnorePattern, string precedingComment = null)
        {
            var tfIgnoreFile = Path.Combine(_workingDirectory, ".tfignore");
            if (!File.Exists(tfIgnoreFile))
            {
                File.WriteAllText(tfIgnoreFile, "");
                await TfExec("add \"" + tfIgnoreFile + "\"");
            }
            using (var sw = File.AppendText(tfIgnoreFile))
            {
                await Checkout(tfIgnoreFile);
                if ((new FileInfo(tfIgnoreFile)).Length > 0)
                {
                    await sw.WriteLineAsync();
                }
                if (!string.IsNullOrEmpty(precedingComment))
                {
                    var comments = precedingComment.Replace("\r", "").Split('\n');
                    foreach (var comment in comments)
                    {
                        await sw.WriteLineAsync("# " + comment);
                    }
                }
                await sw.WriteLineAsync(relativeIgnorePattern);
            }
        }

        public string TaskResult { get; private set; }

        private readonly object sblock = new object();

        private async Task<string> TfExec(string args)
        {
            if (_tfexe == null) throw new InvalidOperationException("Tf.exe is not where it was expected to be.");
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
