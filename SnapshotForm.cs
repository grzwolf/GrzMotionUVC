using System;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.IO;

namespace MotionUVC
{
    public partial class SnapshotForm : Form
    {
        Bitmap bitmap1, bitmap2, bitmap3;
        bool fullRes = false;
        DateTime now;

        public SnapshotForm()
        {
            InitializeComponent();
        }

        public SnapshotForm(Bitmap bitmap1, Bitmap bitmap2, Bitmap bitmap3)
        {
            InitializeComponent();
            now = DateTime.Now;
            this.bitmap1 = (Bitmap)bitmap1.Clone();
            this.bitmap2 = (Bitmap)bitmap2.Clone();
            this.bitmap3 = (Bitmap)bitmap3.Clone();
        }

        private void SnapshotForm_Shown(object sender, EventArgs e) {
            this.buttonBmp1.PerformClick();
        }
        private void buttonBmp1_Click(object sender, EventArgs e) {
            SetImage(this.bitmap1);
            fullRes = false;
        }
        private void buttonBmp2_Click(object sender, EventArgs e) {
            SetImage(this.bitmap2);
            fullRes = false;
        }
        private void buttonBmp3_Click(object sender, EventArgs e) {
            SetImage(this.bitmap3);
            fullRes = true;
        }

        public void SetImage(Bitmap bitmap)
        {
            if ( bitmap == null ) {
                return;
            }

            timeBox.Text = this.now.ToLongTimeString() + "  --  " + bitmap.Width.ToString() + " x " + bitmap.Height.ToString();

            lock ( this ) {
                if ( pictureBox.Image != null ) {
                    pictureBox.Image.Dispose();
                }
                pictureBox.Image = (Bitmap)bitmap.Clone();
            }
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach ( ImageCodecInfo codec in codecs ) {
                if ( codec.FormatID == format.Guid ) {
                    return codec;
                }
            }
            return null;
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            if ( saveFileDialog.ShowDialog() == DialogResult.OK ) {
                string ext = Path.GetExtension(saveFileDialog.FileName).ToLower();
                ImageFormat format = ImageFormat.Jpeg;
                ImageCodecInfo encoder = GetEncoder(ImageFormat.Jpeg);

                if ( ext == ".bmp" ) {
                    format = ImageFormat.Bmp;
                } else {
                    if ( ext == ".png" ) {
                        format = ImageFormat.Png;
                    }
                }

                System.Drawing.Imaging.Encoder myEncoder = System.Drawing.Imaging.Encoder.Quality;
                EncoderParameters myEncoderParameters = new EncoderParameters(1);
                EncoderParameter myEncoderParameter = new EncoderParameter(myEncoder, 100L);
                myEncoderParameters.Param[0] = myEncoderParameter;

                try {
                    lock ( this ) {
                        Bitmap image = (Bitmap)pictureBox.Image;
                        if ( fullRes ) {
                            image = this.bitmap3;
                        }
                        if ( ext == ".jpg" ) {
                            image.Save(saveFileDialog.FileName, encoder, myEncoderParameters);
                        } else {
                            image.Save(saveFileDialog.FileName, format);
                        }
                    }
                } catch ( Exception ex ) {
                    MessageBox.Show("Failed saving the snapshot.\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

    }
}
