using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
// ReSharper disable StringLastIndexOfIsCultureSpecific.1

namespace Wijits.FastKoala.BuildScriptSupport
{
    public partial class AddBuildScriptNamePrompt : Form
    {
        public AddBuildScriptNamePrompt(string containerDirectory, string fileExtension)
        {
            InitializeComponent();
            if (fileExtension.StartsWith(".")) fileExtension = fileExtension.Substring(1);
            FileExtension = fileExtension;

            var proposedFilePath = FindNewFileName(containerDirectory, fileExtension);
            var proposedFileName = proposedFilePath.Substring(proposedFilePath.LastIndexOf("\\") + 1);
            txtBuildScriptFileName.Text = proposedFileName;
            txtBuildScriptFileName.SelectionStart = 0;
            txtBuildScriptFileName.SelectionLength = txtBuildScriptFileName.Text.Length
                                                     - fileExtension.Length - 1;
        }

        private string FindNewFileName(string containerDirectory, string fileExtension)
        {
            var result = Path.Combine(containerDirectory, "BuildScript." + fileExtension);
            var i = 1;
            while (File.Exists(result))
            {
                result = Path.Combine(containerDirectory, ("BuildScript" 
                    + (++i) + "." + fileExtension));
            }
            return result;
        }

        private void AddBuildScriptNamePrompt_Load(object sender, EventArgs e)
        {

        }

        public string FileExtension { get; set; }

        public bool InvokeAfter
        {
            get { return rdoInvokeAfter.Checked; }
        }

        public string FileName
        {
            get { return txtBuildScriptFileName.Text; }
            set { txtBuildScriptFileName.Text = value; }
        }
    }
}
