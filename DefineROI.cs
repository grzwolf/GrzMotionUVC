using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MotionUVC {
    public partial class DefineROI : Form {

        // public property
        public List<MainForm.oneROI> ROIsList { get; set; }

        // locals
        bool dirtyFlag = false;
        int currListNdx = 0;
        bool currRectDrawing = false;
        Rectangle currRect = new Rectangle();

        public DefineROI() {
            InitializeComponent();
        }

        private void DefineROI_Shown(object sender, EventArgs e) {
            while ( ROIsList[currListNdx].rect.Width <= 0 || ROIsList[currListNdx].rect.Height <= 0 ) {
                currListNdx++;
                if ( currListNdx >= ROIsList.Count ) {
                    currListNdx = 0;
                    break;
                }
            }
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }

        // form close methods
        private void buttonOk_Click(object sender, EventArgs e) {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        private void buttonCancel_Click(object sender, EventArgs e) {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        // set image to be shown in pictureBox
        public void SetImage(Bitmap bmp) {
            this.pictureBox.Image = (Bitmap)bmp.Clone();
        }

        // set dirty flag at any change
        private void numericUpDownPosX_ValueChanged(object sender, EventArgs e) {
            this.numericUpDownPosX.ValueChanged -= new System.EventHandler(this.numericUpDownPosX_ValueChanged);
            this.numericUpDownPosX.Value = Math.Min(this.numericUpDownPosX.Value, this.pictureBox.Image.Width - this.numericUpDownWidthX.Value);
            this.numericUpDownPosX.ValueChanged += new System.EventHandler(this.numericUpDownPosX_ValueChanged);
            dirtyFlag = true;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void numericUpDownWidthX_ValueChanged(object sender, EventArgs e) {
            this.numericUpDownWidthX.ValueChanged -= new System.EventHandler(this.numericUpDownWidthX_ValueChanged);
            this.numericUpDownWidthX.Value = Math.Min(this.numericUpDownWidthX.Value, this.pictureBox.Image.Width - this.numericUpDownPosX.Value);
            this.numericUpDownWidthX.ValueChanged += new System.EventHandler(this.numericUpDownWidthX_ValueChanged);
            dirtyFlag = true;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void numericUpDownPosY_ValueChanged(object sender, EventArgs e) {
            this.numericUpDownPosY.ValueChanged -= new System.EventHandler(this.numericUpDownPosY_ValueChanged);
            this.numericUpDownPosY.Value = Math.Min(this.numericUpDownPosY.Value, this.pictureBox.Image.Height - this.numericUpDownHeightY.Value);
            this.numericUpDownPosY.ValueChanged += new System.EventHandler(this.numericUpDownPosY_ValueChanged);
            dirtyFlag = true;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void numericUpDownHeightY_ValueChanged(object sender, EventArgs e) {
            this.numericUpDownHeightY.ValueChanged -= new System.EventHandler(this.numericUpDownHeightY_ValueChanged);
            this.numericUpDownHeightY.Value = Math.Min(this.numericUpDownHeightY.Value, this.pictureBox.Image.Height - this.numericUpDownPosY.Value);
            this.numericUpDownHeightY.ValueChanged += new System.EventHandler(this.numericUpDownHeightY_ValueChanged);
            dirtyFlag = true;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void numericUpDownIntensity_ValueChanged(object sender, EventArgs e) {
            dirtyFlag = true;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void textBoxThreshold_TextChanged(object sender, EventArgs e) {
            double tmp = double.Parse(this.textBoxThreshold.Text);
            if ( tmp > 100 ) {
                this.textBoxThreshold.Text = "100.0";
            }
            if ( tmp < 0 ) {
                this.textBoxThreshold.Text = "0.0";
            }
            dirtyFlag = true;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void numericUpDownBoxScaler_ValueChanged(object sender, EventArgs e) {
            dirtyFlag = true;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void checkBoxReferenceROI_CheckedChanged(object sender, EventArgs e) {
            dirtyFlag = true;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        // apply changes from UI to currently selected ROI
        private void buttonApply_Click(object sender, EventArgs e) {
            // new ROI rect
            ROIsList[currListNdx].rect = new Rectangle((int)this.numericUpDownPosX.Value, (int)this.numericUpDownPosY.Value, (int)this.numericUpDownWidthX.Value, (int)this.numericUpDownHeightY.Value);
            // new ROI intensity
            ROIsList[currListNdx].thresholdIntensity = (int)this.numericUpDownIntensity.Value;
            // new ROI percentage
            ROIsList[currListNdx].thresholdChanges = double.Parse(this.textBoxThreshold.Text, System.Globalization.CultureInfo.InvariantCulture) / 100.0f;
            // new ROI scaler
            ROIsList[currListNdx].boxScaler = (int)this.numericUpDownBoxScaler.Value;
            // only if reference status was changed at all
            if ( ROIsList[currListNdx].reference != this.checkBoxReferenceROI.Checked ) {
                // new reference ROI: before a new reference ROI is declared, make sure all other ROIS are not a reference
                for ( int i = 0; i < ROIsList.Count; i++ ) {
                    if ( ROIsList[i].rect.Width >= 0 && ROIsList[i].rect.Height >= 0 ) {
                        ROIsList[i].reference = false;
                    }
                }
                ROIsList[currListNdx].reference = this.checkBoxReferenceROI.Checked;
            }
            // no longer dirty
            dirtyFlag = false;
            // update UI
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        // reset changes of currently selected ROI
        private void buttonReset_Click(object sender, EventArgs e) {
            dirtyFlag = false;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }

        // delete currently selected ROI
        private void buttonDeleteROI_Click(object sender, EventArgs e) {
            ROIsList[currListNdx].rect = new Rectangle();
            ROIsList[currListNdx].thresholdIntensity = 15;
            ROIsList[currListNdx].thresholdChanges = 0.05f;
            ROIsList[currListNdx].boxScaler = 1;
            ROIsList[currListNdx].reference = false;
            int fstRoiNdx = -1;
            for ( int i = 0; i < ROIsList.Count; i++ ) {
                if ( ROIsList[i].rect.Width >= 0 && ROIsList[i].rect.Height >= 0 ) {
                    fstRoiNdx = i;
                    break;
                }
            }
            currListNdx = fstRoiNdx == -1 ? fstRoiNdx : 0;
            dirtyFlag = false;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }

        // draw a rectangle into pictureBox
        private void pictureBox_MouseDown(object sender, MouseEventArgs e) {
            if ( e.X >= this.pictureBox.Image.Width || e.Y >= this.pictureBox.Image.Height ) {
                return;
            }
            currRectDrawing = true;
            currRect = new Rectangle();
            currRect.X = e.X;
            currRect.Y = e.Y;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void pictureBox_MouseMove(object sender, MouseEventArgs e) {
            if ( e.X >= this.pictureBox.Image.Height || e.Y >= this.pictureBox.Image.Height ) {
                return;
            }
            if ( !currRectDrawing ) {
                return;
            }
            currRect.Width = e.X - currRect.X;
            currRect.Height = e.Y - currRect.Y;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void pictureBox_MouseUp(object sender, MouseEventArgs e) {
            currRect.Width = e.X - currRect.X;
            currRect.Height = e.Y - currRect.Y;
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
            currRectDrawing = false;
        }
        private void buttonAddROI_Click(object sender, EventArgs e) {
            // plausibility test
            if ( currRect.Width <= 0 || currRect.Height <= 0 || this.buttonAddROI.Font.Italic ) {
                return;
            }
            // find the first ROI with empty width and height
            int newRoiNdx = -1;
            for ( int i = 0; i < ROIsList.Count; i++ ) {
                // take ROI, which has empty width and height
                if ( ROIsList[i].rect.Width == 0 && ROIsList[i].rect.Height == 0 ) {
                    newRoiNdx = i;
                    break;
                }
            }
            // empty ROI (aka unused) found
            if ( newRoiNdx != -1 ) {
                ROIsList[newRoiNdx].rect = currRect;
                ROIsList[newRoiNdx].thresholdIntensity = 15;
                ROIsList[newRoiNdx].thresholdChanges = 0.05f;
                ROIsList[newRoiNdx].boxScaler = 1;
                ROIsList[newRoiNdx].reference = false;
                currListNdx = newRoiNdx;
                dirtyFlag = false;
                currRect = new Rectangle();
                this.pictureBox.Invalidate();
                this.pictureBox.Update();
            } else {
                currRect = new Rectangle();
                MessageBox.Show("Error", "Number of ROIs is limited to 10.", MessageBoxButtons.OK);
            }
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }

        // switch ROI
        private void buttonPrevROI_Click(object sender, EventArgs e) {
            currListNdx--;
            if ( currListNdx < 0 ) {
                currListNdx = ROIsList.Count - 1;
            }
            int iterationCount = 0;
            while ( ROIsList[currListNdx].rect.Width <= 0 || ROIsList[currListNdx].rect.Height <= 0 ) {
                currListNdx--;
                if ( currListNdx < 0 ) {
                    currListNdx = ROIsList.Count - 1;
                }
                iterationCount++;
                if ( iterationCount >= ROIsList.Count ) {
                    currListNdx = 0;
                    ROIsList[0].rect = currRect;
                    ROIsList[0].thresholdIntensity = 15;
                    ROIsList[0].thresholdChanges = 0.05f;
                    ROIsList[0].boxScaler = 1;
                    ROIsList[0].reference = false;
                    break;
                }
            }
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }
        private void buttonNextROI_Click(object sender, EventArgs e) {
            currListNdx++;
            if ( currListNdx >= ROIsList.Count ) {
                currListNdx = 0;
            }
            int iterationCount = 0;
            while ( ROIsList[currListNdx].rect.Width <= 0 || ROIsList[currListNdx].rect.Height <= 0 ) {
                currListNdx++;
                if ( currListNdx >= ROIsList.Count ) {
                    currListNdx = 0;
                }
                iterationCount++;
                if ( iterationCount >= ROIsList.Count ) {
                    currListNdx = 0;
                    ROIsList[0].rect = currRect;
                    ROIsList[0].thresholdIntensity = 15;
                    ROIsList[0].thresholdChanges = 0.05f;
                    ROIsList[0].boxScaler = 1;
                    ROIsList[0].reference = false;
                    break;
                }
            }
            this.pictureBox.Invalidate();
            this.pictureBox.Update();
        }

        // all the drawing
        private void pictureBox_Paint(object sender, PaintEventArgs e) {
            // draw existing rectangles inclusive their respective ROI number
            for ( int i = 0; i < ROIsList.Count; i++ ) {
                // skip default ROIs, which have empty width and height
                if ( ROIsList[i].rect.Width > 0 && ROIsList[i].rect.Height > 0 ) {
                    // update panel to active ROI
                    if ( i == currListNdx ) {
                        // update shall only take place, if thre was no parameter change at all
                        if ( !dirtyFlag ) {
                            // update panel with ROI parameters according to active ROI
                            this.labelActiveROI.Text = currListNdx.ToString();
                            this.numericUpDownPosX.Value = ROIsList[i].rect.X;
                            this.numericUpDownWidthX.Value = ROIsList[i].rect.Width;
                            this.numericUpDownPosY.Value = ROIsList[i].rect.Y;
                            this.numericUpDownHeightY.Value = ROIsList[i].rect.Height;
                            this.numericUpDownIntensity.Value = ROIsList[i].thresholdIntensity;
                            this.textBoxThreshold.Text = (ROIsList[i].thresholdChanges * 100.0f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                            this.numericUpDownBoxScaler.Value = ROIsList[i].boxScaler;
                            this.checkBoxReferenceROI.Checked = ROIsList[i].reference;
                            // intentional: because all input controls were updated, what sets dirtyFlag to true 
                            dirtyFlag = false;
                        }
                    }
                    // ROI border color depends on: selected rect vs. refrence vs. 'normal' show
                    using ( Pen pen = currListNdx == i ? new Pen(Color.Red, 2) : ROIsList[i].reference ? new Pen(Color.Yellow, 1) : new Pen(Color.Green, 1) ) {
                        // special treatment for currently selected ROI
                        if ( i == currListNdx ) {
                            if ( dirtyFlag ) {
                                // print out ROI number
                                e.Graphics.DrawString(i.ToString(), new Font("Arial", 15, FontStyle.Bold, GraphicsUnit.Pixel), Brushes.Red, (int)this.numericUpDownPosX.Value, (int)this.numericUpDownPosY.Value - 16);
                                // show changes to ROI rect
                                e.Graphics.DrawRectangle(pen, new Rectangle((int)this.numericUpDownPosX.Value, (int)this.numericUpDownPosY.Value, (int)this.numericUpDownWidthX.Value, (int)this.numericUpDownHeightY.Value));
                            } else {
                                // print out ROI number
                                e.Graphics.DrawString(i.ToString(), new Font("Arial", 15, FontStyle.Bold, GraphicsUnit.Pixel), Brushes.Red, ROIsList[i].rect.X, ROIsList[i].rect.Y - 16);
                                // just show
                                e.Graphics.DrawRectangle(pen, ROIsList[i].rect);
                            }
                        } else {
                            // print out ROI number
                            e.Graphics.DrawString(i.ToString(), new Font("Arial", 15, FontStyle.Bold, GraphicsUnit.Pixel), Brushes.Red, ROIsList[i].rect.X, ROIsList[i].rect.Y - 16);
                            // just show
                            e.Graphics.DrawRectangle(pen, ROIsList[i].rect);
                        }
                    }
                }
            }

            // control whether to allow changes/reset to selected ROI & disallow to switch to next/previous ROI & add new ROI
            this.buttonApply.Enabled = dirtyFlag;
            this.buttonReset.Enabled = dirtyFlag;
            this.buttonNextROI.Enabled = !dirtyFlag;
            this.buttonPrevROI.Enabled = !dirtyFlag;
            this.buttonAddROI.Enabled = !dirtyFlag;
            this.buttonDeleteROI.Enabled = !dirtyFlag;

            // draw currently created rectangle
            if ( currRect.Width > 0 && currRect.Height > 0 ) {
                Font font = new Font("Microsoft Sans Serif", 12, FontStyle.Regular, GraphicsUnit.Point);
                this.buttonAddROI.Font = font;
                using ( Pen pen = new Pen(Color.Red, 1) ) {
                    e.Graphics.DrawRectangle(pen, currRect);
                }
            } else {
                Font font = new Font("Microsoft Sans Serif", 12, FontStyle.Italic, GraphicsUnit.Point);
                this.buttonAddROI.Font = font;
            }
        }
    }
}
