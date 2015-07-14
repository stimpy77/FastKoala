using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;

namespace Wijits.FastKoala.SourceControl
{
    public class NonSccBasicFileSystem : ISccBasicFileSystem
    {
        public Task<bool> ItemIsUnderSourceControl(string filename)
        {
            return Task.FromResult(false);
        }

        public async Task<bool> AddIfProjectIsSourceControlled(Project project, string filename)
        {
            // do nothing
            return await Task.Run(() => false);
        }

        public async Task Move(string source, string destination)
        {
            File.Move(source, destination);
            await Task.Run(() => { });
        }

        public async Task Delete(string filename)
        {
            File.Delete(filename);
            await Task.Run(() => { });
        }


        public async Task UndoMove(string filename, string originalFilename)
        {
            await Move(filename, originalFilename);
        }


        public async Task Checkout(string filename)
        {
            // whatever
            await Task.Run(() => { });
        }
    }
}
