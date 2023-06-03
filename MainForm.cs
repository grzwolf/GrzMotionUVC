using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
// AForge download packages misses: SetVideoProperty, GetVideoProperty, GetVideoPropertyRange --> no brightness setting possible
// fix: http://www.aforgenet.com/forum/viewtopic.php?f=2&t=2939
using AForge.Video.DirectShow;
using System.Globalization;
using System.IO;
using TeleSharp.Entities;  
using TeleSharp.Entities.SendEntities;
using System.Threading;
using System.Threading.Tasks;
// Accord.Video.FFMPEG: !! needs both VC_redist.x86.exe and VC_redist.x64.exe installed on target PC !!
using Accord.Video.FFMPEG;
using static MotionUVC.AppSettings;
using GrzTools;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Linq;
using System.Drawing.Design;

namespace MotionUVC
{
    public partial class MainForm : Form, IMessageFilter {
                
        public class oneROI {                                                // class to define a single 'Region Of Interest' = ROI
            public Rectangle rect { get; set; }                              // monitor area  
            public int thresholdIntensity { get; set; }                      // pixel gray value threshold considered as a potential motion
            public double thresholdChanges { get; set; }                     // percentage of pixels in a ROI considered as a potential motion
            public bool reference { get; set; }                              // reference ROI to exclude false positive motions
            public int boxScaler { get; set; }                               // consecutive pixels in a box   
        };
        public static int ROICOUNT = 10;                                     // max ROI count
        List<oneROI> _roi = new List<oneROI>();                              // list containing ROIs

        public static AppSettings Settings = new AppSettings();              // app settings

        private FilterInfoCollection _videoDevices;                          // AForge collection of camera devices
        private VideoCaptureDevice _videoDevice = null;                      // AForge camera device  
                                                                             
        private string _buttonConnectString;                                 // original text on camera start button
                                                                             
        Bitmap _origFrame = null;                                            // current camera frame original
        public static Bitmap _currFrame = null;                              // current camera scaled frame (typically 800 x 600)
        Bitmap _procFrame = null;                                            // current camera scaled processed frame
        Bitmap _prevFrame = null;                                            // previous camera scaled frame
        double _frameAspectRatio = 1.3333f;                                  // default value until it is overridden via 'firstImageProcessing' in grabber 

        public class Motion {                                                // helper to have a motion list other than the stored files on disk
            public String fileNameMotion;
            public String fileNameProc;
            public DateTime motionDateTime;
            public Bitmap imageMotion;
            public Bitmap imageProc;
            public bool motionSaved;
            public bool motionConsecutive { get; set; }
            public Motion(String fileNameMotion, DateTime motionDateTime) {
                this.fileNameMotion = fileNameMotion;
                this.fileNameProc = "";
                this.motionDateTime = motionDateTime;
                this.motionConsecutive = false;
                this.imageMotion = null;
                this.imageProc = null;
                this.motionSaved = true;
            }
            public Motion(String fileNameMotion, DateTime motionDateTime, Bitmap image, String fileNameProc, Bitmap imageProc) {
                this.fileNameMotion = fileNameMotion;
                this.fileNameProc = fileNameProc;
                this.motionDateTime = motionDateTime;
                this.motionConsecutive = false;
                this.imageMotion = (Bitmap)image.Clone();
                this.imageProc = imageProc != null ? (Bitmap)imageProc.Clone() : null;
                this.motionSaved = false;
            }
        }
        List<Motion> _motionsList = new List<Motion>();                      // list of Motion, which are motion sequences if 'consecutive' is true

        int _motionsDetected = 0;                                            // all motions detection counter
        int _consecutivesDetected = -1;                                      // consecutive motions counter
        bool _justConnected = false;                                         // just connected 
        double _fps = 0;                                                     // current frame rate 
        long _procMs = 0;                                                    // current process time
        Size _sizeBeforeResize;                                              // MainForm size before a change was made by User

        double BRIGHTNESS_CHANGE_THRESHOLD = 10.0f;                          // experimental: camera exposure control thru app  
        int _brightnessNoChangeCounter = 0;                                  // experimental: camera no brightness change counter

        TimeSpan _midNight = new System.TimeSpan(0, 0, 0);                   // magic times 
        TimeSpan _videoTime = new System.TimeSpan(19, 0, 0);
        public static TimeSpan BootTimeBeg = new System.TimeSpan(0, 30, 0);
        public static TimeSpan BootTimeEnd = new System.TimeSpan(0, 31, 0);
        int _dailyVideoErrorCount = 0;                                       // make video error counter to prevent loops
        bool _dailyVideoInProgress = false;                                  // make video in progress flag
                
        TeleSharp.TeleSharp _Bot = null;                                     // Telegram bot  
        bool _alarmSequence = false;
        bool _alarmSequenceBusy = false;
        bool _alarmNotify = false;
        bool _sendVideo = false;
        MessageSender _notifyReceiver = null;
        MessageSender _sequenceReceiver = null;
        DateTime _connectionLiveTick = DateTime.Now;
        int _telegramOnErrorCount = 0;
        int _telegramLiveTickErrorCount = 0;
        bool _runPing = false;

        long ONE_GB =  1000000000;                                            // constants for file delete  
        long TWO_GB =  2000000000;                                            
        long TEN_GB = 10000000000;

        // the one and only way to avoid the 'red cross exception' in pictureBox: "wrong parameter" 
        public class PictureBoxPlus : PictureBox {
            // magic: catch exceptions inside 'protected override void OnPaint'; there is NO way to interpret/avoid them, since they come from the underlying Win32 
            protected override void OnPaint(PaintEventArgs pea) {
                try {
                    base.OnPaint(pea);
                } catch {; }
            }
        }

        // picturebox zoom & pan
        private PictureBoxPlus pictureBox;
        private int _iScaleStep = 0;
        private System.Windows.Rect _iRect = new System.Windows.Rect();
        private System.Drawing.Point _mouseDown = new System.Drawing.Point();
        private bool _stillImage = false;
        private System.Windows.Point _eOld = new System.Windows.Point(-1, -1);
        protected override void OnMouseWheel(MouseEventArgs e) {
            // mouse shall be in the ranges of pictureBox
            if ( e.X >= 0 && e.X < pictureBox.Width && e.Y >= 60 && e.Y < 60 + pictureBox.Height ) {
                // sanity check
                if ( !_stillImage ) {
                    return;
                }

                // init
                if ( _eOld.X == -1 ) {
                    _eOld.X = e.X;
                    _eOld.Y = e.Y;
                }

                // happens once at start
                if ( _iScaleStep == 0 ) {
                    _iRect = new System.Windows.Rect(0, 0, _origFrame.Width, _origFrame.Height);
                    pictureBox.Image = _origFrame;
                }

                // mouse was potentially moved before coming here
                double xMove = e.X - _eOld.X;
                double yMove = e.Y - _eOld.Y;

                // wheel direction
                if ( e.Delta != 0 ) {

                    // picturebox y offset
                    int yOfs = this.ClientSize.Height - pictureBox.Height;

                    // actual zooming
                    if ( e.Delta > 0 ) { // zoom in
                        // mouse was potentially moved before coming here, correct it matching to the latest known zoom scale
                        xMove /= Math.Pow(0.9f, _iScaleStep);
                        yMove /= Math.Pow(0.9f, _iScaleStep);

                        // somehow limit zoom
                        if ( _iRect.Width * 0.9f > this.pictureBox.Size.Width / 4 ) {
                            _iScaleStep++;
                            _iRect = new System.Windows.Rect(_iRect.X, _iRect.Y, _iRect.Width * 0.9f, _iRect.Height * 0.9f);
                        } else {
                            return;
                        }
                    } else { // zoom out
                        // mouse was potentially moved before coming here, correct it matching to the latest known zoom scale
                        xMove *= Math.Pow(0.9f, _iScaleStep);
                        yMove *= Math.Pow(0.9f, _iScaleStep);

                        // border constraints: width & height
                        if ( _iRect.Width / 0.9f > _origFrame.Width ) {
                            _iRect = new System.Windows.Rect(_iRect.X, _iRect.Y, _origFrame.Width, _origFrame.Height);
                        } else {
                            _iScaleStep--;
                            _iRect = new System.Windows.Rect(_iRect.X, _iRect.Y, _iRect.Width / 0.9f, _iRect.Height / 0.9f);
                        }
                    }

                    // mouse cursor X ratio to pictureBox width in percent
                    double pctXpos = (double)e.X / (double)pictureBox.ClientSize.Width;
                    // margin between original Bmp and zoomed tile of Bmp
                    double xMargin = _origFrame.Width - _iRect.Width;
                    // 'cursor X ratio' moves left bound of zoomed tile to xLeft position
                    double xLeft = xMargin * pctXpos;
                    // correct 'previous e.X before coming here (mouse move by user)' vs. 'current e.X
                    _iRect.X = Math.Max(0, xLeft + xMove);

                    // the same to y
                    double pctYpos = (double)(e.Y - yOfs) / (double)pictureBox.ClientSize.Height;
                    _iRect.Y = Math.Max(0, (int)((double)(_origFrame.Height - _iRect.Height) * pctYpos) + yMove);
                    
                    // border constraints: x + width & y + height
                    if ( _iRect.X + _iRect.Width > _origFrame.Width ) {
                        _iRect.X = _origFrame.Width - _iRect.Width;
                    }
                    if ( _iRect.Y + _iRect.Height > _origFrame.Height ) {
                        _iRect.Y = _origFrame.Height - _iRect.Height;
                    }
                    
                    // render image
                    Bitmap bmp = _origFrame.Clone(new Rectangle((int)_iRect.X, (int)_iRect.Y, (int)_iRect.Width, (int)_iRect.Height), PixelFormat.Format24bppRgb);
                    pictureBox.Image = bmp;

                    // save current mouse position
                    _eOld = new System.Windows.Point(e.X, e.Y);
                }
                // 1 : 1
                if ( _iScaleStep == 0 ) {
                    _iRect = new System.Windows.Rect(0, 0, _origFrame.Width, _origFrame.Height);
                    pictureBox.Image = _origFrame;
                }
            }
            base.OnMouseWheel(e);
        }
        private void pictureBox_OnMouseDown(object sender, MouseEventArgs e) {
            // sanity check
            if ( !_stillImage ) {
                return;
            }

            // initial mouse down position
            if ( e.Button == MouseButtons.Left ) {
                int yOfs = this.ClientSize.Height - pictureBox.Height;
                _mouseDown.X = e.X;
                _mouseDown.Y = e.Y;
            }

            // reset all zoom & pan to centered 1:1
            if ( e.Button == MouseButtons.Right ) {
                _eOld = new System.Windows.Point(-1, -1);
                _iScaleStep = 0;
                _mouseDown = new System.Drawing.Point();
                _iRect = new System.Windows.Rect(0, 0, _origFrame.Width, _origFrame.Height);
                pictureBox.Image = _origFrame;
            }
        }
        private void pictureBox_OnMouseMove(object sender, MouseEventArgs e) {
            if ( !_stillImage ) {
                return;
            }
            if ( e.Button == MouseButtons.Left ) {
                double pixelScaler = (double)_iRect.Width / (double)pictureBox.Width;
                int deltaX = (int)Math.Round((double)(e.X - _mouseDown.X) * pixelScaler);
                if ( deltaX != 0 ) {
                    int newX = (int)_iRect.X - deltaX;
                    if ( newX + _iRect.Width > _origFrame.Width ) {
                        newX = _origFrame.Width - (int)_iRect.Width;
                    }
                    if ( newX < 0 ) {
                        newX = 0;
                    }
                    _iRect.X = newX;
                }
                int yOfs = this.ClientSize.Height - pictureBox.Height;
                int deltaY = (int)Math.Round((double)(e.Y - _mouseDown.Y) * pixelScaler);
                if ( deltaY != 0 ) {
                    int newY = (int)_iRect.Y - deltaY;
                    if ( newY + _iRect.Height > _origFrame.Height ) {
                        newY = _origFrame.Height - (int)_iRect.Height;
                    }
                    if ( newY < 0 ) {
                        newY = 0;
                    }
                    _iRect.Y = newY;
                }
                Bitmap bmp = _origFrame.Clone(new Rectangle((int)_iRect.X, (int)_iRect.Y, (int)_iRect.Width, (int)_iRect.Height), PixelFormat.Format24bppRgb);
                pictureBox.Image = bmp;
                // memorize latest mouse down position
                _mouseDown.X = e.X;
                _mouseDown.Y = e.Y;
            }
        }

        public MainForm() {
            // form designer standard init
            InitializeComponent();

            // avoid empty var
            _sizeBeforeResize = this.Size;

            // subclassed PictureBoxPlus handles the 'red cross exception', couldn't find a way to make this class accessible thru designer & toolbox (exception thrown when dragging to form)
            this.pictureBox = new PictureBoxPlus();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).BeginInit();
            this.pictureBox.BackColor = System.Drawing.SystemColors.ActiveBorder;
            this.pictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pictureBox.Margin = new System.Windows.Forms.Padding(0);
            this.pictureBox.Name = "pictureBox";
            this.pictureBox.Size = new System.Drawing.Size(796, 492);
            this.pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox.TabIndex = 0;
            this.pictureBox.TabStop = false;
            this.tableLayoutPanelGraphs.Controls.Add(this.pictureBox, 0, 0);
            this.pictureBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pictureBox_OnMouseDown);
            this.pictureBox.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pictureBox_OnMouseMove);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).EndInit();

            // prevent flickering when paint: https://stackoverflow.com/questions/24910574/how-to-prevent-flickering-when-using-paint-method-in-c-sharp-winforms  
            Control ctrl = this.tableLayoutPanelGraphs;
            ctrl.GetType()
                .GetProperty("DoubleBuffered",
                             System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(this.tableLayoutPanelGraphs, true, null);
            ctrl = this.pictureBox;
            ctrl.GetType()
                .GetProperty("DoubleBuffered",
                             System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(this.pictureBox, true, null);

            // memorize the initial camera connect button text
            _buttonConnectString = this.connectButton.Text;

            // add "about entry" to app's system menu
            SetupSystemMenu();

            // distinguish between 'forced reboot app start after ping fail' and a 'regular app start'
            AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            if ( bool.Parse(ini.IniReadValue("MotionUVC", "RebootPingFlagActive", "False")) ) {
                // app start due to a app forced reboot after ping fail
                ini.IniWriteValue("MotionUVC", "RebootPingFlagActive", "False");
            } else {
                // if app was started regular, reset the ping reboot counter
                ini.IniWriteValue("MotionUVC", "RebootPingCounter", "0");
            }

            // get settings from INI
            Settings.fillPropertyGridFromIni();

            // before processing, images will be scaled down to a real image size
            Settings.ScaledImageSize = new Size(800, 600);                               

            // IMessageFilter - an encapsulated message filter
            // - also needed: class declaration "public partial class MainForm: Form, IMessageFilter"
            // - also needed: event handler "public bool PreFilterMessage( ref Message m )"
            // - also needed: Application.RemoveMessageFilter(this) when closing this form
            Application.AddMessageFilter(this);
        }

        // called when MainForm is finally shown
        private void MainForm_Shown(object sender, EventArgs e) {
            // log start
            AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            Settings.WriteLogfile = bool.Parse(ini.IniReadValue("MotionUVC", "WriteLogFile", "False"));
            Logger.WriteToLog = Settings.WriteLogfile;
            string path = ini.IniReadValue("MotionUVC", "StoragePath", Application.StartupPath + "\\");
            if ( !path.EndsWith("\\") ) {
                path += "\\";
            }
            Logger.FullFileNameBase = path + Path.GetFileName(Application.ExecutablePath);
            Logger.logTextU("\r\n---------------------------------------------------------------------------------------------------------------------------\r\n");
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            Logger.logTextLnU(DateTime.Now, String.Format("{0} {1}", assembly.FullName, fvi.FileVersion));
            // distinguish between regular app start and a restart after app crash 
            if ( bool.Parse(ini.IniReadValue("MotionUVC", "AppCrash", "False")) ) {
                Logger.logTextLnU(DateTime.Now, "App was restarted after crash");
            } else {
                Logger.logTextLn(DateTime.Now, "App start regular");
            }
            // assume an app crash as default behavior: this flag is reset to False, if app closes the normal way
            ini.IniWriteValue("MotionUVC", "AppCrash", "True");
            // set app properties according to settings; in case ini craps out, delete it and begin from scratch with defaults
            try {
                updateAppPropertiesFromSettings();
            } catch {
                System.IO.File.Delete(System.Windows.Forms.Application.ExecutablePath + ".ini");
                Settings.fillPropertyGridFromIni();
                updateAppPropertiesFromSettings();
            }
        }

        // when MainForm is loaded
        private void MainForm_Load(object sender, EventArgs e) {
            // check for UVC devices
            getCameraBasics();
            EnableConnectionControls(true);
        }

        // update app from settings
        void updateAppPropertiesFromSettings() {
            // UI app layout
            this.Size = Settings.FormSize;
            this.Location = Settings.FormLocation;
            // UI exposure controls
            this.hScrollBarExposure.Minimum = Settings.ExposureMin;
            this.hScrollBarExposure.Maximum = Settings.ExposureMax;
            this.hScrollBarExposure.Value = Settings.ExposureVal;
            this.toolTip.SetToolTip(this.hScrollBarExposure, "camera exposure time = " + Settings.ExposureVal.ToString() + " (" + this.hScrollBarExposure.Minimum.ToString() + ".." + this.hScrollBarExposure.Maximum.ToString() + ")");
            // write to logfile
            Logger.WriteToLog = Settings.WriteLogfile;
            if ( !Settings.StoragePath.EndsWith("\\") ) {
                Settings.StoragePath += "\\";
            }
            Logger.FullFileNameBase = Settings.StoragePath + Path.GetFileName(Application.ExecutablePath);
            // get ROI motion zones
            _roi = Settings.getROIsListFromPropertyGrid();
            // handle Telegram bot usage
            System.Net.NetworkInformation.PingReply reply = execPing(Settings.PingTestAddress);
            if ( reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success ) {
                Settings.PingOk = true;
                Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: ping ok");
            } else {
                Settings.PingOk = false;
                Logger.logTextLnU(DateTime.Now, "updateAppPropertiesFromSettings: ping failed");
                if ( Settings.UseTelegramBot ) {
                    Logger.logTextLnU(DateTime.Now, "updateAppPropertiesFromSettings: Telegram not activated due to ping fail");
                }
            }
            if ( Settings.PingOk ) {
                // could be, that Telegram was recently enabled in Settings
                if ( Settings.UseTelegramBot ) {
                    if ( _Bot == null ) {
                        _Bot = new TeleSharp.TeleSharp(Settings.BotAuthenticationToken);
                        _Bot.OnMessage += OnMessage;
                        _Bot.OnError += OnError;
                        _Bot.OnLiveTick += OnLiveTick;
                        this.timerCheckTelegramLiveTick.Start();
                        Logger.logTextLnU(DateTime.Now, "updateAppPropertiesFromSettings: Telegram bot activated");
                    } else {
                        Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: Telegram is already active");
                    }
                }
            }
            // could be, that Telegram was recently disabled in Settings
            if ( !Settings.UseTelegramBot ) {
                if ( _Bot != null ) {
                    _Bot.OnMessage -= OnMessage;
                    _Bot.OnError -= OnError;
                    _Bot.OnLiveTick -= OnLiveTick;
                    _Bot.Stop();
                    this.timerCheckTelegramLiveTick.Stop();
                    _Bot = null;
                    Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: Telegram bot deactivated");
                }
            }
            // ping monitoring in a UI-thread separated task, which is a loop !! overrides Settings.PingOk !!
            Settings.PingTestAddressRef = Settings.PingTestAddress;
            if ( !_runPing ) {
                _runPing = true;
                Task.Run(() => { doPingLooper(ref _runPing, ref Settings.PingTestAddressRef); });
            }
            // if camera was already started, allow to start OR stop webserver
            if ( this.connectButton.Text != this._buttonConnectString ) {
                if ( Settings.RunWebserver ) {
                    ImageWebServer.Start();
                }
            }
            if ( !Settings.RunWebserver ) {
                ImageWebServer.Stop();
            }
            // handle auto start motion detection via camera start
            if ( Settings.DetectMotion ) {
                // click camera button to start it, if not yet running: would start webserver too if enabled 
                if ( this.connectButton.Text == this._buttonConnectString ) {
                    this.connectButton.PerformClick();
                }
            } else {
                // don't click camera button to stop it, if running - because it's an autostart property
                //if ( this.connectButton.Text != this._buttonConnectString ) {
                //    this.connectButton.PerformClick();
                //}
            }
            // sync to motion count from today
            getTodaysMotionsCounters();
            // check whether settings were forcing a 'make video now'
            if ( Settings.MakeVideoNow ) {
                Task.Run(() => { makeMotionVideo(Settings.CameraResolution); });
            }
        }
        // update settings from app
        void updateSettingsFromAppProperties() {
            Settings.FormSize = this.Size;
            Settings.FormLocation = this.Location;
            Settings.ExposureVal = this.hScrollBarExposure.Value;
            Settings.ExposureMin = this.hScrollBarExposure.Minimum;
            Settings.ExposureMax = this.hScrollBarExposure.Maximum;
        }

        // update today's motions counters; perhaps useful, if app is restarted during the day
        private void getTodaysMotionsCounters() {
            DateTime now = DateTime.Now;
            string nowString = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if ( DateTime.Now.TimeOfDay >= new System.TimeSpan(19, 0, 0) ) {
                now = now.AddDays(1);
                nowString = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            string path = System.IO.Path.Combine(Settings.StoragePath, nowString);
            System.IO.Directory.CreateDirectory(path);
            DirectoryInfo di = new DirectoryInfo(path);
            FileInfo[] files = di.GetFiles("*.jpg");
            // save all but no sequences
            if ( Settings.SaveMotion && !Settings.SaveSequences ) {
                _motionsDetected = files.Length;
                _consecutivesDetected = -1;
            }
            // save sequences only
            if ( !Settings.SaveMotion && Settings.SaveSequences ) {
                _consecutivesDetected = files.Length != 0 ? files.Length : -1;
                // a bit of fake: _motionsDetected will always be larger than _consecutivesDetected, but _motionsDetected is not saved anywhere
                _motionsDetected = _consecutivesDetected;
                // in case path _nonc exists, adjust _motionsDetected accordingly
                string noncPath = System.IO.Path.Combine(Settings.StoragePath, nowString + "_nonc");
                di = new DirectoryInfo(noncPath);
                if ( di.Exists ) {
                    _motionsDetected += di.GetFiles("*.jpg").Length;
                }
            }
        }

        // a general timer 1x / 30s for app flow control
        private void timerFlowControl_Tick(object sender, EventArgs e) {

            // check once per 30s, whether to search & send an alarm video sequence; ideally it's just the rest after an already sent sequence of 6 motions started from detectMotion(..)
            if ( _alarmSequence && !_alarmSequenceBusy ) {
                // busy flag to prevent overrun
                _alarmSequenceBusy = true;
                // don't continue, if list is empty
                if ( _motionsList.Count == 0 ) {
                    _alarmSequenceBusy = false;
                    return;
                }
                // don't continue, if latest stored motion is older than 35s
                if ( (DateTime.Now.TimeOfDay.TotalSeconds - _motionsList[_motionsList.Count - 1].motionDateTime.TimeOfDay.TotalSeconds) > 35 ) {
                    _alarmSequenceBusy = false;
                    return;
                }
                // are there consecutive motions?
                Motion mo = new Motion("", new DateTime(1900, 01, 01));
                // pick the most recent motion
                try {
                    for ( int i = _motionsList.Count - 1; i >= 0; i-- ) {
                        if ( _motionsList[i].motionConsecutive ) {
                            mo = _motionsList[i];
                            break;
                        }
                    }
                } catch ( Exception exc ) {
                    Logger.logTextLnU(DateTime.Now, String.Format("timerFlowControl_Tick exc:{0}", exc.Message));
                    _alarmSequenceBusy = false;
                    return;
                }
                // don't continue, if latest consecutive motion is older than 35s
                if ( (DateTime.Now.TimeOfDay.TotalSeconds - mo.motionDateTime.TimeOfDay.TotalSeconds) > 35 ) {
                    _alarmSequenceBusy = false;
                    return;
                }
                // now it's worth to make a motion list copy
                List<Motion> currList = new List<Motion>(_motionsList);
                // add dummy entry to the original motion list, it acts like a marker of what was sent already
                try {
                    _motionsList.Add(new Motion("", new DateTime(1900, 1, 1)));
                } catch {;}
                // only pick the latest consecutive motion index
                int startNdx = -1;  
                for ( int i = currList.Count - 1; i >= 0; i-- ) {
                    if ( currList[i].motionConsecutive ) {
                        startNdx = i;
                        break;
                    }
                }
                // don't continue, if there is no consecutive motion
                if ( startNdx == -1 ) {
                    _alarmSequenceBusy = false;
                    return;
                }
                // make a sub list containing the latest consecutive motions
                List<Motion> subList = new List<Motion>();
                for ( int i = startNdx; i>=0; i-- ) {
                    if ( currList[i].motionConsecutive ) {
                        subList.Insert(0, currList[i]);
                    } else {
                        break;
                    }
                }
                // don't continue, if subList is too small
                if ( subList.Count < 2 ) {
                    _alarmSequenceBusy = false;
                    return;
                }
                // make latest motion video sequence, send it via Telegram and reset flag _alarmSequenceBusy when done
                Task.Run(() => { makeMotionSequence(subList, Settings.CameraResolution); });
            }

            // once per hour
            if ( DateTime.Now.Minute % 60 == 0  && DateTime.Now.Second < 31) {

                // log once per hour the current app status
                bool currentWriteLogStatus = Settings.WriteLogfile;
                if ( !Settings.WriteLogfile ) {
                    Settings.WriteLogfile = true;
                }
                Logger.logTextLnU(DateTime.Now, String.Format("motion detect count={0}/{1} process time={2}ms bot alive={3}", _motionsDetected, _consecutivesDetected, _procMs, (_Bot != null)));
                if ( !currentWriteLogStatus ) {
                    Settings.WriteLogfile = currentWriteLogStatus;
                }

                // check if remaining disk space is less than 2GB
                if ( (Settings.SaveMotion || Settings.SaveSequences) && driveFreeBytes(Settings.StoragePath) < TWO_GB ) {
                    Logger.logTextLnU(DateTime.Now, "timerFlowControl_Tick: free disk space <2GB");
                    // delete avi-files in storage folder, try to gain 10GB space (could mean all of them)
                    deleteAviFiles(Settings.StoragePath, TEN_GB);
                    // if the remaining disk space is still less than 1GB, start deleting the oldest image folder
                    if ( driveFreeBytes(Settings.StoragePath) < ONE_GB ) {
                        Logger.logTextLnU(DateTime.Now, "timerFlowControl_Tick: free disk space <1GB");
                        deleteOldestImageFolder(Settings.StoragePath);
                        // if finally the remaining disk space is still less than 1GB
                        if ( driveFreeBytes(Settings.StoragePath) < ONE_GB ) {
                            // check alternative storage path and switch to it if feasible
                            if ( System.IO.Directory.Exists(Settings.StoragePathAlt) && driveFreeBytes(Settings.StoragePathAlt) > TEN_GB ) {
                                Settings.StoragePath = Settings.StoragePathAlt;
                                Logger.logTextLnU(DateTime.Now, "Now using alternative storage path.");
                                return;
                            }
                            // if finally the remaining disk space were still less than 1GB --> give up storing anything on disk
                            Logger.logTextLnU(DateTime.Now, "MotionUVC stops saving detected motions due to lack of disk space.");
                            Settings.SaveMotion = false;
                            Settings.SaveSequences = false;
                            Settings.writePropertyGridToIni();
                        }
                    }
                }
            }

            // one check every 15 minutes
            if ( DateTime.Now.Minute % 15 == 0 && DateTime.Now.Second < 31 ) {

                // clean up _motionList from leftover Bitmaps
                for ( int i=0; i < _motionsList.Count - 1; i++ ) {
                    // ignore all entries younger than 60s: TBD ?? what if a sequence is longer than 60s ??
                    if ( (DateTime.Now.TimeOfDay.TotalSeconds - _motionsList[i].motionDateTime.TimeOfDay.TotalSeconds) > 60 ) {
                        // release hires images
                        if ( _motionsList[i].imageMotion != null ) {
                            _motionsList[i].imageMotion.Dispose();
                            _motionsList[i].imageMotion = null;
                        }
                        // lores images
                        if ( Settings.DebugNonConsecutives ) {
                            // save an release
                            if ( _motionsList[i].imageProc != null ) {
                                string pathNonC = System.IO.Path.GetDirectoryName(_motionsList[i].fileNameProc);
                                pathNonC = pathNonC.Substring(0, pathNonC.Length - 4) + "nonc";
                                string fileNonC = System.IO.Path.GetFileName(_motionsList[i].fileNameProc);
                                System.IO.Directory.CreateDirectory(pathNonC);
                                _motionsList[i].imageProc.Save(System.IO.Path.Combine(pathNonC, fileNonC), System.Drawing.Imaging.ImageFormat.Jpeg);
                                _motionsList[i].imageProc.Dispose();
                                _motionsList[i].imageProc = null;
                            }
                        } else {
                            // release only
                            if ( _motionsList[i].imageProc != null ) {
                                _motionsList[i].imageProc.Dispose();
                                _motionsList[i].imageProc = null;
                            }
                        }
                    }
                }

                // try to restart Telegram, if it should run but it doesn't due to an internal fail
                if ( Settings.UseTelegramBot && _Bot == null ) {
                    Logger.logTextLnU(DateTime.Now, "timerFlowControl_Tick: Telegram restart");
                    _telegramOnErrorCount = 0;
                    _Bot = new TeleSharp.TeleSharp(Settings.BotAuthenticationToken);
                    _Bot.OnMessage += OnMessage;
                    _Bot.OnError += OnError;
                    _Bot.OnLiveTick += OnLiveTick;
                }

                // EXPERIMENTAL: check for a gradual image brightness change and adjust camera exposure time accordingly
                if ( Settings.ExposureByApp ) {
                    // get brightness change over time
                    double brightnessChange = GrayAvgBuffer.GetSlope();
                    // brightness change shall exceed an empirical threshold
                    if ( _brightnessNoChangeCounter >= 4 ) {
                        _brightnessNoChangeCounter = 0;
                        Logger.logTextLn(DateTime.Now, string.Format("timerFlowControl_Tick: no brightness change detected {0:0.###} vs. {1}", brightnessChange, BRIGHTNESS_CHANGE_THRESHOLD));
                    }
                    if ( Math.Abs(brightnessChange) < BRIGHTNESS_CHANGE_THRESHOLD ) {
                        _brightnessNoChangeCounter++;
                        return;
                    }
                    // get current exposure time
                    int currValue;
                    bool success = getCameraExposureTime(out currValue);
                    if ( !success ) {
                        Logger.logTextLn(DateTime.Now, "timerFlowControl_Tick: no current exposure time returned");
                        return;
                    }
                    // distinguish between images got brighter vs. darker
                    int changeValue = currValue;
                    if ( brightnessChange < 0 ) {
                        changeValue++;
                    } else {
                        changeValue--;
                    }
                    // set new exposure time
                    int newValue;
                    success = setCameraExposureTime(changeValue, out newValue);
                    if ( !success ) {
                        Logger.logTextLn(DateTime.Now, "timerFlowControl_Tick: no new exposure time returned");
                        return;
                    }
                    // reset brightness monitor history
                    GrayAvgBuffer.ResetData();
                    // update UI
                    _brightnessNoChangeCounter = 0;
                    Logger.logTextLn(DateTime.Now, string.Format("timerFlowControl_Tick: camera exposure time old={0} new={1}", currValue, newValue));
                    updateUiCameraProperties();
                }

            }

            // only care, if making a daily video is active
            if ( Settings.MakeDailyVideo ) {
                // make video from today's images at 19:00:00: if not done yet AND if today's error count < 5 AND not in progress
                if ( DateTime.Now.TimeOfDay >= _videoTime && !Settings.DailyVideoDone && (_dailyVideoErrorCount < 5) && !_dailyVideoInProgress ) {
                    // generate daily video: a) from single motion images b) from motion sequences afterwards
                    Task.Run(() => { makeMotionVideo(Settings.CameraResolution); });
                    // clear _motionsList for current day
                    for ( int i = 0; i < _motionsList.Count - 1; i++ ) {
                        // dispose hires if existing
                        if ( _motionsList[i].imageMotion != null ) {
                            _motionsList[i].imageMotion.Dispose();
                            _motionsList[i].imageMotion = null;
                        }
                        // dispose lores if existing
                        if ( _motionsList[i].imageProc != null ) {
                            _motionsList[i].imageProc.Dispose();
                            _motionsList[i].imageProc = null;
                        }
                    }
                    _motionsList.Clear();
                    // done   
                    return;
                }
                // prevent 'make video' loops in case of errors
                if ( _dailyVideoErrorCount == 5 ) {
                    _dailyVideoErrorCount = int.MaxValue;
                    Logger.logTextLnU(DateTime.Now, "timerFlowControl_Tick: too many 'make video' errors, giving up for today");
                }
                // some time after 19:00 _dailyVideoDone might become TRUE, it needs to reset after midnight
                if ( Settings.DailyVideoDone && DateTime.Now.TimeOfDay >= _midNight && DateTime.Now.TimeOfDay < _videoTime ) {
                    // reset _dailyVideoDone right after midnight BUT not after 19:00
                    Settings.DailyVideoDone = false;
                    IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                    ini.IniWriteValue("MotionUVC", "DailyVideoDoneForToday", "False");
                    _dailyVideoErrorCount = 0;
                    _dailyVideoInProgress = false;
                    Logger.logTextLnU(DateTime.Now, "timerFlowControl_Tick: reset video done flag at midnight");
                    // sync to motion count from today
                    getTodaysMotionsCounters();
                }
            }

            // only care, if daily reboot of Windows-OS is active
            if ( Settings.RebootDaily ) {
                // timer tick is 30s, so within a range of 60s this condition will be met once for sure
                if ( DateTime.Now.TimeOfDay >= MainForm.BootTimeBeg && DateTime.Now.TimeOfDay <= MainForm.BootTimeEnd ) {
                    Logger.logTextLnU(DateTime.Now, "Now: daily reboot system");
                    // INI: write to ini
                    updateSettingsFromAppProperties();
                    Settings.writePropertyGridToIni();
                    // a planned reboot is not a crash
                    AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                    ini.IniWriteValue("MotionUVC", "AppCrash", "False");
                    // reboot
                    System.Diagnostics.Process.Start("shutdown", "/r /f /y /t 1");    // REBOOT: /f == force if /t > 0; /y == yes to all questions asked 
                }
            }
        }

        // make video sequence from motion/image data stored in mol aka List<Motion> 
        public void makeMotionSequence(List<Motion> mol, Size size) {
            // folder and video file name
            DateTime now = DateTime.Now;
            Logger.logTextLnU(DateTime.Now, "makeMotionSequence: 'on demand' start");
            string nowString = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = System.IO.Path.Combine(Settings.StoragePath, nowString + "_sequ");
            System.IO.Directory.CreateDirectory(path);
            // fileName shall distinguish between full motion sequence and an 'on demand' sequence via Telegram
            string fileName = System.IO.Path.Combine(path, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".avi");
            if ( _alarmSequence && _Bot != null && _sequenceReceiver != null ) {
                fileName = System.IO.Path.Combine(path, now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".avi");
            }
            Accord.Video.FFMPEG.VideoFileWriter writer = null;
            // try to make a motion video sequence
            try {
                // video writer: !! needs both VC_redist.x86.exe and VC_redist.x64.exe installed on target PC !!
                writer = new VideoFileWriter();
                // create new video file
                writer.Open(fileName, size.Width, size.Height, 25, VideoCodec.MPEG4);
                Bitmap image;
                // loop list 
                foreach ( Motion mo in mol ) {
                    if ( mo.motionConsecutive ) {
                        try {
                            if ( mo.motionSaved ) {
                                // if motion is already saved, get bmp from disk
                                image = new Bitmap(mo.fileNameMotion);
                            } else {
                                // if motion is not yet saved, get image bmp from list
                                image = (Bitmap)mo.imageMotion.Clone();
                            }
                            writer.WriteVideoFrame(image);
                            image.Dispose();
                        } catch {
                            continue;
                        }
                    }
                }
                writer.Close();
            } catch ( Exception ex1 ) {
                // update bot status
                if ( _alarmSequence && _Bot != null && _sequenceReceiver != null ) {
                    _Bot.SendMessage(new SendMessageParams {
                        ChatId = _sequenceReceiver.Id.ToString(),
                        Text = "Make video sequence failed."
                    });
                }
                Logger.logTextLnU(DateTime.Now, String.Format("makeMotionSequence ex: {0}", ex1.Message));
                if ( writer != null ) {
                    writer.Close();
                }
                _alarmSequenceBusy = false;
                return;
            }
            // send motion alarm sequence
            if ( _alarmSequence && _Bot != null && _sequenceReceiver != null ) {
                sendVideo(_sequenceReceiver, fileName);
                Logger.logTextLnU(DateTime.Now, "makeMotionSequence: 'on demand' done");
            } else {
                if ( Settings.MakeVideoNow ) {
                    Logger.logTextLnU(DateTime.Now, "makeMotionSequence: 'now sequence' done");
                } else {
                    Logger.logTextLnU(DateTime.Now, "makeMotionSequence: 'daily sequence' done");
                }
            }
            // the busy flag was set in the calling method
            _alarmSequenceBusy = false;
        }
        // make video from today's images  
        public void makeMotionVideo(Size size, MessageSender sender = null) {
            _dailyVideoInProgress = true;
            Logger.logTextLnU(DateTime.Now, "makeMotionVideo: 'make video' start");
            // folder and video file name
            string nowString = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = System.IO.Path.Combine(Settings.StoragePath, nowString);
            System.IO.Directory.CreateDirectory(path);
            string fileName = System.IO.Path.Combine(path, nowString + ".avi");
            Accord.Video.FFMPEG.VideoFileWriter writer = null;
            int excStep = 0;
            try {
                // video writer: !! needs both VC_redist.x86.exe and VC_redist.x64.exe installed on target PC !!
                writer = new VideoFileWriter();
                excStep = 1;
                // create new video file
                excStep = 2;
                writer.Open(fileName, size.Width, size.Height, 25, VideoCodec.MPEG4);
                excStep = 3;
                // folder with images to process
                DirectoryInfo d = new DirectoryInfo(path);
                excStep = 4;
                FileInfo[] Files = d.GetFiles("*.jpg");
                excStep = 5;
                int fileCount = Files.Length;
                excStep = 6;
                int fileError = 0;
                excStep = 7;
                Bitmap image;
                excStep = 8;
                foreach ( FileInfo file in Files ) {
                    try {
                        image = new Bitmap(file.FullName);
                        writer.WriteVideoFrame(image);
                        image.Dispose();
                    } catch {
                        fileError++;
                        continue;
                    }
                }
                // if image files are locked
                if ( fileError == fileCount ) {
                    Logger.logTextLnU(DateTime.Now, "makeMotionVideo: too many file errors");
                    if ( !Settings.MakeVideoNow ) {
                        Settings.DailyVideoDone = false;
                    }
                    Settings.MakeVideoNow = false;
                    _dailyVideoErrorCount++;
                    _dailyVideoInProgress = false;
                    _sendVideo = false;
                    writer.Close();
                    return;
                }
                writer.Close();
            } catch ( Exception ex ) {
                // update bot status
                if ( _sendVideo && _Bot != null && sender != null ) {
                    _Bot.SendMessage(new SendMessageParams {
                        ChatId = sender.Id.ToString(),
                        Text = "Make video failed, try again later."
                    });
                }
                Logger.logTextLnU(DateTime.Now, String.Format("makeMotionVideo ex at step{0}: {1}", excStep, ex.Message));
                if ( !Settings.MakeVideoNow ) {
                    Settings.DailyVideoDone = false;
                }
                Settings.MakeVideoNow = false;
                _dailyVideoErrorCount++;
                _dailyVideoInProgress = false;
                _sendVideo = false;
                if ( writer != null ) {
                    writer.Close();
                }
                return;
            }
            // distinguish regular video (== !_sendVideo) and video on demand (== _sendVideo)
            if ( !_sendVideo ) {
                if ( !Settings.MakeVideoNow ) {
                    // set done flag for making the today's video
                    Settings.DailyVideoDone = true;
                    IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                    ini.IniWriteValue("MotionUVC", "DailyVideoDoneForToday", "True");
                }
            } else {
                // send on demand video
                if ( sender != null ) {
                    sendVideo(sender, fileName);
                }
            }
            Settings.MakeVideoNow = false;
            _dailyVideoErrorCount = 0;
            _dailyVideoInProgress = false;
            Logger.logTextLnU(DateTime.Now, "makeMotionVideo: 'make video' done");
        }
        // prepare to send a video
        void prepareToSendVideo(MessageSender sender) {
            // check today's folder for a existing video file
            string fileName = "";
            if ( Settings.DailyVideoDone ) {
                // folder and video file name
                string nowString = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string path = System.IO.Path.Combine(Settings.StoragePath, nowString);
                System.IO.Directory.CreateDirectory(path);
                fileName = System.IO.Path.Combine(path, nowString + ".avi");
                if ( !System.IO.File.Exists(fileName) ) {
                    fileName = "";
                }
            }
            if ( fileName.Length == 0 ) {
                _Bot.SendMessage(new SendMessageParams {
                    ChatId = sender.Id.ToString(),
                    Text = "Preparing video may take a while ..."
                });
                try {
                    // if no video exists, make one
                    Task.Run(() => { makeMotionVideo(Settings.CameraResolution, sender); });
                } catch ( Exception e ) {
                    Logger.logTextLnU(DateTime.Now, "prepareToSendVideo: " + e.Message);
                    _sendVideo = false;
                }
            } else {
                // if the video exists, send it
                sendVideo(sender, fileName);
            }
        }
        // really send video
        void sendVideo(MessageSender sender, string fileName) {
            if ( _Bot != null ) {
                _Bot.SetCurrentAction(sender, ChatAction.UploadVideo);
                byte[] buffer = System.IO.File.ReadAllBytes(fileName);
                _Bot.SendVideo(sender, buffer, "snapshot", "video");
                Logger.logTextLnU(DateTime.Now, "video sent");
                _sendVideo = false;
            }
        }

        // continuously check network availability: needed for Telegram bot
        System.Net.NetworkInformation.PingReply execPing(string strTestIP) {
            System.Net.NetworkInformation.Ping pinger = new System.Net.NetworkInformation.Ping();
            System.Net.NetworkInformation.PingReply reply = pinger.Send(strTestIP, 10);
            return reply;
        }
        public void doPingLooper(ref bool runPing, ref string strTestIP) {
            int pingFailCounter = 0;
            int stopLogCounter = 0;
            do {
                // execute ping
                System.Net.NetworkInformation.PingReply reply = execPing(strTestIP);
                // two possibilities
                if ( reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success ) {
                    // ping ok
                    Settings.PingOk = true;
                    // notify about previous fails
                    if ( pingFailCounter > 10 ) {
                        Logger.logTextLnU(DateTime.Now, String.Format("ping is ok - after {0} fails", pingFailCounter));
                    }
                    pingFailCounter = 0;
                    if ( stopLogCounter > 0 ) {
                        Logger.logTextLnU(DateTime.Now, "ping is ok - after a long time failing");
                    }
                    stopLogCounter = 0;
                } else {
                    // ping fail
                    Settings.PingOk = false;
                    pingFailCounter++;
                }
                // reboot AFTER 10x subsequent ping fails in 100s 
                if ( (pingFailCounter > 0) && (pingFailCounter % 10 == 0) ) {
                    Logger.logTextLn(DateTime.Now, "network reset after 10x ping fail");
                    bool networkUp = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                    if ( networkUp ) {
                        Logger.logTextLn(DateTime.Now, "network is up, but 10x ping failed");
                        if ( Settings.RebootPingCounter < 3 ) {
                            if ( Settings.RebootPingAllowed ) {
                                Logger.logTextLnU(DateTime.Now, "network is up, but ping fails --> next reboot System");
                                Settings.RebootPingCounter++;
                                AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                                ini.IniWriteValue("MotionUVC", "RebootPingCounter", Settings.RebootPingCounter.ToString());
                                ini.IniWriteValue("MotionUVC", "RebootPingFlagActive", "True");
                                System.Diagnostics.Process.Start("shutdown", "/r /f /y /t 1");    // REBOOT: /f == force if /t > 0; /y == yes to all questions asked 
                            } else {
                                Logger.logTextLnU(DateTime.Now, "network is up, but ping fails --> BUT reboot System is not allowed");
                            }
                        } else {
                            if ( stopLogCounter < 5 ) {
                                Logger.logTextLn(DateTime.Now, "Reboot Counter >= 3 --> no reboot, despite of local network is up");
                                stopLogCounter++;
                            }
                        }
                    } else {
                        if ( Settings.RebootPingCounter < 3 ) {
                            if ( Settings.RebootPingAllowed ) {
                                Logger.logTextLnU(DateTime.Now, "network is down --> next reboot System");
                                Settings.RebootPingCounter++;
                                AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                                ini.IniWriteValue("MotionUVC", "RebootPingCounter", Settings.RebootPingCounter.ToString());
                                ini.IniWriteValue("MotionUVC", "RebootPingFlagActive", "True");
                                System.Diagnostics.Process.Start("shutdown", "/r /f /y /t 1");    // REBOOT: /f == force if /t > 0; /y == yes to all questions asked 
                            } else {
                                Logger.logTextLnU(DateTime.Now, "network is down --> BUT reboot System is not allowed");
                            }
                        } else {
                            if ( stopLogCounter < 5 ) {
                                Logger.logTextLn(DateTime.Now, "Reboot Counter >= 3 --> no reboot, despite of network is down");
                                stopLogCounter++;
                            }
                        }
                    }
                }
                //
                System.Threading.Thread.Sleep(10000);
            } while ( runPing );
        }

        // Telegram connector provides a live tick info, this timer tick shall act, if Telegram live tick info fails multiple time
        private void timerCheckTelegramLiveTick_Tick(object sender, EventArgs e) {
            if ( _Bot != null ) {
                TimeSpan span = DateTime.Now - _connectionLiveTick;
                if ( (span).TotalSeconds > 120 ) {
                    if ( _telegramLiveTickErrorCount > 10 ) {
                        // give up after more than 10 live tick errors and log app restart
                        Logger.logTextLnU(DateTime.Now, String.Format("timerCheckTelegramLiveTick_Tick: Telegram not active for #{0} cycles, now restarting MotionUVC", _telegramLiveTickErrorCount));
                        // restart MotionUVC
                        string exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
                        try {
                            System.Diagnostics.Process.Start(startInfo);
                            this.Close();
                        } catch ( Exception ) {; }
                    } else {
                        // try to restart Telegram, it's not fully reliable - therefore a counter is introduced
                        _telegramLiveTickErrorCount++;
                        Logger.logTextLnU(DateTime.Now, String.Format("timerCheckTelegramLiveTick_Tick: Telegram not active detected, now shut it down #{0}", _telegramLiveTickErrorCount));
                        _Bot.OnMessage -= OnMessage;
                        _Bot.OnError -= OnError;
                        _Bot.OnLiveTick -= OnLiveTick;
                        _Bot.Stop();
                        _Bot = null;
                    }
                }
            }
        }
        // Telegram provides a live tick info
        private void OnLiveTick(DateTime now) {
            _connectionLiveTick = now;
            if ( _telegramLiveTickErrorCount > 0 ) {
                // telegram restart after a live tick fail was successful
                Logger.logTextLnU(DateTime.Now, String.Format("OnLiveTick: Telegram now active after previous fail #{0}", _telegramLiveTickErrorCount));
                _telegramLiveTickErrorCount = 0;
            }
        }
        // Telegram connector detected a connection issue
        private void OnError(bool connectionError) {
            _telegramOnErrorCount++;
            Logger.logTextLnU(DateTime.Now, String.Format("OnError: Telegram connect error {0} {1}", _telegramOnErrorCount, connectionError));
            if ( _Bot != null ) {
                _Bot.OnMessage -= OnMessage;
                _Bot.OnError -= OnError;
                _Bot.OnLiveTick -= OnLiveTick;
                _Bot.Stop();
                _Bot = null;
                Logger.logTextLnU(DateTime.Now, "OnError: Telegram connect error, now shut down");
            } else {
                Logger.logTextLnU(DateTime.Now, "OnError: _Bot == null, but OnError still active");
            }
        }
        // read received Telegram messages to the local bot
        private void OnMessage(TeleSharp.Entities.Message message) {
            // get message sender information
            MessageSender sender = (MessageSender)message.Chat ?? message.From;
            Logger.logTextLnU(DateTime.Now, "'" + message.Text + "'");
            string baseStoragePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            if ( string.IsNullOrEmpty(message.Text) || string.IsNullOrEmpty(baseStoragePath) ) {
                return;
            }
            try {
                if ( !string.IsNullOrEmpty(message.Text) )
                    switch ( message.Text.ToLower() ) {
                        case "/help": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "Valid commands, pick one:\n/hello /help /time /location /video /image /start_notify /stop_notify /start_alarm /stop_alarm"
                                });
                                break;
                            }
                        case "/hello": {
                                string welcomeMessage = $"Welcome {message.From.Username} !{Environment.NewLine}My name is {_Bot.Me.Username}{Environment.NewLine}";
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = welcomeMessage
                                });
                                break;
                            }
                        case "/time": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString()
                                });
                                break;
                            }
                        case "/location": {
                                _Bot.SendLocation(sender, "50.69421", "3.17456");
                                break;
                            }
                        case "/video": {
                                if ( !_sendVideo && !_dailyVideoInProgress ) {
                                    _Bot.SendMessage(new SendMessageParams {
                                        ChatId = sender.Id.ToString(),
                                        Text = "Checking video status ..."
                                    });
                                    _sendVideo = true;
                                    prepareToSendVideo(sender);
                                } else {
                                    if ( _dailyVideoInProgress ) {
                                        _Bot.SendMessage(new SendMessageParams {
                                            ChatId = sender.Id.ToString(),
                                            Text = "The daily video is in progress, try again in a few minutes."
                                        });
                                    } else {
                                        _Bot.SendMessage(new SendMessageParams {
                                            ChatId = sender.Id.ToString(),
                                            Text = "making video is already in progress ..."
                                        });
                                    }
                                }
                                break;
                            }
                        case "/start_alarm": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /start_alarm"
                                });
                                _alarmSequence = true;
                                _sequenceReceiver = sender;
                                break;
                            }
                        case "/stop_alarm": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /stop_alarm"
                                });
                                _alarmSequence = false;
                                _sequenceReceiver = null;
                                break;
                            }
                        case "/start_notify": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /start_notify"
                                });
                                _alarmNotify = true;
                                _notifyReceiver = sender;
                                break;
                            }
                        case "/stop_notify": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /stop_notify"
                                });
                                _alarmNotify = false;
                                _notifyReceiver = null;
                                break;
                            }
                        case "/image": {
                                if ( _currFrame == null ) {
                                    Logger.logTextLnU(DateTime.Now, "image capture not working");
                                    _Bot.SendMessage(new SendMessageParams {
                                        ChatId = sender.Id.ToString(),
                                        Text = "image capture not working",
                                    });
                                    break;
                                }
                                _Bot.SetCurrentAction(sender, ChatAction.UploadPhoto);
                                byte[] buffer = bitmapToByteArray(_currFrame);
                                _Bot.SendPhoto(sender, buffer, "snapshot", "image");
                                Logger.logTextLnU(DateTime.Now, String.Format("image sent to: {0}", sender.Id.ToString()));
                                break;
                            }
                        default: {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = message.Text,
                                });
                                Logger.logTextLnU(DateTime.Now, String.Format("unknown command '{0}' from {1}", message.Text, sender.Id.ToString()));
                                break;
                            }
                    }
            } catch ( Exception ex ) {
                Logger.logTextLnU(DateTime.Now, "EXCEPTION OnMessage: " + ex.Message);
            }
        }

        // get UVC devices into a combo box
        void getCameraBasics() {
            this.devicesCombo.Items.Clear();

            // enumerate video devices
            _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if ( _videoDevices.Count == 0 ) {
                this.devicesCombo.Items.Add("No DirectShow devices found");
                this.devicesCombo.SelectedIndex = 0;
                videoResolutionsCombo.Items.Clear();
                return;
            }

            // loop all UVC devices and add them to combo
            bool currentDeviceDisappeared = true;
            int indexToSelect = 0;
            int ndx = 0;
            foreach ( FilterInfo device in _videoDevices ) {
                this.devicesCombo.Items.Add(device.Name);
                if ( device.MonikerString == Settings.CameraMoniker ) {
                    indexToSelect = ndx;
                    currentDeviceDisappeared = false;
                }
                ndx++;
            }

            // null at 1st enter
            if ( (_videoDevice == null) || currentDeviceDisappeared ) {
                this.devicesCombo.SelectedIndex = indexToSelect; // selecting an index automatically calls devicesCombo_SelectedIndexChanged(..)
            } else {
                // do not reselect camera (+ resolution) at a new camera arrival/departure when camera is already running 
                if ( _videoDevice.IsRunning ) {
                    this.devicesCombo.SelectedIndexChanged -= new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
                    this.devicesCombo.SelectedIndex = indexToSelect;
                    this.devicesCombo.SelectedIndexChanged += new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
                } else {
                    this.devicesCombo.SelectedIndex = indexToSelect;
                }
            }
        }

        // closing the main form
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e) {
            Logger.logTextLnU(DateTime.Now, "MotionUVC closed by user.");

            // IMessageFilter
            Application.RemoveMessageFilter(this);

            // shutdown webserver no matter what
            ImageWebServer.Stop();

            // stop ping looper task
            _runPing = false;

            // stop camera & restore meaningful camera parameters
            if ( (_videoDevice != null) && _videoDevice.IsRunning ) {
                _videoDevice.SignalToStop();
                _videoDevice.NewFrame -= new AForge.Video.NewFrameEventHandler(videoDevice_NewFrame);
                _videoDevice.SetCameraProperty(CameraControlProperty.Exposure, -5, CameraControlFlags.Auto);
                _videoDevice.SetVideoProperty(VideoProcAmpProperty.Brightness, -6, VideoProcAmpFlags.Auto);
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                do {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                } while ( sw.ElapsedMilliseconds < 500 );
                EnableConnectionControls(true);
                sw.Start();
                do {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                } while ( sw.ElapsedMilliseconds < 500 );
            }

            // INI: write to ini
            updateSettingsFromAppProperties();
            Settings.writePropertyGridToIni();
            // if app live cycle comes here, there was no app crash, write such info to ini for next startup log
            AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            ini.IniWriteValue("MotionUVC", "AppCrash", "False");
        }

        // enable/disable camera connection related controls
        private void EnableConnectionControls(bool enable) {
            this.devicesCombo.Enabled = enable;
            this.videoResolutionsCombo.Enabled = enable;
            this.connectButton.Text = enable ? _buttonConnectString : "-- stop --";
        }

        // video device selection was changed
        private void devicesCombo_SelectedIndexChanged(object sender, EventArgs e) {
            if ( _videoDevices.Count != 0 ) {
                _videoDevice = new VideoCaptureDevice(_videoDevices[devicesCombo.SelectedIndex].MonikerString);
                Settings.CameraMoniker = _videoDevices[devicesCombo.SelectedIndex].MonikerString;
                EnumerateSupportedFrameSizes(_videoDevice);
            }
        }

        // collect supported video frame sizes in a combo box
        private void EnumerateSupportedFrameSizes(VideoCaptureDevice videoDevice) {
            this.Cursor = Cursors.WaitCursor;
            this.videoResolutionsCombo.Items.Clear();
            try {
                int indexToSelect = 0;
                int ndx = 0;
                foreach ( VideoCapabilities capabilty in videoDevice.VideoCapabilities ) {
                    string currRes = string.Format("{0} x {1}", capabilty.FrameSize.Width, capabilty.FrameSize.Height);
                    // for unknown reason 'videoDevice.VideoCapabilities' sometimes contains all resolutions of a given camera twice
                    if ( this.videoResolutionsCombo.FindString(currRes) == -1 ) {
                        this.videoResolutionsCombo.Items.Add(currRes);
                    }
                    if ( currRes == String.Format("{0} x {1}", Settings.CameraResolution.Width, Settings.CameraResolution.Height) ) {
                        indexToSelect = ndx;
                    }
                    ndx++;
                }
                if ( videoDevice.VideoCapabilities.Length > 0 ) {
                    this.videoResolutionsCombo.SelectedIndex = indexToSelect;
                }
            } finally {
                this.Cursor = Cursors.Default;
            }
        }

        // camera resolution was changed
        private void videoResolutionsCombo_SelectedIndexChanged(object sender, EventArgs e) {
            // get altered video resolution
            if ( (_videoDevice.VideoCapabilities != null) && (_videoDevice.VideoCapabilities.Length != 0) ) {
                _videoDevice.VideoResolution = _videoDevice.VideoCapabilities[this.videoResolutionsCombo.SelectedIndex];
                Settings.CameraResolution = new Size(_videoDevice.VideoCapabilities[this.videoResolutionsCombo.SelectedIndex].FrameSize.Width, _videoDevice.VideoCapabilities[this.videoResolutionsCombo.SelectedIndex].FrameSize.Height);
            }
        }

        // "Start" button clicked
        private void connectButton_Click(object sender, EventArgs e) {
            // restore pictureBox after 'Big Red Cross' exception
            ResetExceptionState(this.pictureBox);

            if ( (_buttonConnectString == this.connectButton.Text) && (sender != null) ) {
                // no still image anymore
                _stillImage = false;
                // only connect if feasible
                if ( (_videoDevice == null) || (_videoDevice.VideoCapabilities == null) || (_videoDevice.VideoCapabilities.Length == 0) || (this.videoResolutionsCombo.Items.Count == 0) ) {
                    return;
                }
                Logger.logTextLn(DateTime.Now, "connectButton_Click: start camera");
                _videoDevice.VideoResolution = _videoDevice.VideoCapabilities[videoResolutionsCombo.SelectedIndex];
                _videoDevice.Start();
                _videoDevice.NewFrame += new AForge.Video.NewFrameEventHandler(videoDevice_NewFrame);
                _justConnected = true;
                // get camera auto exposure status
                CameraControlFlags flag = getCameraExposureAuto();
                this.buttonAutoExposure.Enabled = !(flag == CameraControlFlags.Auto);
                Settings.ExposureAuto = (flag == CameraControlFlags.Auto);
                // get camera exposure range parameters
                int min, max, step, def, value;
                CameraControlFlags cFlag;
                _videoDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out step, out def, out cFlag);
                this.hScrollBarExposure.Maximum = max;
                this.hScrollBarExposure.Minimum = min;
                this.hScrollBarExposure.SmallChange = step;
                this.hScrollBarExposure.LargeChange = step;
                if ( Settings.ExposureAuto ) {
                    this.hScrollBarExposure.Value = def;
                } else {
                    _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out value, out cFlag);
                    this.hScrollBarExposure.Value = value;
                }
                Settings.ExposureVal = hScrollBarExposure.Value;
                Settings.ExposureMin = min;
                Settings.ExposureMax = max;
                this.toolTip.SetToolTip(this.hScrollBarExposure, "camera exposure time = " + Settings.ExposureVal.ToString() + " (" + this.hScrollBarExposure.Minimum.ToString() + ".." + this.hScrollBarExposure.Maximum.ToString() + ")");
                // prepare for camera exposure time monitoring / adjusting
                GrayAvgBuffer.ResetData();
                // disable camera combos
                EnableConnectionControls(false);
                // minimize app if set
                if ( Settings.DetectMotion && Settings.MinimizeApp && e.Equals(EventArgs.Empty) ) {
                    System.Threading.Timer timer = null;
                    timer = new System.Threading.Timer((obj) => {
                        Invoke(new Action(() => {
                            this.WindowState = FormWindowState.Minimized;
                        }));
                        timer.Dispose();
                    },
                    null, 5000, System.Threading.Timeout.Infinite);
                }
                // init done
                Logger.logTextLn(DateTime.Now, "connectButton_Click: start camera done");
                //
                // NOTE: as soon as camera works -> '_justConnected', the webserver is activated depending on Settings.RunWebserver 
                //
            } else {
                // shutdown webserver no matter what
                ImageWebServer.Stop();
                // disconnect means stop video device
                if ( _videoDevice.IsRunning ) {
                    _videoDevice.NewFrame -= new AForge.Video.NewFrameEventHandler(videoDevice_NewFrame);
                    _videoDevice.SignalToStop();
                }
                // some controls
                EnableConnectionControls(true);
                this.buttonAutoExposure.Enabled = true;
                Logger.logTextLn(DateTime.Now, "connectButton_Click: stop camera done");
            }
        }

        // control screenshot
        public Bitmap takeCtlScreenShot(Control ctl) {
            Point location = new Point();
            Invoke(new Action(() => { location = ctl.PointToScreen(Point.Empty); }));
            Bitmap bmp = new Bitmap(ctl.Width, ctl.Height, PixelFormat.Format32bppArgb);
            using ( Graphics g = Graphics.FromImage(bmp) ) {
                g.CopyFromScreen(location.X, location.Y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }
        // full form screenshot
        public Bitmap thisScreenShot() {
            var form = Form.ActiveForm;
            var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            return bmp;
        }
        // show current snapshot modeless in new window
        private void snapshotButton_Click(object sender, EventArgs e) {
            if ( _origFrame == null ) {
                return;
            }
            try {
                Bitmap snapshotFull = thisScreenShot();
                Bitmap snapshotCtl = takeCtlScreenShot(this.pictureBox);
                SnapshotForm snapshotForm = new SnapshotForm(snapshotFull, snapshotCtl);
                snapshotForm.Show();
            } catch {
                Logger.logTextLnU(DateTime.Now, "snapshotButton_Click: exception");
            }
        }

        // extended camera props dialog
        private void buttonProperties_Click(object sender, EventArgs e) {
            if ( _videoDevice != null ) {
                try {
                    // providing a handle makes the dialog modal, aka UI blocking
                    _videoDevice.DisplayPropertyPage(this.Handle);
                } catch {
                    Logger.logTextLnU(DateTime.Now, "buttonProperties_Click: Cannot connect to camera properties");
                }
                // since the above dialog is modal, the only way to get here, is after the camera property dialog was closed
                updateUiCameraProperties();
            }
        }

        // update the few UI camera controls
        private void updateUiCameraProperties() {
            // get camera auto exposure status
            CameraControlFlags flag = getCameraExposureAuto();
            this.buttonAutoExposure.Enabled = !(flag == CameraControlFlags.Auto);
            Settings.ExposureAuto = (flag == CameraControlFlags.Auto);
            // get camera exposure value
            if ( Settings.ExposureAuto ) {
                // if auto exposure, camera exposure time is def 
                int min, max, step, def;
                _videoDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out step, out def, out flag);
                this.hScrollBarExposure.Value = def;
            } else {
                int value;
                CameraControlFlags controlFlags;
                _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out value, out controlFlags);
                this.hScrollBarExposure.Value = value;
            }
            Settings.ExposureVal = hScrollBarExposure.Value;
            this.toolTip.SetToolTip(this.hScrollBarExposure, "camera exposure time = " + Settings.ExposureVal.ToString() + " (" + this.hScrollBarExposure.Minimum.ToString() + ".." + this.hScrollBarExposure.Maximum.ToString() + ")");
            // needed to update the scroller according to the new value
            this.PerformLayout();
        }

        // force camera to set exposure time to automatic
        private void buttonAutoExposure_Click(object sender, EventArgs e) {
            if ( _videoDevice == null ) {
                return;
            }
            if ( setCameraExposureAuto() != CameraControlFlags.Auto ) {
                Logger.logTextLnU(DateTime.Now, "buttonAutoExposure_Click: Cannot set camera exposure time to automatic.");
            }
            updateUiCameraProperties();
        }

        // force camera to set all its properties to default values
        private void buttonDefaultCameraProps_Click(object sender, EventArgs e) {
            if ( _videoDevice == null ) {
                return;
            }

            // camera props
            int min, max, step, def;
            CameraControlFlags cFlag;
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Exposure, def, CameraControlFlags.Auto);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Focus, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Focus, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Iris, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Iris, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Pan, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Pan, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Roll, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Roll, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Tilt, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Tilt, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Zoom, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Zoom, def, CameraControlFlags.Manual);

            // video props
            VideoProcAmpFlags vFlag;
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.BacklightCompensation, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.BacklightCompensation, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Brightness, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Brightness, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.ColorEnable, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.ColorEnable, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Contrast, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Contrast, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Gain, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Gain, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Gamma, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Gamma, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Hue, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Hue, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Saturation, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Saturation, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Sharpness, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Sharpness, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.WhiteBalance, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.WhiteBalance, def, VideoProcAmpFlags.Auto);

            // update UI
            updateUiCameraProperties();
        }

        // set/get camera to auto exposure time
        private CameraControlFlags setCameraExposureAuto() {
            if ( _videoDevice == null ) {
                return CameraControlFlags.None;
            }
            CameraControlFlags flag;
            int min, max, stp, def;
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out stp, out def, out flag); // only to get def
            _videoDevice.SetCameraProperty(CameraControlProperty.Exposure, def, CameraControlFlags.Auto);                      // set def again  
            _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out def, out flag);                                 // get flag again  
            return flag;
        }
        private CameraControlFlags getCameraExposureAuto() {
            if ( _videoDevice == null ) {
                return CameraControlFlags.None;
            }
            CameraControlFlags flag;
            int intValue;
            _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out intValue, out flag);
            return flag;
        }
        // set/get camera to exposure time
        private bool setCameraExposureTime(int expTime, out int newValue) {
            if ( _videoDevice == null ) {
                newValue = -100;
                return false;
            }
            CameraControlFlags flag;
            int min, max, stp, def;
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out stp, out def, out flag);
            if ( expTime < min ) {
                expTime = min;
            }
            if ( expTime > max ) {
                expTime = max;
            }
            _videoDevice.SetCameraProperty(CameraControlProperty.Exposure, expTime, CameraControlFlags.Manual);
            _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out newValue, out flag);
            if ( newValue > max || newValue < min ) {
                return false;
            }
            return true;
        }
        private bool getCameraExposureTime(out int value) {
            if ( _videoDevice == null ) {
                value = -100;
                return false;
            }
            CameraControlFlags flag;
            int min, max, stp, def;
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out stp, out def, out flag);
            _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out value, out flag);
            if ( value > max || value < min ) {
                return false;
            }
            return true;
        }

        // set camera exposure & brightness manually via UI scrollers
        private void hScrollBarExposure_Scroll(object sender, ScrollEventArgs e) {
            this.buttonAutoExposure.Enabled = true;
            _videoDevice.SetCameraProperty(CameraControlProperty.Exposure, this.hScrollBarExposure.Value, CameraControlFlags.Manual);
            Settings.ExposureVal = hScrollBarExposure.Value;
            Settings.ExposureAuto = false;
            this.toolTip.SetToolTip(this.hScrollBarExposure, "camera exposure time = " + Settings.ExposureVal.ToString() + " (" + this.hScrollBarExposure.Minimum.ToString() + ".." + this.hScrollBarExposure.Maximum.ToString() + ")");
        }

        // EXPERIMENTAL: camera exposure time monitor helper, average brightness of bmp
        public unsafe byte Bmp24bppToGreenAverage(Bitmap bmp) {
            if ( bmp.PixelFormat != PixelFormat.Format24bppRgb ) {
                return 0;
            }
            BitmapData bData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0 = (byte*)bData.Scan0.ToPointer();
            double lenBmp = bmp.Width * bmp.Height;
            int lenBmpFull = bData.Stride * bmp.Height - 3;
            int stepCount = 3 * 100;        // 1% of pixels should be enough
            double collector = 0;
            int divisor = 0;
            for ( int i = 0; i < lenBmpFull; i += stepCount ) {
                divisor++;
                collector += scan0[i + 1];  // just green is faster than real gray 
            }
            bmp.UnlockBits(bData);
            byte avgGreen = (byte)(collector / divisor);
            return avgGreen;
        }

        // EXPERIMENTAL: gray average ring buffer
        static class GrayAvgBuffer {
            static byte[] arr = new byte[3600]; // array with 3600 byte values
            private static int arrNdx = 0;      // active array index  
            private static int arrLevel = 0;    // current array level, could be smaller than length of array
            // set most recent gray value
            public static void SetLatestValue(byte value) {
                arr[arrNdx] = value;
                arrNdx++;
                if ( arrLevel < arr.Length ) {
                    arrLevel++;
                }
                if ( arrNdx >= arr.Length ) {
                    arrNdx = 0;
                }
            }
            // get trend of the last gray averages 
            public static double GetSlope() {
                // source https://classroom.synonym.com/f-value-statistics-6039.html
                double sumX = 0;
                double sumY = 0;
                double sumXxY = 0;
                double sumXsq = 0;
                for ( int i = 0; i < arrLevel; i++ ) {
                    sumXxY += i * arr[i];
                    sumX += i;
                    sumY += arr[i];
                    sumXsq += i * i;
                }
                double A = arrLevel * sumXxY;
                double B = sumX * sumY;
                double C = arrLevel * sumXsq;
                double D = sumX * sumX;
                double m = (A - B) / (C - D);
                return m;
            }
            // reset all history data in array
            public static void ResetData() {
                arr = new byte[3600];
                arrNdx = 0;
                arrLevel = 0;
            }
        }

        // Bitmap ring buffer for 5 images
        static class BmpRingBuffer {
            private static Bitmap[] bmpArr = new Bitmap[] { new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1) };
            private static int bmpNdx = 0;
            // public get & set
            public static Bitmap bmp {
                // always return the penultimate bmp
                get {
                    int prevNdx = bmpNdx - 1;
                    if ( prevNdx < 0 ) {
                        prevNdx = 4;
                    }
                    return bmpArr[prevNdx];
                }
                // override bmp in array and increase array index
                set {
                    bmpArr[bmpNdx].Dispose();
                    bmpArr[bmpNdx] = value;
                    bmpNdx++;
                    if ( bmpNdx > 4 ) {
                        bmpNdx = 0;
                    }
                }
            }
        }

        // camera new frame event handler
        void videoDevice_NewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs) {
            // put recent image into ring buffer
            BmpRingBuffer.bmp = (Bitmap)eventArgs.Frame.Clone();
            // start motion detection after the first received image 
            if ( _justConnected ) {
                new Thread(() => cameraImageGrabber()).Start();
                _justConnected = false;
            }
        }
        // image grabber for motion detection runs independent from camera new frame event to ensure frame rate being exact 2fps
        void cameraImageGrabber() {
            // on first enter flag
            bool firstImageProcessing = true;
            // stopwatch
            System.Diagnostics.Stopwatch swFrameProcessing = new System.Diagnostics.Stopwatch();
            DateTime lastFrameTime = DateTime.Now;
            // init such vars just once 
            Font timestampFont = new Font("Arial", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            int timestampHeight = 0;
            int timestampLength = 0;
            int oneCharStampLength = 0;
            int yFill = 0;
            int yDraw = 0;
            // dispose 'previous image', camera resolution might have changed
            if ( _prevFrame != null ) {
                _prevFrame.Dispose();
                _prevFrame = null;
            }
            // sync to motion count from today
            getTodaysMotionsCounters();

            //
            // loop as long as camera is running
            //
            int excStep = -1;
            while ( _videoDevice.IsRunning ) {

                // calc fps
                DateTime now = DateTime.Now;
                double revFps = (double)(now - lastFrameTime).TotalMilliseconds;
                lastFrameTime = now;
                _fps = 1000.0f / revFps;

                // measure consumed time for image processing
                swFrameProcessing.Restart();

                try {

                    // avoid Exception when GC is too slow
                    excStep = 0;
                    if ( _origFrame != null ) {
                        _origFrame.Dispose();
                    }

                    // get original frame from BmpRingBuffer
                    excStep = 1;
                    _origFrame = (Bitmap)BmpRingBuffer.bmp.Clone();

                    // prepare and add timestamp watermark + motions detected counter
                    if ( firstImageProcessing ) {
                        timestampHeight = _origFrame.Height / 30;
                        timestampFont = new Font("Arial", timestampHeight, FontStyle.Bold, GraphicsUnit.Pixel);
                        timestampHeight += 15;
                        _frameAspectRatio = (double)_origFrame.Width / (double)_origFrame.Height;
                        yFill = _origFrame.Height - timestampHeight;
                        yDraw = yFill + 5;
                        // for later processing scaled images are used 
                        if ( _origFrame.Width > 800 ) {
                            Settings.ScaledImageSize = new Size(800, (int)(800.0f / _frameAspectRatio));
                        } else {
                            Settings.ScaledImageSize = new Size(_origFrame.Width, _origFrame.Height);
                        }
                    }
                    excStep = 2;
                    using ( var graphics = Graphics.FromImage(_origFrame) ) {
                        string text = now.ToString("yyyy.MM.dd HH:mm:ss_fff", System.Globalization.CultureInfo.InvariantCulture);
                        if ( firstImageProcessing ) {
                            timestampLength = (int)graphics.MeasureString(text, timestampFont).Width + 10;
                            oneCharStampLength = (int)((double)timestampLength / 20.0f);
                        }
                        graphics.FillRectangle(Brushes.Yellow, 0, 0, timestampLength, timestampHeight);
                        graphics.DrawString(text, timestampFont, Brushes.Black, 5, 5);
                        text = _motionsDetected.ToString() + "/" + _consecutivesDetected.ToString();
                        int xPos = _origFrame.Width - oneCharStampLength * text.Length - 5;
                        graphics.FillRectangle(Brushes.Yellow, xPos, yFill, _origFrame.Width, _origFrame.Height);
                        graphics.DrawString(text, timestampFont, Brushes.Black, xPos, yDraw);
                    }

                    // motion detector works with a scaled image, typically 800 x 600
                    excStep = 3;
                    if ( _currFrame != null ) {
                        _currFrame.Dispose();
                    }
                    _currFrame = resizeBitmap(_origFrame, Settings.ScaledImageSize);

                    // this will become the processed frame
                    excStep = 4;
                    if ( _procFrame != null ) {
                        _procFrame.Dispose();
                    }
                    _procFrame = (Bitmap)_currFrame.Clone();

                    // make one time sure, there is a previous image
                    excStep = 5;
                    if ( _prevFrame == null ) {
                        _prevFrame = (Bitmap)_currFrame.Clone();
                    }

                    // process image
                    excStep = 6;
                    if ( detectMotion(_currFrame, _prevFrame) ) {
                        _motionsDetected++;
                    }

                    // show current, scaled and processed image in pictureBox, if not minimized
                    excStep = 7;
                    if ( this.WindowState != FormWindowState.Minimized ) {
                        if ( this.pictureBox.Image != null ) {
                            this.pictureBox.Image.Dispose();
                        }
                        excStep = 8;
                        this.pictureBox.Image = (Bitmap)_procFrame.Clone();
                    }

                    // if 1st image processing is done 
                    if ( firstImageProcessing ) {
                        firstImageProcessing = false;
                        // adjust the MainForm canvas matching to the presumable new aspect ratio of the bmp
                        Invoke(new Action(() => { adjustMainFormSize(_frameAspectRatio); }));
                        // handle webserver activity depending of Settings
                        if ( Settings.RunWebserver ) {
                            ImageWebServer.Start();
                        } else {
                            ImageWebServer.Stop();
                        }
                    }

                    // finally make the current frame to the previous frame
                    excStep = 9;
                    _prevFrame.Dispose();
                    excStep = 10;
                    _prevFrame = (Bitmap)_currFrame.Clone();

                    // get process time in ms
                    swFrameProcessing.Stop();
                    _procMs = swFrameProcessing.ElapsedMilliseconds;

                    // update title
                    headLine();

                } catch ( Exception ex ) {
                    Logger.logTextLnU(now, String.Format("cameraImageGrabber: excStep={0} {1}", excStep, ex.Message));
                } finally {
                    // sleep for '500ms - process time' to ensure 2fps
                    System.Threading.Thread.Sleep(Math.Max(0, 500 - (int)_procMs));
                }
            }
        }

        // resize bitmap and keep pixel format
        public static Bitmap resizeBitmap(Bitmap imgToResize, Size size) {
            Bitmap rescaled = new Bitmap(size.Width, size.Height, imgToResize.PixelFormat);
            using ( Graphics g = Graphics.FromImage(rescaled) ) {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;  // significant cpu load decrease 
                g.DrawImage(imgToResize, 0, 0, size.Width, size.Height);
            }
            return rescaled;
        }

        // return RGB pixel data as two boxed (boxDim x boxDim) gray byte arrays
        public unsafe void TwoBmp24bppToGray8bppByteArrayScaledBox(Bitmap bmp_1, out byte[] arr_1, Bitmap bmp_2, out byte[] arr_2, int boxDim) {
            // sanity checks to make sure. both bmp have matching dimensions 
            arr_1 = new byte[1];
            arr_2 = new byte[1];
            if ( bmp_1.PixelFormat != PixelFormat.Format24bppRgb || bmp_2.PixelFormat != PixelFormat.Format24bppRgb ) {
                return;
            }
            if ( (bmp_1.Width != bmp_2.Width) || (bmp_1.Height != bmp_2.Height) ) {
                return;
            }
            // needed later
            uint boxDimSquare = (uint)boxDim * (uint)boxDim;
            // box dimension constraints
            int arrWidth = bmp_1.Width / boxDim;
            int arrHeight = bmp_1.Height / boxDim;
            // adjusted bmp width and height (adjusted values are a multiple to boxDim)
            int bmpWidth = arrWidth * boxDim;
            int bmpFullWidth = bmpWidth * 3;
            int bmpHeight = arrHeight * boxDim;
            // temporary buffers for data of a "new" row after boxDim x boxDim shrinking
            int tmpLen = (int)(bmpWidth / boxDim);
            uint[] tmp_1 = new uint[tmpLen];
            uint[] tmp_2 = new uint[tmpLen];
            // final array size
            arr_1 = new byte[arrWidth * arrHeight];
            arr_2 = new byte[arrWidth * arrHeight];
            int arrNdx = 0;
            // bmp pointers to two Bitmaps
            BitmapData bmpData_1 = bmp_1.LockBits(new Rectangle(0, 0, bmp_1.Width, bmp_1.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0_1 = (byte*)bmpData_1.Scan0.ToPointer();
            BitmapData bmpData_2 = bmp_2.LockBits(new Rectangle(0, 0, bmp_2.Width, bmp_2.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0_2 = (byte*)bmpData_2.Scan0.ToPointer();
            // loop bmp height
            for ( int y = 0; y < bmpHeight; ) {
                // loop b times boxDim rows and increment y with each b increment
                for ( int b = 0; b < boxDim; b++, y++ ) {
                    // offset to data pointer goes with the full width of bmp, aka Stride
                    int scanOfs = y * bmpData_1.Stride;
                    // loop a full row width in x and fill the tmp array along it
                    for ( int x = 0; x < bmpFullWidth; x += 3 ) {
                        // one pixel gray value
                        byte gray1 = (byte)(0.3f * (float)scan0_1[scanOfs + x + 0] + 0.6f * (float)scan0_1[scanOfs + x + 1] + 0.1f * (float)scan0_1[scanOfs + x + 2]);
                        byte gray2 = (byte)(0.3f * (float)scan0_2[scanOfs + x + 0] + 0.6f * (float)scan0_2[scanOfs + x + 1] + 0.1f * (float)scan0_2[scanOfs + x + 2]);
                        // tmpNdx shall alter after one boxDim pixels are collected
                        int tmpNdx = (x / 3) / boxDim;
                        // add gray values of boxDim pixels and store them in tmp
                        tmp_1[(uint)tmpNdx] += gray1;
                        tmp_2[(uint)tmpNdx] += gray2;
                    }
                }
                // now two shrunk rows are ready
                for ( int t = 0; t < tmpLen; t++ ) {
                    // make avg Pixel of a XxX box
                    tmp_1[t] = (tmp_1[t] / boxDimSquare);
                    tmp_2[t] = (tmp_2[t] / boxDimSquare);
                    // get both tmp arrays to buf arrays
                    arr_1[arrNdx] = (byte)tmp_1[t];
                    arr_2[arrNdx] = (byte)tmp_2[t];
                    arrNdx++;
                    // tmp clear
                    tmp_1[t] = 0;
                    tmp_2[t] = 0;
                }
            }
            // unlock Bitmaps
            bmp_1.UnlockBits(bmpData_1);
            bmp_2.UnlockBits(bmpData_2);
        }

        // write boxed (boxDim x boxDim) gray byte array into a larger 24bppRgb color bmp as overlay
        public unsafe Bitmap ScaledBoxGray8bppByteArrayToBmp24bppOverlay(Bitmap ori, Rectangle rcDest, byte[] arr, int boxDim, bool motionDetected) {
            // box dimension constraints
            int arrHeight = rcDest.Height / boxDim;
            int arrWidth = rcDest.Width / boxDim;
            // adjusted rcDest width and height to assure such values are a multiple of boxDim
            int rcDestHeight = arrHeight * boxDim;
            int rcDestWidth = arrWidth * boxDim;
            int rcDestFullWidth = rcDestWidth * 3;
            // scan0 y offset goes with the full ori Bitmap width
            int oriFullWidth = ori.Width * 3;
            // pointer to start in the original Bitmap is determined by the upper left corner of rcDest
            BitmapData bmpData = ori.LockBits(rcDest, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0 = (byte*)bmpData.Scan0.ToPointer();
            // loop over rcDest height
            for ( int y = 0; y < rcDestHeight; y++ ) {
                // y offset for pointer goes with the full ori Bitmap width (Stride), scanOfsY stands always at left border of rcDest
                int scanOfsY = y * bmpData.Stride;
                // offset in arr is changed every 8th row of rcDestHeight
                int arrNdxY = (int)(y / boxDim) * arrWidth;
                // loop over the full length of a row in rcDest
                for ( int x = 0; x < rcDestFullWidth; x += 3 ) {
                    // arr index changes every 8th full column in rcDest
                    int arrNdxX = (int)((x / 3) / boxDim);
                    int arrNdxFin = arrNdxY + arrNdxX;
                    // obtain gray value from box
                    int gray = arr[arrNdxFin];
                    // 255 is an indication for a _roi[i].thresholdIntensity exceed
                    if ( gray == 255 ) {
                        if ( motionDetected ) {
                            // set Pixel in rcDest to transparent red
                            scan0[scanOfsY + x + 2] = 255;
                        } else {
                            // set Pixel in output image to 'sort of white/gray'
                            scan0[scanOfsY + x + 0] = (byte)Math.Min(255, scan0[scanOfsY + x + 0] + 50);
                            scan0[scanOfsY + x + 1] = (byte)Math.Min(255, scan0[scanOfsY + x + 1] + 50);
                            scan0[scanOfsY + x + 2] = (byte)Math.Min(255, scan0[scanOfsY + x + 2] + 50);
                        }
                    }
                }
            }
            ori.UnlockBits(bmpData);
            return ori;
        }

        //
        // motion detector process image method
        //
        bool detectMotion(Bitmap currFrame, Bitmap prevFrame) {

            // camera running w/o motion detection
            if ( !Settings.DetectMotion ) {
                return false;
            }

            // flags
            bool motionDetected = false;
            bool falsePositive = false;
            bool itsDarkOutside = false;

            // we have ROIs, each of them generates a tile out of the two images to compare
            for ( int i = 0; i < ROICOUNT; i++ ) {

                // only use a valid ROI
                if ( i >= _roi.Count ) {
                    break;
                }
                if ( _roi[i].rect.Width <= 0 ) {
                    continue;
                }
                // camera resolution might not fit to ROI
                if ( _roi[i].rect.X + _roi[i].rect.Width > currFrame.Width || _roi[i].rect.Y + _roi[i].rect.Height > currFrame.Height ) {
                    continue;
                }

                // number of pixels in the current tile
                double numberOfPixels = _roi[i].rect.Width * _roi[i].rect.Height;
                double currentPixelsChanged = 0;
                bool motionDetectedInRoi = false;

                // make two Bitmap tiles out of prevFrame and currFrame according to the active ROI
                Bitmap prevTile = prevFrame.Clone(_roi[i].rect, PixelFormat.Format24bppRgb);
                Bitmap currTile = currFrame.Clone(_roi[i].rect, PixelFormat.Format24bppRgb);

                // if reference roi
                if ( _roi[i].reference ) {
                    //  get the average gray value of the current tile
                    byte avgGrayCurr = Bmp24bppToGreenAverage(currTile);
                    // day / night flag
                    itsDarkOutside = (bool)(avgGrayCurr < 50);
                    // app could adjust camera exposure time by itself (fixes camera OV5640 with IR lens: sometimes tends to brightness jumps if ambient is very bright)
                    if ( Settings.ExposureByApp ) {
                        // store it in a buffer for further inspection
                        GrayAvgBuffer.SetLatestValue(avgGrayCurr);
                    }
                }

                // two tiles to compare, now as _roi[i].boxScaler x _roi[i].boxScaler boxed byte buffers for easy comparison
                TwoBmp24bppToGray8bppByteArrayScaledBox(prevTile, out byte[] prevBuf, currTile, out byte[] currBuf, _roi[i].boxScaler);

                // sanity check
                if ( currBuf.Length <= 1 ) {
                    continue;
                }

                // build a resulting buffer
                byte[] bufResu = new byte[currBuf.Length];

                // loop thru both tile buffers to compare pixel by pixel (which is actually the avg of a _roi[i].boxScaler box of pixels)
                for ( int pix = 0; pix < currBuf.Length; pix++ ) {
                    // simple difference of the two input tiles in a certain _roi[i].boxScaler box
                    bufResu[pix] = (byte)Math.Abs((int)currBuf[pix] - (int)prevBuf[pix]);
                    // the following threshold (must be larger than noise) decides, whether the pixel turns white/red or not
                    if ( bufResu[pix] >= _roi[i].thresholdIntensity ) {
                        // result image shall contain changes between the two above tile in red color (or white color, if pixel percent threshold is not reached)
                        bufResu[pix] = 255;
                        currentPixelsChanged++;
                    }
                }

                // check whether the change is considered as a motion
                currentPixelsChanged *= (_roi[i].boxScaler * _roi[i].boxScaler);
                currentPixelsChanged /= numberOfPixels;

                // if the change is a motion
                if ( currentPixelsChanged > _roi[i].thresholdChanges ) {
                    // motion detected inside active ROI
                    motionDetectedInRoi = true;
                    // return value indicates, a motion took place
                    motionDetected = true;
                    // false positive motion, if reference ROI detects a motion
                    if ( _roi[i].reference ) {
                        falsePositive = true;
                    }
                }

                // draw bufResu into _procFrame, which is shown in UI
                _procFrame = ScaledBoxGray8bppByteArrayToBmp24bppOverlay(_procFrame, _roi[i].rect, bufResu, _roi[i].boxScaler, motionDetectedInRoi);

                // show the currently affected ROI
                using ( Graphics g = Graphics.FromImage(_procFrame) ) {
                    g.DrawRectangle(_roi[i].reference ? new Pen(Color.Yellow) : new Pen(Color.Red), _roi[i].rect);
                }

                // release the two Bitmap tiles
                prevTile.Dispose();
                currTile.Dispose();

            } // end of loop ROIs for motion detection

            // image to show on webserver
            if ( Settings.RunWebserver && ImageWebServer.IsRunning ) {
                ImageWebServer.Image = (Settings.WebserverImage == AppSettings.WebserverImageType.PROCESS) ? (Bitmap)_procFrame.Clone() : (Bitmap)_currFrame.Clone();
            }

            // save motions needs useful names based on timestamps
            DateTime nowFile = DateTime.Now;
            DateTime nowPath = DateTime.Now;
            string nowStringFile = nowFile.ToString("yyyy-MM-dd-HH-mm-ss_fff", CultureInfo.InvariantCulture);
            string nowStringPath = nowPath.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // if video will be generated, images captured between 19:00 ... 24:00 will be saved into the next day's folder
            if ( Settings.MakeDailyVideo ) {
                if ( DateTime.Now.TimeOfDay >= new System.TimeSpan(19, 0, 0) ) {
                    // jump one day forward
                    nowPath = nowPath.AddDays(1);
                    nowStringPath = nowPath.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }

            // false positive handling
            if ( falsePositive ) {
                // false positive images are no motion images
                motionDetected = false;
                // save lores fully processed false positive file for debug purposes
                if ( Settings.DebugFalsePositiveImages ) {
                    Task.Run(() => {
                        try {
                            // filename based on current time stamp
                            string path = System.IO.Path.Combine(Settings.StoragePath, nowStringPath + "_false");
                            string fileName = System.IO.Path.Combine(path, nowStringFile + ".jpg");
                            // storage directory
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fileName));
                            // save the 'false positive image'
                            Bitmap tmp = (Bitmap)_procFrame.Clone();
                            tmp.Save(fileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                            tmp.Dispose();
                        } catch ( Exception ex ) {
                            string msg = ex.Message;
                        }
                    });
                }
            }

            // a motion was detected
            if ( motionDetected ) {

                // save hires & lores images
                if ( Settings.SaveMotion || Settings.SaveSequences || _alarmSequence ) {
                    try {
                        // storage directory is built from nowStringPath, which already takes care about image save >19:00 into the next day folder
                        string filePath = System.IO.Path.Combine(Settings.StoragePath, nowStringPath);
                        System.IO.Directory.CreateDirectory(filePath);
                        // build filename from nowStringPath + simple time stamp
                        string fileName = System.IO.Path.Combine(filePath, nowStringFile + ".jpg");
                        // in case of debug lores, filename is based on current time stamp, yet no need to create path
                        string pathDbg = System.IO.Path.Combine(Settings.StoragePath, nowStringPath + "_proc");
                        string fileNameDbg = System.IO.Path.Combine(pathDbg, nowStringFile + ".jpg");

                        // save current motion directly OR if it's dark outside (makes sure, nightly single events are saved)
                        bool motionSaved = false;
                        if ( Settings.SaveMotion || itsDarkOutside ) {
                            // save hires
                            Task.Run(() => {
                                _origFrame.Save(fileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                            });
                            // save lores fully processed file for debug purposes
                            if ( Settings.DebugProcessImages ) {
                                Task.Run(() => {
                                    try {
                                        // storage directory
                                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fileNameDbg));
                                        // save lores image
                                        Bitmap tmp = (Bitmap)_procFrame.Clone();
                                        tmp.Save(fileNameDbg, System.Drawing.Imaging.ImageFormat.Jpeg);
                                        tmp.Dispose();
                                    } catch ( Exception ex ) {
                                        string msg = ex.Message;
                                    }
                                });
                            }
                            // set flag
                            motionSaved = true;
                        }
                        
                        // consider motion sequence
                        if ( Settings.SaveSequences || _alarmSequence ) {

                            // save 'motion sequence data' either to list or add an info entry depending on 'motion save status'
                            if ( !motionSaved ) {
                                _motionsList.Add(new Motion(fileName, nowFile, (Bitmap)_origFrame.Clone(), fileNameDbg, Settings.DebugProcessImages ? (Bitmap)_procFrame.Clone() : null));
                            } else {
                                _motionsList.Add(new Motion(fileName, nowFile));
                            }

                            // need to wait for at least 3 queued images to allow some time comparison between the list entries
                            if ( _motionsList.Count > 2 ) {

                                // calc time differences: last to 3rd to last, last to penultimate, penultimate to 3rd to last
                                double lastToThrd = _motionsList[_motionsList.Count - 1].motionDateTime.TimeOfDay.TotalSeconds - _motionsList[_motionsList.Count - 3].motionDateTime.TimeOfDay.TotalSeconds;
                                double lastToPenu = _motionsList[_motionsList.Count - 1].motionDateTime.TimeOfDay.TotalSeconds - _motionsList[_motionsList.Count - 2].motionDateTime.TimeOfDay.TotalSeconds;
                                double penuToThrd = _motionsList[_motionsList.Count - 2].motionDateTime.TimeOfDay.TotalSeconds - _motionsList[_motionsList.Count - 3].motionDateTime.TimeOfDay.TotalSeconds;
                                
                                // check if the current motion happened within certain time intervals to previous motions --> 'sequence'
                                if ( (lastToThrd < 2.5f) || ((lastToPenu < 1.5f) && (penuToThrd < 1.5f)) ) {

                                    // make the last three motions consecutive
                                    _motionsList[_motionsList.Count - 3].motionConsecutive = true;
                                    _motionsList[_motionsList.Count - 2].motionConsecutive = true;
                                    _motionsList[_motionsList.Count - 1].motionConsecutive = true;

                                    // save a consecutive image to disk (only @ 1st enter it's a sequence of three images)
                                    saveSequence();

                                    // make a motion sequence video only in this specific case
                                    if ( _alarmSequence ) {
                                        // fire & forget is ok
                                        Task.Run(() => {
                                            // make a sub list containing the latest consecutive motions
                                            _alarmSequenceBusy = true;
                                            List<Motion> subList = new List<Motion>();
                                            for ( int i = _motionsList.Count - 1; i >= 0; i-- ) {
                                                if ( _motionsList[i].motionConsecutive ) {
                                                    subList.Insert(0, _motionsList[i]);
                                                } else {
                                                    break;
                                                }
                                            }
                                            // don't continue, if subList is too small = have at least a 3s motion sequence (6 motions); less motions will be picked up by timer flow control with some delay
                                            if ( subList.Count < 7 ) {
                                                _alarmSequenceBusy = false;
                                                return;
                                            }
                                            // prevent to send the current motion sequence again, by placing two stoppers into motion list
                                            _motionsList.Add(new Motion("", new DateTime(1900, 01, 01)));
                                            _motionsList.Add(new Motion("", new DateTime(1900, 01, 01)));
                                            // make latest motion video sequence, send it via Telegram and reset flag _alarmSequenceBusy when done
                                            makeMotionSequence(subList, Settings.CameraResolution);
                                        });
                                    }
                                }
                            }
                        }
                    } catch ( Exception ex ) {
                        string msg = ex.Message;
                        Logger.logTextLnU(DateTime.Now, "image save ex:" + msg);
                    }
                }

                // send motion alarm photo to Telegram
                if ( _alarmNotify ) {
                    Task.Run(() => {
                        try {
                            // all alarm images are sent
                            _Bot.SetCurrentAction(_notifyReceiver, ChatAction.UploadPhoto);
                            byte[] buffer = bitmapToByteArray(_origFrame);
                            _Bot.SendPhoto(_notifyReceiver, buffer, "alarm", "alarm photo");
                            Logger.logTextLn(DateTime.Now, "alarm photo sent");
                        } catch ( Exception ex ) {
                            string msg = ex.Message;
                            Logger.logTextLnU(DateTime.Now, msg);
                        }
                    });
                }
            }

            // return assessment regarding motion detection
            return motionDetected;
        }

        // supposed to save not yet saved motion images, if they are consecutive
        private void saveSequence() {
            // loop list
            for ( int i = _motionsList.Count - 1; i >= 0; i-- ) {
                // debug non consecutive images
                if ( Settings.DebugNonConsecutives ) {
                    if ( !_motionsList[i].motionConsecutive ) {
                        // save lores if existing
                        if ( _motionsList[i].imageProc != null ) {
                            string pathNonC = System.IO.Path.GetDirectoryName(_motionsList[i].fileNameProc);
                            pathNonC = pathNonC.Substring(0, pathNonC.Length - 4) + "nonc";
                            string fileNonC = System.IO.Path.GetFileName(_motionsList[i].fileNameProc);
                            System.IO.Directory.CreateDirectory(pathNonC);
                            _motionsList[i].imageProc.Save(System.IO.Path.Combine(pathNonC, fileNonC), System.Drawing.Imaging.ImageFormat.Jpeg);
                            _motionsList[i].imageProc.Dispose();
                            _motionsList[i].imageProc = null;
                        }
                    }
                }
                // only consider existing images
                if ( _motionsList[i].imageMotion != null ) {
                    // further checks
                    if ( _motionsList[i].motionConsecutive && !_motionsList[i].motionSaved ) {
                        // save to disk may take some time
                        try {
                            // save hires, inc counter, set 'save flag' & dispose
                            _motionsList[i].imageMotion.Save(_motionsList[i].fileNameMotion, System.Drawing.Imaging.ImageFormat.Jpeg);
                            _consecutivesDetected++;
                            _motionsList[i].motionSaved = true;
                            _motionsList[i].imageMotion.Dispose();
                            _motionsList[i].imageMotion = null;
                            // save lores if existing
                            if ( _motionsList[i].imageProc != null ) {
                                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_motionsList[i].fileNameProc));
                                _motionsList[i].imageProc.Save(_motionsList[i].fileNameProc, System.Drawing.Imaging.ImageFormat.Jpeg);
                                _motionsList[i].imageProc.Dispose();
                                _motionsList[i].imageProc = null;
                            }
                        } catch ( Exception ex ) {
                            Logger.logTextLnU(DateTime.Now, "saveSequence ex: " + ex.Message);
                        }
                    } else {
                        // applies to existing, but already saved images - ideally this should not happen 
                        _motionsList[i].imageMotion.Dispose();
                        _motionsList[i].imageMotion = null;
                        if ( _motionsList[i].imageProc != null ) {
                            _motionsList[i].imageProc.Dispose();
                            _motionsList[i].imageProc = null;
                        }
                    }
                } else {
                    break;
                }
            }
        }

        // supposed to reset an exception state, sometimes needed for pictureBox and 'red cross exception' <-- perhaps not needed when pictureBox is subclassed with try/catch OnPaint
        void ResetExceptionState(Control control) {
            typeof(Control).InvokeMember("SetState", System.Reflection.BindingFlags.NonPublic |
                                                     System.Reflection.BindingFlags.InvokeMethod |
                                                     System.Reflection.BindingFlags.Instance,
                                                     null,
                                                     control,
                                                     new object[] { 0x400000, false });
        }

        // TBD something useful
        const int WM_KEYDOWN = 0x100;
        const int WM_SYSKEYDOWN = 0x105;
        public bool PreFilterMessage(ref System.Windows.Forms.Message m)     // IMessageFilter: intercept messages
        {
            // 'Alt-P' is the magic key combination
            if ( (ModifierKeys == Keys.Alt) && ((Keys)m.WParam == Keys.P) && (m.Msg == WM_SYSKEYDOWN) ) {
                return false;
            }
            // in case to add something useful
            if ( m.Msg == WM_KEYDOWN ) {
                // <ESC> 
                if ( (Keys)m.WParam == Keys.Escape ) {
                }
            }
            return false;
        }

        // return a Bitmap as byte array
        byte[] bitmapToByteArray(Bitmap bmp) {
            using ( MemoryStream memoryStream = new MemoryStream() ) {
                bmp.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                return memoryStream.ToArray();
            }
        }

        // MainForm resize: match MainForm size to aspect ratio of pictureBox
        void adjustMainFormSize(double aspectRatioBmp) {
            // PFM offset
            int toolbarOfs = this.Height - this.pictureBox.Height - 12;
            // what dimension is the driver for the size change?
            int deltaWidth = _sizeBeforeResize.Width - this.Size.Width;
            int deltaHeight = _sizeBeforeResize.Height - this.Size.Height;
            if ( Math.Abs(deltaWidth) > Math.Abs(deltaHeight) ) {
                // keep width
                this.Size = new Size(this.Width, (int)((double)this.Width / aspectRatioBmp) + toolbarOfs);
            } else {
                // keep height
                this.Size = new Size((int)((double)(this.Height - toolbarOfs) * aspectRatioBmp), this.Height);
            }
            this.Invalidate();
            this.Update();
        }
        private void MainForm_ResizeBegin(object sender, EventArgs e) {
            _sizeBeforeResize = this.Size;
        }
        private void MainForm_Resize(object sender, EventArgs e) {
            try {
                if ( this.WindowState != FormWindowState.Minimized ) {
                    adjustMainFormSize(_frameAspectRatio);
                    headLine();
                }
            } catch ( System.InvalidOperationException ioe ) {
                ;
            } finally {
            }
        }
        private void MainForm_ResizeEnd(object sender, EventArgs e) {
            _sizeBeforeResize = this.Size;
        }

        // update title bar info
        void headLine() {
            Invoke(new Action(() => {
                try {
                    this.Text = String.Format("MotionUVC - {0}ms @{1:0.0}fps", _procMs, _fps);
                } catch ( Exception ex ) {
                    this.Text = "headLine() Exception " + ex.Message;
                }
            }));
        }

        // show "about" in system menu
        const int WM_DEVICECHANGE = 0x0219;
        const int WM_SYSCOMMAND = 0x112;
        [DllImport("user32.dll")]
        private static extern int GetSystemMenu(int hwnd, int bRevert);
        [DllImport("user32.dll")]
        private static extern int AppendMenu(int hMenu, int Flagsw, int IDNewItem, string lpNewItem);
        private void SetupSystemMenu() {
            // get handle to app system menu
            int menu = GetSystemMenu(this.Handle.ToInt32(), 0);
            // add a separator
            AppendMenu(menu, 0xA00, 0, null);
            // add items with unique message ID
            AppendMenu(menu, 0, 1236, "Loupe");
            AppendMenu(menu, 0, 1235, "Still Image");
            AppendMenu(menu, 0, 1234, "About MotionUVC");
        }
        protected override void WndProc(ref System.Windows.Forms.Message m) {
            // something happened to USB, not clear whether camera or something else
            if ( m.Msg == WM_DEVICECHANGE ) {
                getCameraBasics();
            }

            // WM_SYSCOMMAND is 0x112
            if ( m.Msg == WM_SYSCOMMAND ) {
                // loupe
                if ( m.WParam.ToInt32() == 1236 ) {
                    Loupe.Loupe lp = new Loupe.Loupe();
                    lp.StartPosition = FormStartPosition.Manual;
                    lp.Location = new Point(this.Location.X - lp.Width - 5, this.Location.Y + 5);
                    lp.Show(this);
                }
                // open a still image
                if ( m.WParam.ToInt32() == 1235 ) {
                    if ( _videoDevice != null && _videoDevice.IsRunning ) {
                        MessageBox.Show("Stop the running camera prior to open a still image.", "Note");
                        return;
                    }
                    OpenFileDialog of = new OpenFileDialog();
                    of.InitialDirectory = Application.StartupPath;
                    of.Filter = "All Files|*.*|JPeg Image|*.jpg";
                    DialogResult result = of.ShowDialog();
                    if ( result != DialogResult.OK ) {
                        EnableConnectionControls(true);
                        return;
                    }
                    try {
                        _origFrame = new Bitmap(of.FileName);
                        double ar = (double)_origFrame.Width / (double)_origFrame.Height;
                        int height = (int)(800.0f / ar);
                        _currFrame = resizeBitmap(_origFrame, new Size(800, height));
                        _procFrame = (Bitmap)_currFrame.Clone();
                        _prevFrame = (Bitmap)_currFrame.Clone();
                        // reset zoom & pan
                        _eOld = new System.Windows.Point(-1, -1);
                        _stillImage = true;
                        _iScaleStep = 0;
                        _mouseDown = new System.Drawing.Point();
                        _iRect = new System.Windows.Rect(0, 0, _origFrame.Width, _origFrame.Height);
                        // show image in picturebox
                        this.pictureBox.Image = _origFrame;
                    } catch (Exception e) {
                        MessageBox.Show(e.Message, "Error"); 
                    }
                }
                // show About box: check for added menu item's message ID
                if ( m.WParam.ToInt32() == 1234 ) {
                    // show About box here...
                    AboutBox dlg = new AboutBox();
                    dlg.ShowDialog();
                    dlg.Dispose();
                }
            }

            // it is essential to call the base behavior
            base.WndProc(ref m);
        }

        // delete avi files
        void deleteAviFiles(string homeFolder, long gainedSpace) {
            // get avi-files in homeFolder and have the file array sorted by last write time in ascending order
            System.IO.DirectoryInfo di = new System.IO.DirectoryInfo(homeFolder);
            System.IO.FileInfo[] fiArr = di.GetFiles("*.avi").OrderBy(p => p.LastWriteTime).ToArray();
            // delete oldest avi-files until gainedSpace is achieved as goal (could mean all of them)
            long deletedSpace = 0;
            foreach ( System.IO.FileInfo fi in fiArr ) {
                try {
                    deletedSpace += fi.Length;
                    fi.Delete();
                } catch ( Exception ) {; }
                if ( deletedSpace > gainedSpace ) {
                    break;
                }
            }
        }
        // delete oldest image folder
        void deleteOldestImageFolder(string homeFolder) {
            FileSystemInfo fileInfo = new DirectoryInfo(homeFolder).GetFileSystemInfos().OrderBy(fi => fi.CreationTime).First();
            Directory.Delete(fileInfo.FullName, true);
        }

        // PInvoke for Windows API function GetDiskFreeSpaceEx
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);
        public static long driveFreeBytes(string folderName) {
            long freespace = -1;
            if ( string.IsNullOrEmpty(folderName) ) {
                return freespace;
            }
            if ( !folderName.EndsWith("\\") ) {
                folderName += '\\';
            }
            ulong free = 0, dummy1 = 0, dummy2 = 0;
            if ( GetDiskFreeSpaceEx(folderName, out free, out dummy1, out dummy2) ) {
                freespace = (long)free;
                return freespace;
            } else {
                return freespace;
            }
        }

        // manually call to app settings: PropertyGrid dialog
        private void buttonSettings_Click(object sender, EventArgs e) {
            // transfer current app settings to Settings class
            updateSettingsFromAppProperties();
            // start settings dialog
            Settings dlg = new Settings(Settings);
            // memorize settings
            AppSettings oldSettings;
            Settings.CopyAllTo(Settings, out oldSettings);
            if ( dlg.ShowDialog() == DialogResult.OK ) {
                // get changed values back from PropertyGrid settings dlg
                Settings = dlg.Setting;
                // update app settings
                updateAppPropertiesFromSettings();
                // INI: write settings to ini
                Settings.writePropertyGridToIni();
            } else {
                Settings.CopyAllTo(oldSettings, out Settings);
            }
        }

        // allow panel with disabled controls to show tooltips
        private void tableLayoutPanel_MouseHover(object sender, EventArgs e) {
            Point pt = ((TableLayoutPanel)sender).PointToClient(Control.MousePosition);
            try {
                TableLayoutPanelCellPosition pos = GetCellPosition((TableLayoutPanel)sender, pt);
                Control c = ((TableLayoutPanel)sender).GetControlFromPosition(pos.Column, pos.Row);
                if ( c != null ) {
                    string tt = this.toolTip.GetToolTip(c);
                    toolTip.Show(tt, (TableLayoutPanel)sender, pt, 500);
                }
            } catch {;} 
        }
        // TableLayoutPanel cell position under the mouse: https://stackoverflow.com/questions/39040847/show-text-when-hovering-over-cell-in-tablelayoutpanel-c-sharp 
        private TableLayoutPanelCellPosition GetCellPosition(TableLayoutPanel panel, Point p) {
            // cell position
            TableLayoutPanelCellPosition pos = new TableLayoutPanelCellPosition(0, 0);
            // panel size
            Size size = panel.Size;
            // get the cell row y coordinate
            float y = 0;
            for ( int i = 0; i < panel.RowCount; i++ ) {
                // calculate the sum of the row heights.
                SizeType type = panel.RowStyles[i].SizeType;
                float height = panel.RowStyles[i].Height;
                switch ( type ) {
                    case SizeType.Absolute:
                        y += height;
                        break;
                    case SizeType.Percent:
                        y += height / 100 * size.Height;
                        break;
                    case SizeType.AutoSize:
                        SizeF cellAutoSize = new SizeF(size.Width / panel.ColumnCount, size.Height / panel.RowCount);
                        y += cellAutoSize.Height;
                        break;
                }
                // check the mouse position to decide if the cell is in current row.
                if ( (int)y > p.Y ) {
                    pos.Row = i;
                    break;
                }
            }
            // get the cell column x coordinate
            float x = 0;
            for ( int i = 0; i < panel.ColumnCount; i++ ) {
                // calculate the sum of the row widths
                SizeType type = panel.ColumnStyles[i].SizeType;
                float width = panel.ColumnStyles[i].Width;
                switch ( type ) {
                    case SizeType.Absolute:
                        x += width;
                        break;
                    case SizeType.Percent:
                        x += width / 100 * size.Width;
                        break;
                    case SizeType.AutoSize:
                        SizeF cellAutoSize = new SizeF(size.Width / panel.ColumnCount, size.Height / panel.RowCount);
                        x += cellAutoSize.Width;
                        break;
                }
                // check the mouse position to decide if the cell is in current column
                if ( (int)x > p.X ) {
                    pos.Column = i;
                    break;
                }
            }
            // return the mouse position
            return pos;
        }

    }

    // app settings
    public class AppSettings {
        // the literal name of the ini section
        private string iniSection = "MotionUVC";

        // show ROIs edit dialog from a property grid
        [Editor(typeof(RoiEditor), typeof(System.Drawing.Design.UITypeEditor))]
        class RoiEditor : System.Drawing.Design.UITypeEditor {
            public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return System.Drawing.Design.UITypeEditorEditStyle.Modal;
            }
            public override object EditValue(ITypeDescriptorContext context, System.IServiceProvider provider, object value) {
                System.Windows.Forms.Design.IWindowsFormsEditorService svc = provider.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)) as System.Windows.Forms.Design.IWindowsFormsEditorService;
                // no current image --> no ROIs edit dialog
                if ( MainForm._currFrame != null ) {
                    // ROIs edit dialog
                    using ( DefineROI form = new DefineROI() ) {
                        // set image
                        form.SetImage(MainForm._currFrame);
                        // get ROIs data from PropertyGrid 
                        form.ROIsList = MainForm.Settings.getROIsListFromPropertyGrid();
                        // exec dialog
                        if ( svc.ShowDialog(form) == DialogResult.OK ) {
                            // save dialog ROIs to settings PropertyGrid
                            MainForm.Settings.setPropertyGridToROIsList(form.ROIsList);
                        }
                    }
                } else {
                    MessageBox.Show("First, start camera in main window.");
                }
                return value;
            }
        }

        // custom form to show text inside a property grid
        [Editor(typeof(FooEditor), typeof(System.Drawing.Design.UITypeEditor))]
        class FooEditor : System.Drawing.Design.UITypeEditor {
            public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return System.Drawing.Design.UITypeEditorEditStyle.Modal;
            }
            public override object EditValue(ITypeDescriptorContext context, System.IServiceProvider provider, object value) {
                System.Windows.Forms.Design.IWindowsFormsEditorService svc = provider.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)) as System.Windows.Forms.Design.IWindowsFormsEditorService;
                String foo = value as String;
                if ( svc != null && foo != null ) {
                    using ( FooForm form = new FooForm() ) {
                        form.Value = foo;
                        svc.ShowDialog(form);
                    }
                }
                return value;
            }
        }
        class FooForm : Form {
            private TextBox textbox;
            private Button okButton;
            public FooForm() {
                textbox = new TextBox();
                textbox.Multiline = true;
                textbox.Dock = DockStyle.Fill;
                textbox.WordWrap = false;
                textbox.Font = new Font(FontFamily.GenericMonospace, textbox.Font.Size);
                textbox.ScrollBars = ScrollBars.Both;
                Controls.Add(textbox);
                okButton = new Button();
                okButton.Text = "OK";
                okButton.Dock = DockStyle.Bottom;
                okButton.DialogResult = DialogResult.OK;
                Controls.Add(okButton);
            }
            public string Value {
                get { return textbox.Text; }
                set { textbox.Text = value; }
            }
        }

        // form to start 'make video now'
        [Editor(typeof(ActionButtonVideoEditor), typeof(System.Drawing.Design.UITypeEditor))]
        class ActionButtonVideoEditor : System.Drawing.Design.UITypeEditor {
            public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return System.Drawing.Design.UITypeEditorEditStyle.Modal;
            }
            public override object EditValue(ITypeDescriptorContext context, System.IServiceProvider provider, object value) {
                System.Windows.Forms.Design.IWindowsFormsEditorService svc = provider.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)) as System.Windows.Forms.Design.IWindowsFormsEditorService;
                if ( svc != null ) {
                    MainForm.Settings.MakeVideoNow = false;
                    using ( ActionButton form = new ActionButton("Make motion video now") ) {
                        MainForm.Settings.MakeVideoNow = svc.ShowDialog(form) == DialogResult.OK;
                    }
                }
                return value;
            }
        }
        // form to start 'reboot windows now'
        [Editor(typeof(ActionButtonVideoEditor), typeof(System.Drawing.Design.UITypeEditor))]
        class ActionButtonRebootEditor : System.Drawing.Design.UITypeEditor {
            public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return System.Drawing.Design.UITypeEditorEditStyle.Modal;
            }
            public override object EditValue(ITypeDescriptorContext context, System.IServiceProvider provider, object value) {
                System.Windows.Forms.Design.IWindowsFormsEditorService svc = provider.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)) as System.Windows.Forms.Design.IWindowsFormsEditorService;
                if ( svc != null ) {
                    MainForm.Settings.MakeVideoNow = false;
                    using ( ActionButton form = new ActionButton("Reboot Windows now - are you sure?") ) {
                        if ( svc.ShowDialog(form) == DialogResult.OK ) {
                            // pretend to workflow time tick, that boot time is now
                            DateTime now = DateTime.Now;
                            MainForm.BootTimeBeg = new System.TimeSpan(now.Hour, now.Minute, now.Second);
                            MainForm.BootTimeEnd = new System.TimeSpan(now.Hour, now.Minute + 1, now.Second);
                        }
                    }
                }
                return value;
            }
        }
        // a general action form
        class ActionButton : Form {
            private Label textbox;
            private Button okButton;
            private Button cancelButton;
            public ActionButton(String title) {
                this.FormBorderStyle = FormBorderStyle.FixedSingle;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                textbox = new Label();
                textbox.Location = new System.Drawing.Point(0, 50);
                textbox.Font = new Font("Microsoft Sans Serif", 18, FontStyle.Regular, GraphicsUnit.Point);
                textbox.TextAlign = ContentAlignment.MiddleCenter;
                textbox.Size = new Size(this.Width, 100);
                textbox.Text = title;
                Controls.Add(textbox);
                okButton = new Button();
                okButton.Text = "OK";
                okButton.Location = new System.Drawing.Point(this.ClientSize.Width - 80, this.ClientSize.Height - 60);
                okButton.Size = new Size(60, 25);
                okButton.DialogResult = DialogResult.OK;
                Controls.Add(okButton);
                cancelButton = new Button();
                cancelButton.Location = new System.Drawing.Point(20, this.ClientSize.Height - 60);
                cancelButton.Text = "Cancel";
                cancelButton.Size = new Size(60, 25);
                cancelButton.DialogResult = DialogResult.OK;
                Controls.Add(cancelButton);
            }
        }

        public class FolderNameEditorWithRootFolder : UITypeEditor {
            public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return UITypeEditorEditStyle.Modal;
            }

            public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value) {
                using ( FolderBrowserDialog dlg = new FolderBrowserDialog() ) {
                    dlg.SelectedPath = (string)value;
                    if ( dlg.ShowDialog() == DialogResult.OK )
                        return dlg.SelectedPath;
                }
                return base.EditValue(context, provider, value);
            }
        }

        // make a copy of all class properties
        public void CopyAllTo(AppSettings source, out AppSettings target) {
            target = new AppSettings();
            var type = typeof(AppSettings);
            foreach ( var sourceProperty in type.GetProperties() ) {
                var targetProperty = type.GetProperty(sourceProperty.Name);
                targetProperty.SetValue(target, sourceProperty.GetValue(source, null), null);
            }
            foreach ( var sourceField in type.GetFields() ) {
                var targetField = type.GetField(sourceField.Name);
                targetField.SetValue(target, sourceField.GetValue(source));
            }
        }

        // webserver image type
        public enum WebserverImageType {
            LORES = 0,
            PROCESS = 1
        }

        // define app properties
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public string CameraMoniker { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public Size CameraResolution { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public Size ScaledImageSize { get; set; }
        [CategoryAttribute("Camera")]
        [Description("experimental - let MotionUVC adjust camera exposure time")]
        [ReadOnly(false)]
        public bool ExposureByApp { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public bool ExposureAuto { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int ExposureVal { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int ExposureMin { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int ExposureMax { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int Brightness { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int BrightnessMin { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int BrightnessMax { get; set; }
        [CategoryAttribute("User Interface")]
        [ReadOnly(true)]
        public Size FormSize { get; set; }
        [ReadOnly(true)]

        [CategoryAttribute("User Interface")]
        public Point FormLocation { get; set; }
        [Description("Minimize app at motion detection")]
        [CategoryAttribute("User Interface")]
        [ReadOnly(false)]
        public Boolean MinimizeApp { get; set; }

        [CategoryAttribute("Network")]
        [ReadOnly(true)]
        [Description("Current network status via ping")]
        public Boolean PingOk { get; set; }
        [CategoryAttribute("Network")]
        [ReadOnly(false)]
        [Description("Network test IP address for ping")]
        public string PingTestAddress { get; set; }
        public string PingTestAddressRef;

        [CategoryAttribute("Data Storage")]
        private string storagePath;
        [Description("App storage path: images, ini, logfiles")]
        [CategoryAttribute("Data Storage")]
        [ReadOnly(false)]
        [EditorAttribute(typeof(FolderNameEditorWithRootFolder), typeof(UITypeEditor))]
        public string StoragePath { 
            get {
                return this.storagePath;
            } 
            set {
                this.storagePath = value;
                if ( this.storagePath.Length == 0 ) {
                    return;
                }
                if ( this.storagePath == "\\" ) {
                    AutoMessageBox.Show(String.Format("StoragePath = '{0}' is not regular.", this.storagePath), "Error", 5000);
                    this.storagePath = "";
                    return;
                }
                if ( this.storagePath.IndexOf(":") != 1 ) {
                    AutoMessageBox.Show(String.Format("StoragePath = '{0}' is not valid.", this.storagePath), "Error", 5000);
                    return;
                }
                if ( this.storagePath.IndexOf("\\") != 2 ) {
                    AutoMessageBox.Show(String.Format("StoragePath = '{0}' is not acceptable.", this.storagePath), "Error", 5000);
                    return;
                }
                try {
                    Directory.CreateDirectory(this.storagePath);
                    if ( !this.storagePath.EndsWith("\\") ) {
                        this.storagePath += "\\";
                    }
                } catch ( Exception ) {
                    AutoMessageBox.Show(String.Format("StoragePath = '{0}' is not accessible.", this.storagePath), "Error", 5000);
                }
            } 
        }
        [CategoryAttribute("Data Storage")]
        private string storagePathAlt;
        [CategoryAttribute("Data Storage")]
        [Description("Alternative app storage path, if regular storage path (see above) is full")]
        [ReadOnly(false)]
        [EditorAttribute(typeof(FolderNameEditorWithRootFolder), typeof(UITypeEditor))]
        public string StoragePathAlt {
            get {
                return this.storagePathAlt;
            }
            set {
                this.storagePathAlt = value;
                if ( this.storagePathAlt.Length == 0 ) {
                    return;
                }
                if ( this.storagePathAlt == "\\" ) {
                    AutoMessageBox.Show(String.Format("StoragePathAlt = '{0}' is not regular.", this.storagePathAlt), "Error", 5000);
                    this.storagePathAlt = "";
                    return;
                }
                if ( this.storagePathAlt.IndexOf(":") != 1 ) {
                    AutoMessageBox.Show(String.Format("StoragePathAlt = '{0}' is not valid.", this.storagePathAlt), "Error", 5000);
                    return;
                }
                if ( this.storagePathAlt.IndexOf("\\") != 2 ) {
                    AutoMessageBox.Show(String.Format("StoragePathAlt = '{0}' is not acceptable.", this.storagePathAlt), "Error", 5000);
                    return;
                }
                try {
                    Directory.CreateDirectory(this.storagePathAlt);
                    if ( !this.storagePathAlt.EndsWith("\\") ) {
                        this.storagePathAlt += "\\";
                    }
                } catch ( Exception ) {
                    AutoMessageBox.Show(String.Format("StoragePathAlt = '{0}' is not accessible.", this.storagePathAlt), "Error", 5000);
                }            
            }
        }
        [Description("Free storage space")]
        [CategoryAttribute("Data Storage")]
        [ReadOnly(true)]
        public string FreeStorageSpace { get; set; }
        [Description("App writes to logfile")]
        [CategoryAttribute("Data Storage")]
        [ReadOnly(false)]
        public Boolean WriteLogfile { get; set; }

        [Description("Save processed images, useful for debug purposes")]
        [CategoryAttribute("Debugging")]
        [ReadOnly(false)]
        public Boolean DebugProcessImages { get; set; }
        [Description("Save false positive images, useful for debug purposes")]
        [CategoryAttribute("Debugging")]
        [ReadOnly(false)]
        public Boolean DebugFalsePositiveImages { get; set; }
        [Description("Save non consecutive images, useful for debug purposes")]
        [CategoryAttribute("Debugging")]
        [ReadOnly(false)]
        public Boolean DebugNonConsecutives { get; set; }

        [CategoryAttribute("Motion Save Strategy")]
        [Description("Save motion detection sequences")]
        [ReadOnly(false)]
        public Boolean SaveSequences { get; set; }
        [Description("Save hi resolution motion detection images")]
        [CategoryAttribute("Motion Save Strategy")]
        [ReadOnly(false)]
        public Boolean SaveMotion { get; set; }
        [Description("Auto start motion detection at app start")]
        [CategoryAttribute("Motion Save Strategy")]
        [ReadOnly(false)]
        public Boolean DetectMotion { get; set; }
        [CategoryAttribute("Motion Save Strategy")]
        [Description("Start making video, after closing the Settings dialog")]
        [Editor(typeof(ActionButtonVideoEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string MakeMotionVideoNow { get; set; }
        public bool MakeVideoNow;
        [Description("Make daily video of saved motion images at 19:00")]
        [CategoryAttribute("Motion Save Strategy")]
        [ReadOnly(false)]
        public Boolean MakeDailyVideo { get; set; }
        [Description("Status flag, whether the daily motion video is already generated")]
        [CategoryAttribute("Motion Save Strategy")]
        [ReadOnly(false)]
        public Boolean DailyVideoDone { get; set; }
        [ReadOnly(false)]

        [CategoryAttribute("Reboot Behaviour")]
        [Description("Reboot Windows daily at 00:30")]
        public Boolean RebootDaily { get; set; }
        [CategoryAttribute("Reboot Behaviour")]
        [Description("Reboot Windows now - may take up to 30s")]
        [Editor(typeof(ActionButtonRebootEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string RebootWindowsNow { get; set; }
        [CategoryAttribute("Reboot Behaviour")]
        [Description("Reboot Windows allowed after ping fail > 10 minutes")]
        [ReadOnly(false)]
        public Boolean RebootPingAllowed { get; set; }
        [CategoryAttribute("Reboot Behaviour")]
        [Description("Reboot after ping fail counter")]
        [ReadOnly(false)]
        public int RebootPingCounter { get; set; }

        [CategoryAttribute("Telegram")]
        [Description("Use Telegram bot")]
        [ReadOnly(false)]
        public Boolean UseTelegramBot { get; set; }
        [CategoryAttribute("Telegram")]
        [Description("Telegram bot authentication token")]
        [ReadOnly(false)]
        public string BotAuthenticationToken { get; set; }
        [CategoryAttribute("Telegram")]
        [Description("Open link in browser to learn, how to use a Telegram Bot")]
        [Editor(typeof(FooEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public String HowToUseTelegram { get; set; }

        [CategoryAttribute("Webserver")]
        [Description("Run app embedded image webserver")]
        [ReadOnly(false)]
        public Boolean RunWebserver { get; set; }
        [CategoryAttribute("Webserver")]
        [Description("Webserver image type: LORES = low resolution image vs. PROCESS = processed image")]
        [ReadOnly(false)]
        public WebserverImageType WebserverImage { get; set; }

        [CategoryAttribute("ROI")]
        [ReadOnly(true)]
        [Description("Edit regions of interest = ROIs")]
        [Editor(typeof(RoiEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public String EditROIs { get; set; }
        [CategoryAttribute("ROI")]
        [Description("List all regions of interest = ROIs")]
        [ReadOnly(true)]
        public string[] ListROIs { get; set; }

        // INI: read PropertyGrid from ini
        public void fillPropertyGridFromIni()
        {
            IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            int tmpInt;
            bool tmpBool;
            string tmpStr;
            // camera moniker string
            CameraMoniker = ini.IniReadValue(iniSection, "CameraMoniker", "empty");
            // camera resolution width
            if ( int.TryParse(ini.IniReadValue(iniSection, "CameraResolutionWidth", "100"), out tmpInt) ) {
                CameraResolution = new Size(tmpInt, 0);
            }
            // camera resolution height
            if ( int.TryParse(ini.IniReadValue(iniSection, "CameraResolutionHeight", "200"), out tmpInt) ) {
                CameraResolution = new Size(CameraResolution.Width, tmpInt);
            }
            // camera exposure 
            if ( bool.TryParse(ini.IniReadValue(iniSection, "ExposureByApp", "False"), out tmpBool) ) {
                ExposureByApp = tmpBool;
            }
            if ( bool.TryParse(ini.IniReadValue(iniSection, "ExposureAuto", "False"), out tmpBool) ) {
                ExposureAuto = tmpBool;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "Exposure", "-5"), out tmpInt) ) {
                ExposureVal = tmpInt;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "ExposureMin", "-200"), out tmpInt) ) {
                ExposureMin = tmpInt;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "ExposureMax", "200"), out tmpInt) ) {
                ExposureMax = tmpInt;
            }
            // camera brightness
            if ( int.TryParse(ini.IniReadValue(iniSection, "Brightness", "-6"), out tmpInt) ) {
                Brightness = tmpInt;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "BrightnessMin", "-200"), out tmpInt) ) {
                BrightnessMin = tmpInt;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "BrightnessMax", "200"), out tmpInt) ) {
                BrightnessMax = tmpInt;
            }
            // form width
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormWidth", "657"), out tmpInt) ) {
                FormSize = new Size(tmpInt, 0);
            }
            // form height
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormHeight", "588"), out tmpInt) ) {
                FormSize = new Size(FormSize.Width, tmpInt);
            }
            // form x
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormX", "10"), out tmpInt) ) {
                FormLocation = new Point(Math.Min(Math.Max(0, tmpInt), 500), 0);
            }
            // form y
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormY", "10"), out tmpInt) ) {
                FormLocation = new Point(FormLocation.X, Math.Min(Math.Max(0, tmpInt), 400));
            }
            // debug image processing  
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DebugProc", "False"), out tmpBool) ) {
                DebugProcessImages = tmpBool;
            }
            // debug false positive images
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DebugFalsePositives", "False"), out tmpBool) ) {
                DebugFalsePositiveImages = tmpBool;
            }
            // debug non consecutive images
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DebugNonConsecutives", "False"), out tmpBool) ) {
                DebugNonConsecutives = tmpBool;
            }
            // save motion sequences
            if ( bool.TryParse(ini.IniReadValue(iniSection, "SaveSequences", "False"), out tmpBool) ) {
                SaveSequences = tmpBool;
            }
            // save motion images
            if ( bool.TryParse(ini.IniReadValue(iniSection, "SaveMotion", "False"), out tmpBool) ) {
                SaveMotion = tmpBool;
            }
            // auto detect motion at app start
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DetectMotion", "False"), out tmpBool) ) {
                DetectMotion = tmpBool;
            }
            // minimize app while motion detection
            if ( bool.TryParse(ini.IniReadValue(iniSection, "MinimizeApp", "True"), out tmpBool) ) {
                MinimizeApp = tmpBool;
            }
            // always false
            MakeVideoNow = false;
            // make daily motion video
            if ( bool.TryParse(ini.IniReadValue(iniSection, "MakeDailyVideo", "False"), out tmpBool) ) {
                MakeDailyVideo = tmpBool;
            }
            // app start after 19:00 should not start making daily video again, if it was already done
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DailyVideoDoneForToday", "False"), out tmpBool) ) {
                DailyVideoDone = tmpBool;
            }
            // reboot windows once a day
            if ( bool.TryParse(ini.IniReadValue(iniSection, "RebootDaily", "False"), out tmpBool) ) {
                RebootDaily = tmpBool;
            }
            // reboot windows allowed after heavy ping fail
            if ( bool.TryParse(ini.IniReadValue(iniSection, "RebootPingAllowed", "False"), out tmpBool) ) {
                RebootPingAllowed = tmpBool;
            }
            // reboot counter after heavy ping fail
            if ( int.TryParse(ini.IniReadValue(iniSection, "RebootPingCounter", "0"), out tmpInt) ) {
                RebootPingCounter = tmpInt;
            }
            // run webserver
            if ( bool.TryParse(ini.IniReadValue(iniSection, "RunWebserver", "False"), out tmpBool) ) {
                RunWebserver = tmpBool;
            }
            // webserver image type
            tmpStr = ini.IniReadValue(iniSection, "WebserverImageType", "empty");
            Array values = Enum.GetValues(typeof(WebserverImageType));
            foreach ( WebserverImageType val in values ) {
                if ( val.ToString() == tmpStr ) {
                    WebserverImage = val;
                    break;
                }
                WebserverImage = WebserverImageType.PROCESS;
            }
            // ping test address + a ref var with the same purpose (get/set cannot be a ref var)
            PingTestAddress = ini.IniReadValue(iniSection, "PingTestAddress", "8.8.8.8");
            PingTestAddressRef = PingTestAddress;
            // use Telegram bot
            if ( bool.TryParse(ini.IniReadValue(iniSection, "UseTelegramBot", "False"), out tmpBool) ) {
                UseTelegramBot = tmpBool;
            }
            // Telegram bot authentication token
            BotAuthenticationToken = ini.IniReadValue(iniSection, "BotAuthenticationToken", "");
            // app common storage path
            StoragePath = ini.IniReadValue(iniSection, "StoragePath", Application.StartupPath + "\\");
            // alternative app storage path, if above path is full
            StoragePathAlt = ini.IniReadValue(iniSection, "StoragePathAlt", "");
            // free storage space
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = MainForm.driveFreeBytes(StoragePath);
            int order = 0;
            while ( len >= 1024 && order < sizes.Length - 1 ) {
                order++;
                len = len / 1024;
            }
            FreeStorageSpace = String.Format("{0:0.##} {1}", len, sizes[order]);
            // app writes logfile
            if ( bool.TryParse(ini.IniReadValue(iniSection, "WriteLogfile", "False"), out tmpBool) ) {
                WriteLogfile = tmpBool;
            }
            // hint to edit ROIs
            EditROIs = "Click, then click again the right hand side '...' button to edit ROIs";
            // set all ROIs in PropertyGrid array
            ListROIs = new string[] { "", "", "", "", "", "", "", "", "", "" };
            for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                string strROI = ini.IniReadValue("ROI section", "roi" + i.ToString(), "0,0,0,0,0,0.0,False,1");
                ListROIs[i] = strROI;
            }
            // how to use a Telegram bot
            HowToUseTelegram = "https://core.telegram.org/bots#creating-a-new-bot\\";
        }

        // INI: write to ini
        public void writePropertyGridToIni()
        {
            // wipe existing ini
            System.IO.File.Delete(System.Windows.Forms.Application.ExecutablePath + ".ini");
            // ini from scratch
            IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            // camera moniker string
            ini.IniWriteValue(iniSection, "CameraMoniker", CameraMoniker);
            // camera resolution width
            ini.IniWriteValue(iniSection, "CameraResolutionWidth", CameraResolution.Width.ToString());
            // camera resolution height
            ini.IniWriteValue(iniSection, "CameraResolutionHeight", CameraResolution.Height.ToString());
            // camera exposure
            ini.IniWriteValue(iniSection, "ExposureByApp", ExposureByApp.ToString());
            ini.IniWriteValue(iniSection, "ExposureAuto", ExposureAuto.ToString());
            ini.IniWriteValue(iniSection, "Exposure", ExposureVal.ToString());
            ini.IniWriteValue(iniSection, "ExposureMin", ExposureMin.ToString());
            ini.IniWriteValue(iniSection, "ExposureMax", ExposureMax.ToString());
            // camera brightness
            ini.IniWriteValue(iniSection, "Brightness", Brightness.ToString());
            ini.IniWriteValue(iniSection, "BrightnessMin", BrightnessMin.ToString());
            ini.IniWriteValue(iniSection, "BrightnessMax", BrightnessMax.ToString());
            // form width
            ini.IniWriteValue(iniSection, "FormWidth", FormSize.Width.ToString());
            // form height
            ini.IniWriteValue(iniSection, "FormHeight", FormSize.Height.ToString());
            // form width
            ini.IniWriteValue(iniSection, "FormX", FormLocation.X.ToString());
            // form height
            ini.IniWriteValue(iniSection, "FormY", FormLocation.Y.ToString());
            // debug image processing
            ini.IniWriteValue(iniSection, "DebugProc", DebugProcessImages.ToString());
            // debug false positive images
            ini.IniWriteValue(iniSection, "DebugFalsePositives", DebugFalsePositiveImages.ToString());
            // debug non consecutive images
            ini.IniWriteValue(iniSection, "DebugNonConsecutives", DebugNonConsecutives.ToString());
            // save motion sequences
            ini.IniWriteValue(iniSection, "SaveSequences", SaveSequences.ToString());
            // save motion images
            ini.IniWriteValue(iniSection, "SaveMotion", SaveMotion.ToString());
            // auto detect motion at app start
            ini.IniWriteValue(iniSection, "DetectMotion", DetectMotion.ToString());
            // minimize app while motion detection
            ini.IniWriteValue(iniSection, "MinimizeApp", MinimizeApp.ToString());
            // make daily motion video
            ini.IniWriteValue(iniSection, "MakeDailyVideo", MakeDailyVideo.ToString());
            // flag make daily motion video done
            ini.IniWriteValue(iniSection, "DailyVideoDoneForToday", DailyVideoDone.ToString());
            // reboot counter
            ini.IniWriteValue(iniSection, "RebootPingCounter", RebootPingCounter.ToString());
            // app storage path
            ini.IniWriteValue(iniSection, "StoragePath", StoragePath.ToString());
            // alternative app storage path
            ini.IniWriteValue(iniSection, "StoragePathAlt", StoragePathAlt.ToString());
            // app writes logfile
            ini.IniWriteValue(iniSection, "WriteLogfile", WriteLogfile.ToString());
            // run webserver
            ini.IniWriteValue(iniSection, "RunWebserver", RunWebserver.ToString());
            // webserver image type
            ini.IniWriteValue(iniSection, "WebserverImageType", WebserverImage.ToString());
            // ping test address
            ini.IniWriteValue(iniSection, "PingTestAddress", PingTestAddress);
            // use Telegram bot
            ini.IniWriteValue(iniSection, "UseTelegramBot", UseTelegramBot.ToString());
            // Telegram bot authentication token
            ini.IniWriteValue(iniSection, "BotAuthenticationToken", BotAuthenticationToken);
            // reboot windows daily
            ini.IniWriteValue(iniSection, "RebootDaily", RebootDaily.ToString());
            // reboot windows allowed after heavy ping fail
            ini.IniWriteValue(iniSection, "RebootPingAllowed", RebootPingAllowed.ToString());
            // write ROIs from PropertyGrid array to INI
            for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                ini.IniWriteValue("ROI section", "roi" + i.ToString(), ListROIs[i]);
            }
        }

        // obtain the list of ROIs from the settings PropertyGrid
        public List<MainForm.oneROI> getROIsListFromPropertyGrid() {
            List<MainForm.oneROI> list = new List<MainForm.oneROI>();
            for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                string[] arr = ListROIs[i].Split(',');
                list.Add(new MainForm.oneROI());
                list[i].rect = new Rectangle(int.Parse(arr[0]), int.Parse(arr[1]), int.Parse(arr[2]), int.Parse(arr[3]));
                list[i].thresholdIntensity = int.Parse(arr[4]);
                double outVal;
                double.TryParse(arr[5], NumberStyles.Any, CultureInfo.InvariantCulture, out outVal);
                list[i].thresholdChanges = outVal;
                list[i].reference = bool.Parse(arr[6]);
                list[i].boxScaler = int.Parse(arr[7]);
            }
            return list;
        }
        // set the settings PropertyGrid to the list of ROIs provided by the ROIs edit dialog
        public void setPropertyGridToROIsList(List<MainForm.oneROI> list) {
            IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                if ( i >= list.Count ) {
                    break;
                }
                ListROIs[i] =
                    list[i].rect.X.ToString() + "," +
                    list[i].rect.Y.ToString() + "," +
                    list[i].rect.Width.ToString() + "," +
                    list[i].rect.Height.ToString() + "," +
                    list[i].thresholdIntensity.ToString() + "," +
                    list[i].thresholdChanges.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    list[i].reference.ToString() + "," +
                    list[i].boxScaler.ToString();
            }
        }

        // INI-Files CLass : easiest (though outdated) way to administer app specific setup data
        public class IniFile
        {
            private string path;
            [DllImport("kernel32")]
            private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);
            [DllImport("kernel32")]
            private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);
            public IniFile(string path)
            {
                this.path = path;
            }
            public void IniWriteValue(string Section, string Key, string Value)
            {
                try {
                    WritePrivateProfileString(Section, Key, Value, this.path);
                }
                catch ( Exception ex ) {
                    Logger.logTextLnU(DateTime.Now, "IniWriteValue ex: " + ex.Message);
                    AutoMessageBox.Show("INI-File could not be saved. Please select another 'home folder' in the Main Window.", "Error", 5000);
                }
            }
            public string IniReadValue(string Section, string Key, string DefaultValue)
            {
                StringBuilder retVal = new StringBuilder(255);
                int i = GetPrivateProfileString(Section, Key, DefaultValue, retVal, 255, this.path);
                return retVal.ToString();
            }
        }
    }

}

