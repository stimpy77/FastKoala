using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;

namespace Wijits.FastKoala.Logging
{
    public class DteLogger : ILogger
    {
        private DTE _dte;
        private string _outputPrefix;

        public DteLogger(DTE dte, string outputPrefix = null)
        {
            this._dte = dte;
            this._outputPrefix = outputPrefix ?? (typeof(DteLogger).Assembly.GetName().Name + ": ");
        }

        private OutputWindowPane OutputWindowPane
        {
            get
            {
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                var window = _dte.Windows.Item(Constants.vsWindowKindOutput);
                var outputWindow = (OutputWindow)window.Object;
                var panes = outputWindow.OutputWindowPanes;
                return panes.Cast<OutputWindowPane>().SingleOrDefault(pane => pane.Name == "Output") 
                    ?? panes.Add("Output");
            }
        }

        public void LogInfo(string infoMessage)
        {
            WriteLine(infoMessage);
        }

        public void LogWarn(string warningMessage)
        {
            WriteLine("Warning: " + warningMessage);
        }

        public void LogError(string errorMessage)
        {
            WriteLine("Error: " + errorMessage);
        }

        private void WriteLine(string message)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            OutputWindowPane.Activate();
            OutputWindowPane.OutputString(_outputPrefix + message + "\r\n");
        }
    }
}
