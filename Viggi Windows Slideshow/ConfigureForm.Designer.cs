namespace Viggi_Windows_Slideshow
{
    partial class ConfigureForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.label1 = new System.Windows.Forms.Label();
            this.okButton = new System.Windows.Forms.Button();
            this.directoryText = new System.Windows.Forms.TextBox();
            this.cancelButton = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.randomCheckBox = new System.Windows.Forms.CheckBox();
            this.labelCheckBox = new System.Windows.Forms.CheckBox();
            this.intervalNumeric = new System.Windows.Forms.NumericUpDown();
            this.browseButton = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.fileInfoCheckBox = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.intervalNumeric)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(97, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "These are settings!";
            // 
            // okButton
            // 
            this.okButton.Location = new System.Drawing.Point(225, 175);
            this.okButton.Name = "okButton";
            this.okButton.Size = new System.Drawing.Size(75, 23);
            this.okButton.TabIndex = 1;
            this.okButton.Text = "OK";
            this.okButton.UseVisualStyleBackColor = true;
            this.okButton.Click += new System.EventHandler(this.okButton_Click);
            // 
            // directoryText
            // 
            this.directoryText.Location = new System.Drawing.Point(13, 58);
            this.directoryText.Name = "directoryText";
            this.directoryText.Size = new System.Drawing.Size(287, 20);
            this.directoryText.TabIndex = 2;
            // 
            // cancelButton
            // 
            this.cancelButton.Location = new System.Drawing.Point(306, 175);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(75, 23);
            this.cancelButton.TabIndex = 3;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.Click += new System.EventHandler(this.cancelButton_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(13, 39);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(49, 13);
            this.label2.TabIndex = 4;
            this.label2.Text = "Directory";
            // 
            // randomCheckBox
            // 
            this.randomCheckBox.AutoSize = true;
            this.randomCheckBox.Location = new System.Drawing.Point(37, 96);
            this.randomCheckBox.Name = "randomCheckBox";
            this.randomCheckBox.Size = new System.Drawing.Size(95, 17);
            this.randomCheckBox.TabIndex = 5;
            this.randomCheckBox.Text = "Random Order";
            this.randomCheckBox.UseVisualStyleBackColor = true;
            // 
            // labelCheckBox
            // 
            this.labelCheckBox.AutoSize = true;
            this.labelCheckBox.Location = new System.Drawing.Point(197, 96);
            this.labelCheckBox.Name = "labelCheckBox";
            this.labelCheckBox.Size = new System.Drawing.Size(103, 17);
            this.labelCheckBox.TabIndex = 6;
            this.labelCheckBox.Text = "Show Filenames";
            this.labelCheckBox.UseVisualStyleBackColor = true;
            // 
            // intervalNumeric
            // 
            this.intervalNumeric.Location = new System.Drawing.Point(13, 149);
            this.intervalNumeric.Maximum = new decimal(new int[] {
            999999999,
            0,
            0,
            0});
            this.intervalNumeric.Name = "intervalNumeric";
            this.intervalNumeric.Size = new System.Drawing.Size(96, 20);
            this.intervalNumeric.TabIndex = 7;
            // 
            // browseButton
            // 
            this.browseButton.Location = new System.Drawing.Point(306, 56);
            this.browseButton.Name = "browseButton";
            this.browseButton.Size = new System.Drawing.Size(75, 23);
            this.browseButton.TabIndex = 8;
            this.browseButton.Text = "Browse";
            this.browseButton.UseVisualStyleBackColor = true;
            this.browseButton.Click += new System.EventHandler(this.browseButton_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(13, 130);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(64, 13);
            this.label3.TabIndex = 9;
            this.label3.Text = "Interval (ms)";
            // 
            // fileInfoCheckBox
            // 
            this.fileInfoCheckBox.AutoSize = true;
            this.fileInfoCheckBox.Location = new System.Drawing.Point(197, 126);
            this.fileInfoCheckBox.Name = "fileInfoCheckBox";
            this.fileInfoCheckBox.Size = new System.Drawing.Size(93, 17);
            this.fileInfoCheckBox.TabIndex = 10;
            this.fileInfoCheckBox.Text = "Show File Info";
            this.fileInfoCheckBox.UseVisualStyleBackColor = true;
            // 
            // ConfigureForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(393, 210);
            this.Controls.Add(this.fileInfoCheckBox);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.browseButton);
            this.Controls.Add(this.intervalNumeric);
            this.Controls.Add(this.labelCheckBox);
            this.Controls.Add(this.randomCheckBox);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.directoryText);
            this.Controls.Add(this.okButton);
            this.Controls.Add(this.label1);
            this.Name = "ConfigureForm";
            this.Text = "ConfigureForm";
            ((System.ComponentModel.ISupportInitialize)(this.intervalNumeric)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button okButton;
        private System.Windows.Forms.TextBox directoryText;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox randomCheckBox;
        private System.Windows.Forms.CheckBox labelCheckBox;
        private System.Windows.Forms.NumericUpDown intervalNumeric;
        private System.Windows.Forms.Button browseButton;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.CheckBox fileInfoCheckBox;
    }
}