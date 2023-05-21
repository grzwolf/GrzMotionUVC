namespace Loupe
{
    partial class Loupe
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
            if ( disposing && (components != null) ) {
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
            this.magnifyingGlass1 = new DesktopColorPicker.MagnifyingGlass();
            this.SuspendLayout();
            // 
            // magnifyingGlass1
            // 
            this.magnifyingGlass1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.magnifyingGlass1.Cursor = System.Windows.Forms.Cursors.SizeAll;
            this.magnifyingGlass1.Location = new System.Drawing.Point(1, 1);
            this.magnifyingGlass1.Name = "magnifyingGlass1";
            this.magnifyingGlass1.PixelRange = 10;
            this.magnifyingGlass1.PixelSize = 5;
            this.magnifyingGlass1.PosAlign = System.Drawing.ContentAlignment.TopLeft;
            this.magnifyingGlass1.PosFormat = "#x ; #y";
            this.magnifyingGlass1.ShowPixel = true;
            this.magnifyingGlass1.ShowPosition = true;
            this.magnifyingGlass1.Size = new System.Drawing.Size(105, 105);
            this.magnifyingGlass1.TabIndex = 0;
            this.magnifyingGlass1.UseMovingGlass = true;
            // 
            // Loupe
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(107, 107);
            this.Controls.Add(this.magnifyingGlass1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Loupe";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Loupe";
            this.ResumeLayout(false);

        }

        #endregion

        private DesktopColorPicker.MagnifyingGlass magnifyingGlass1;
    }
}