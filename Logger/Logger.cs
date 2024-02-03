using System;
using System.Globalization;
using System.Diagnostics; 
using System.IO;
using System.Windows.Forms;

namespace Logging {

    // logger class
    public static class Logger {
        // write to log flag
        public static bool WriteToLog { get; set; }
        public static String FullFileNameBase { get; set; }
        // unconditional logging
        public static void logTextLnU(DateTime now, string logtxt) {
            _writeLogOverrule = true;
            logTextLn(now, logtxt);
            _writeLogOverrule = false;
        }
        public static void logTextU(string logtxt) {
            _writeLogOverrule = true;
            logTextToFile(logtxt);
            _writeLogOverrule = false;
        }
        // logging depending on WriteToLog
        public static void logTextLn(DateTime now, string logtxt) {
            logtxt = now.ToString("dd.MM.yyyy HH:mm:ss_fff ", CultureInfo.InvariantCulture) + logtxt;
            logText(logtxt + "\r\n");
        }
        public static void logText(string logtxt) {
            logTextToFile(logtxt);
        }
        // log motions list entry
        public static void logMotionListEntry(string loc, int motionIndex, bool bmpExists, bool motionConsecutive, DateTime motionTime, bool motionSaved, string alarm = "") {
            Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            while ( _busy ) {
                Application.DoEvents();
                if ( sw.ElapsedMilliseconds > 1000 ) {
                    sw.Stop();
                    return;
                }
            }
            sw.Stop();
            _busy = true;
            try {
                if ( FullFileNameBase.Length == 0 ) {
                    FullFileNameBase = Application.ExecutablePath;
                }
                string logFileName = FullFileNameBase + DateTime.Now.ToString("_yyyyMMdd", CultureInfo.InvariantCulture) + ".motions";
                System.IO.StreamWriter lsw = System.IO.File.AppendText(logFileName);
                if ( new FileInfo(logFileName).Length == 0 ) {
                    lsw.Write("call\tndx\tbmpEx.\tconsec.\ttimestamp\t\tbmpSaved\talarm\n");
                }
                string text = String.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", loc, motionIndex, bmpExists, motionConsecutive, motionTime.ToString("HH:mm:ss_fff", CultureInfo.InvariantCulture), motionSaved, alarm);
                lsw.Write(text + "\n");
                lsw.Close();
            } catch {; }
            _busy = false;
        }
        // log motions list extra marker
        public static void logMotionListExtra(string text) {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            while ( _busy ) {
                Application.DoEvents();
                if ( sw.ElapsedMilliseconds > 1000 ) {
                    sw.Stop();
                    return;
                }
            }
            sw.Stop();
            _busy = true;
            try {
                if ( FullFileNameBase.Length == 0 ) {
                    FullFileNameBase = Application.ExecutablePath;
                }
                string logFileName = FullFileNameBase + DateTime.Now.ToString("_yyyyMMdd", CultureInfo.InvariantCulture) + ".motions";
                System.IO.StreamWriter lsw = System.IO.File.AppendText(logFileName);
                if ( new FileInfo(logFileName).Length == 0 ) {
                    lsw.Write("call\tndx\tbmpEx.\tconsec.\ttimestamp\t\tbmpSaved\n");
                }
                lsw.Write(text + "\n");
                lsw.Close();
            } catch {; }
            _busy = false;
        }
        // private
        private static bool _writeLogOverrule = false;
        private static bool _busy = false;
        private static void logTextToFile(string logtxt) {
            if ( !WriteToLog && !_writeLogOverrule ) {
                return;
            }
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            while ( _busy ) {
                Application.DoEvents();
                if ( sw.ElapsedMilliseconds > 1000 ) {
                    sw.Stop();
                    return;
                }
            }
            sw.Stop();
            _busy = true;
            try {
                if ( FullFileNameBase == null || FullFileNameBase.Length == 0 ) {
                    FullFileNameBase = Application.ExecutablePath;
                }
                string logFileName = FullFileNameBase + DateTime.Now.ToString("_yyyyMMdd", CultureInfo.InvariantCulture) + ".log";
                System.IO.StreamWriter lsw = System.IO.File.AppendText(logFileName);
                lsw.Write(logtxt);
                lsw.Close();
            } catch {; }
            _busy = false;
        }
    }

}
