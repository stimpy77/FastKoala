using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Wijits.FastKoala.Transformations;

namespace Wijits.FastKoala.Utilities
{
    public class FileUtilities
    {
        // ReSharper disable InconsistentNaming
        private const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const int FILE_ATTRIBUTE_NORMAL = 0x80;
        // ReSharper enable InconsistentNaming

        public static string GetRelativePath(string fromPath, string toPath, bool trimDotSlash = true)
        {
            var fromAttr = GetPathAttribute(fromPath);
            var toAttr = GetPathAttribute(toPath);

            var path = new StringBuilder(260); // MAX_PATH
            if (PathRelativePathTo(
                path,
                fromPath,
                fromAttr,
                toPath,
                toAttr) == 0)
            {
                throw new ArgumentException("Paths must have a common prefix");
            }
            var result = path.ToString();
            if (trimDotSlash && result.StartsWith(".\\"))
                result = result.Substring(2);
            return result;
        }

        [DllImport("shlwapi.dll", SetLastError = true)]
        private static extern int PathRelativePathTo(StringBuilder pszPath,
            string pszFrom, int dwAttrFrom, string pszTo, int dwAttrTo);

        private static int GetPathAttribute(string path)
        {
            var di = new DirectoryInfo(path);
            if (di.Exists)
            {
                return FILE_ATTRIBUTE_DIRECTORY;
            }

            var fi = new FileInfo(path);
            if (fi.Exists)
            {
                return FILE_ATTRIBUTE_NORMAL;
            }

            // ReSharper disable once StringLastIndexOfIsCultureSpecific.1
            if (path.LastIndexOf(Path.DirectorySeparatorChar) < path.LastIndexOf("."))
                return FILE_ATTRIBUTE_NORMAL;
            return FILE_ATTRIBUTE_DIRECTORY;
        }

        public static bool ExistsOnPath(string fileName)
        {
            if (GetFullPath(fileName) != null)
                return true;
            return false;
        }

        public static string GetFullPath(string fileName)
        {
            if (File.Exists(fileName))
                return Path.GetFullPath(fileName);

            var values = Environment.GetEnvironmentVariable("PATH");
            Debug.Assert(values != null, "values != null");
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var path in values.Split(';'))
            {
                var fullPath = Path.Combine(path, fileName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            return null;
        }

        public static void WriteFileFromAssemblyResourceManifest(string resourceName, string resourcePlaceholderValue,
            string resourcePlaceholderDefault, string targetPath)
        {
            var assembly = typeof(FastKoalaPackage).Assembly;
            resourceName = typeof(FastKoalaPackage).Namespace + @".Resources." + resourceName.Replace("\\", ".");
            using (var stream = assembly.GetManifestResourceStream(string.Format(resourceName, resourcePlaceholderValue))
                             ?? assembly.GetManifestResourceStream(string.Format(resourceName, resourcePlaceholderDefault)))
            {
                var parentDirectory = Directory.GetParent(targetPath).FullName;
                if (!Directory.Exists(parentDirectory)) Directory.CreateDirectory(parentDirectory);
                using (var fileStream = File.OpenWrite(targetPath))
                {
                    Debug.Assert(stream != null, "stream != null");
                    stream.CopyTo(fileStream);
                }
            }
        }
    }
}