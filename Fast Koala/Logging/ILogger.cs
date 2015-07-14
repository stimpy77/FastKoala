using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wijits.FastKoala.Logging
{
    public interface ILogger
    {
        void LogInfo(string infoMessage);
        void LogWarn(string warningMessage);
        void LogError(string errorMessage);
    }
}
