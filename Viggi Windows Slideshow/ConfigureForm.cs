using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Viggi_Windows_Slideshow
{
    public partial class ConfigureForm : Form
    {
        private ConfigFile mConfig = null;

        public ConfigureForm()
        {
            InitializeComponent();

            mConfig = ConfigFile.Load(ConfigFile.ConfigPath);

            directoryText.Text = mConfig.PicDirectory;
            randomCheckBox.Checked = mConfig.RandomOrder;
            labelCheckBox.Checked = mConfig.LabelPics;
            fileInfoCheckBox.Checked = mConfig.Info;
            intervalNumeric.Value = mConfig.IntervalMS;
        }

        private void browseButton_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "Select directory";
            dialog.SelectedPath = directoryText.Text;
            DialogResult result = dialog.ShowDialog(this);
            if (result == DialogResult.OK)
            {
                directoryText.Text = dialog.SelectedPath;
            }
        }
        
        private void okButton_Click(object sender, EventArgs e)
        {
            mConfig.PicDirectory = directoryText.Text;
            mConfig.RandomOrder = randomCheckBox.Checked;
            mConfig.LabelPics = labelCheckBox.Checked;
            mConfig.Info = fileInfoCheckBox.Checked;
            mConfig.IntervalMS = (int)intervalNumeric.Value;

            mConfig.Save();

            Application.Exit();
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
