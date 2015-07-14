using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Wijits.FastKoala.Transformations
{
    public partial class ConfigDirPrompt : Form
    {
        public ConfigDirPrompt()
        {
            InitializeComponent();
        }

        public string ConfigDir
        {
            get { return txtConfigDir.Text; }
            set { txtConfigDir.Text = value; }
        }
    }
}
