using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using EnvDTE;
using Wijits.FastKoala.Events;
using Wijits.FastKoala.Utilities;

namespace Wijits.FastKoala.Transformations
{
    public class ConfigWatcher : IDisposable
    {
        private FileSystemWatcher _fileSystemWatcher;
        public event EventHandler<AppConfigFileChangedEventArgs> AppConfigFileChanged;
        private Project _project;
        private bool _isBuilding;
        private DateTime _buildingTimeout = DateTime.MinValue;

        public ConfigWatcher(Project project)
        {
            _project = project;
            var appConfig = project.GetConfigFile();
            if (!string.IsNullOrEmpty(appConfig))
            {
                var fileInfo = new FileInfo(appConfig);
                Debug.Assert(fileInfo.DirectoryName != null, "fileInfo.DirectoryName != null");
                _fileSystemWatcher = new FileSystemWatcher(fileInfo.DirectoryName, fileInfo.Name);
                _fileSystemWatcher.Changed += OnAppConfigChanged;
                _fileSystemWatcher.EnableRaisingEvents = true;
            }
        }

        private void OnAppConfigChanged(object sender, FileSystemEventArgs fileSystemEventArgs)
        {
            if (_project == null) return;
            if (IsBuilding) return;
            if (AppConfigFileChanged != null)
            {
                AppConfigFileChanged(_project, new AppConfigFileChangedEventArgs(_project, fileSystemEventArgs.FullPath));
            }
        }

        public void Dispose()
        {
            _project = null;
            _fileSystemWatcher = null;
        }

        public bool IsBuilding
        {
            get { return _isBuilding || DateTime.Now - _buildingTimeout < TimeSpan.FromMilliseconds(500); }
            set
            {
                _isBuilding = value;
                _buildingTimeout = DateTime.Now;
            }
        }
    }
}
