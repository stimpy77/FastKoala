using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;

namespace Wijits.FastKoala.SourceControl
{
    public interface ISccBasicFileSystem
    {
        Task<bool> ItemIsUnderSourceControl(string filename);
        Task<bool> AddIfProjectIsSourceControlled(Project project, string filename);
        Task Checkout(string filename);
        Task Move(string source, string destination);
        Task Delete(string filename);
        Task UndoMove(string filename, string originalPath);
    }
}
