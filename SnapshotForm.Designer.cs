namespace MotionUVC
{
    partial class SnapshotForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose( bool disposing )
        {
            if ( disposing && ( components != null ) )
            {
                components.Dispose( );
            }
            base.Dispose( disposing );
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent( )
        {
            this.pictureBox = new System.Windows.Forms.PictureBox();
            this.saveButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.timeBox = new System.Windows.Forms.TextBox();
            this.saveFileDialog = new System.Windows.Forms.SaveFileDialog();
            this.buttonBmp1 = new System.Windows.Forms.Button();
            this.buttonBmp2 = new System.Windows.Forms.Button();
            this.buttonBmp3 = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox
            // 
            this.pictureBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pictureBox.BackColor = System.Drawing.SystemColors.ControlDark;
            this.pictureBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pictureBox.Location = new System.Drawing.Point(10, 40);
            this.pictureBox.Name = "pictureBox";
            this.pictureBox.Size = new System.Drawing.Size(435, 315);
            this.pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox.TabIndex = 0;
            this.pictureBox.TabStop = false;
            // 
            // saveButton
            // 
            this.saveButton.Location = new System.Drawing.Point(10, 10);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(75, 23);
            this.saveButton.TabIndex = 1;
            this.saveButton.Text = "&Save";
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.saveButton_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(113, 15);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(52, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Snapshot";
            // 
            // timeBox
            // 
            this.timeBox.Location = new System.Drawing.Point(173, 12);
            this.timeBox.Name = "timeBox";
            this.timeBox.ReadOnly = true;
            this.timeBox.Size = new System.Drawing.Size(138, 20);
            this.timeBox.TabIndex = 3;
            // 
            // saveFileDialog
            // 
            this.saveFileDialog.Filter = "JPEG images (*.jpg)|*.jpg|PNG images (*.png)|*.png|BMP images (*.bmp)|*.bmp";
            this.saveFileDialog.Title = "Save snapshot";
            // 
            // buttonBmp1
            // 
            this.buttonBmp1.Location = new System.Drawing.Point(325, 10);
            this.buttonBmp1.Name = "buttonBmp1";
            this.buttonBmp1.Size = new System.Drawing.Size(37, 23);
            this.buttonBmp1.TabIndex = 4;
            this.buttonBmp1.Text = "UI";
            this.buttonBmp1.UseVisualStyleBackColor = true;
            this.buttonBmp1.Click += new System.EventHandler(this.buttonBmp1_Click);
            // 
            // buttonBmp2
            // 
            this.buttonBmp2.Location = new System.Drawing.Point(367, 10);
            this.buttonBmp2.Name = "buttonBmp2";
            this.buttonBmp2.Size = new System.Drawing.Size(37, 23);
            this.buttonBmp2.TabIndex = 5;
            this.buttonBmp2.Text = "lores";
            this.buttonBmp2.UseVisualStyleBackColor = true;
            this.buttonBmp2.Click += new System.EventHandler(this.buttonBmp2_Click);
            // 
            // buttonBmp3
            // 
            this.buttonBmp3.Location = new System.Drawing.Point(408, 10);
            this.buttonBmp3.Name = "buttonBmp3";
            this.buttonBmp3.Size = new System.Drawing.Size(37, 23);
            this.buttonBmp3.TabIndex = 6;
            this.buttonBmp3.Text = "HD";
            this.buttonBmp3.UseVisualStyleBackColor = true;
            this.buttonBmp3.Click += new System.EventHandler(this.buttonBmp3_Click);
            // 
            // SnapshotForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(454, 364);
            this.Controls.Add(this.buttonBmp3);
            this.Controls.Add(this.buttonBmp2);
            this.Controls.Add(this.buttonBmp1);
            this.Controls.Add(this.timeBox);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.pictureBox);
            this.Name = "SnapshotForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Snapshot";
            this.Shown += new System.EventHandler(this.SnapshotForm_Shown);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox pictureBox;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox timeBox;
        private System.Windows.Forms.SaveFileDialog saveFileDialog;
        private System.Windows.Forms.Button buttonBmp1;
        private System.Windows.Forms.Button buttonBmp2;
        private System.Windows.Forms.Button buttonBmp3;
    }
}