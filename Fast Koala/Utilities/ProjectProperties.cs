﻿using System.Linq;
using EnvDTE;
using Microsoft.Build.Construction;

namespace Wijits.FastKoala.Utilities
{
    public class ProjectProperties
    {
        private readonly DTE _dte;
        private string _projectUniqueName;
        private string _projectName;
        public ProjectProperties(Project project)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            _dte = project.DTE;
            Project = project;
        }

        public Project Project
        {
            get
            {
                var t = _dte.GetProjectByUniqueNameAsync(_projectUniqueName);
                t.ConfigureAwait(true);
                var result = t.Result;
                if (result == null)
                {
                    t = _dte.GetProjectByNameAsync(_projectName);
                    t.ConfigureAwait(true);
                    result = t.Result;
                }
                return result;
            }
            set
            {
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                _projectName = value.Name;
                _projectUniqueName = value.UniqueName;
            }
        }

        private ProjectRootElement ProjectRoot
        {
            get { return Project.GetProjectRoot(); }
        }

        public bool BuildTimeAppCfgTransformsEnabled
        {
            get { return (GetPropertyValue("BuildTimeAppCfgTransformsEnabled") ?? "").ToLower() == "true"; }
            set { SetPropertyValue("BuildTimeAppCfgTransformsEnabled", value); }
        }

        public void SetPropertyValue(string propertyName, object propertyValue, string condition = null)
        {
            var existingProp = ProjectRoot.Properties.LastOrDefault(
                p => p.Name == propertyName &&
                     ((condition == null && string.IsNullOrEmpty(p.Condition)) ||
                      (condition != null && p.Condition == condition)));
            if (propertyValue == null) propertyValue = "";
            if (propertyValue is bool) propertyValue = propertyValue.ToString().ToLower();
            if (existingProp != null) existingProp.Value = propertyValue.ToString();
            else ProjectRoot.AddProperty(propertyName, propertyValue.ToString());
        }

        public string GetPropertyValue(string propertyName, string condition = null)
        {
            var prop = ProjectRoot.Properties.LastOrDefault(
                p => p.Name == propertyName &&
                     ((condition == null && string.IsNullOrEmpty(p.Condition)) ||
                      (condition != null && p.Condition == condition)));
            return prop != null ? prop.Value : null;
        }

        public string AppCfgType
        {
            get { return GetPropertyValue("AppCfgType"); }
            set { SetPropertyValue("AppCfgType", value); }
        }

        public string ConfigDir
        {
            get { return GetPropertyValue("ConfigDir"); }
            set { SetPropertyValue("ConfigDir", value); }
        }

        public bool? InlineAppCfgTransforms
        {
            get
            {
                var ret = GetPropertyValue("InlineAppCfgTransforms");
                if (string.IsNullOrEmpty(ret)) return null;
                return bool.Parse(ret);
            }
            set
            {
                SetPropertyValue("InlineAppCfgTransforms", value);
            }
        }

        public string CfgBaseName
        {
            get
            {
                var ret = GetPropertyValue("CfgBaseName");
                if (string.IsNullOrEmpty(ret)) ret = "Base";
                return ret;
            }
            set
            {
                SetPropertyValue("CfgBaseName", value);
            }
        }

        public bool PowerShellBuildEnabled
        {
            get
            {
                var ret = GetPropertyValue("PowerShellBuildEnabled");
                if (string.IsNullOrEmpty(ret)) ret = "false";
                return bool.Parse(ret);
            }
            set
            {
                SetPropertyValue("PowerShellBuildEnabled", value);
            }
        }

        public bool NodeJSBuildEnabled
        {
            get
            {
                var ret = GetPropertyValue("NodeJSBuildEnabled");
                if (string.IsNullOrEmpty(ret)) ret = "false";
                return bool.Parse(ret);
            }
            set
            {
                SetPropertyValue("NodeJSBuildEnabled", value);
            }
        }
    }
}
