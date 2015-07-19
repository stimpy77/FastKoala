using System.Linq;
using EnvDTE;
using Microsoft.Build.Construction;

namespace Wijits.FastKoala.Utilities
{
    public class ProjectProperties
    {
        private DTE _dte;
        private string _projectUniqueName;
        private string _projectName;
        public ProjectProperties(Project project)
        {
            _dte = project.DTE;
            Project = project;
        }

        public Project Project
        {
            get
            {
                return _dte.GetProjectByUniqueName(_projectUniqueName)
                    ?? _dte.GetProjectByName(_projectName);
            }
            set
            {
                _projectName = value.Name;
                _projectUniqueName = value.UniqueName;
            }
        }

        private ProjectRootElement ProjectRoot
        {
            get { return Project.GetProjectRoot(); }
        }

        public bool BuildTimeTransformsEnabled
        {
            get { return (GetPropertyValue("BuildTimeTransformsEnabled") ?? "").ToLower() == "true"; }
            set { SetPropertyValue("BuildTimeTransformsEnabled", value.ToString().ToLower()); }
        }

        public void SetPropertyValue(string propertyName, string propertyValue, string condition = null)
        {
            var existingProp = ProjectRoot.Properties.LastOrDefault(
                p => p.Name == propertyName &&
                     ((condition == null && string.IsNullOrEmpty(p.Condition)) ||
                      (condition != null && p.Condition == condition)));
            if (existingProp != null) existingProp.Value = propertyValue;
            else ProjectRoot.AddProperty(propertyName, propertyValue);
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

        public bool? InlineTransformations
        {
            get
            {
                var ret = GetPropertyValue("InlineTransformations");
                if (string.IsNullOrEmpty(ret)) return null;
                return bool.Parse(ret);
            }
            set
            {
                SetPropertyValue("InlineTransformations", value == null ? "" : value.ToString());
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
    }
}
