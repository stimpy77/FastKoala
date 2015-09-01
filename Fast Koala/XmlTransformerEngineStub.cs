using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;

namespace Wijits.FastKoala
{
    public class XmlTransformerEngineStub : IBuildEngine
    {
        public void LogErrorEvent(BuildErrorEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties,
            IDictionary targetOutputs)
        {
            return false;
        }

        public bool ContinueOnError { get; private set; }
        public int LineNumberOfTaskNode { get; private set; }
        public int ColumnNumberOfTaskNode { get; private set; }
        public string ProjectFileOfTaskNode { get; private set; }
    }
}
