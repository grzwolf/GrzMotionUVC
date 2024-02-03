using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;               // BitmapData, LockBits
using System.Runtime.InteropServices;       // DLLImport, Marshal
using System.IO;                            // File, Path
using System.Globalization;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Threading;

namespace GrzTools
{
    // non blocking & self closing message box
    public class AutoMessageBox {
        AutoMessageBox(string text, string caption, int timeout) {
            Form w = new Form() { Size = new Size(0, 0) };
            TaskEx.Delay(timeout)
                  .ContinueWith((t) => w.Close(), TaskScheduler.FromCurrentSynchronizationContext());
            MessageBox.Show(w, text, caption);
        }
        public static void Show(string text, string caption, int timeout) {
            new AutoMessageBox(text, caption, timeout);
        }
        static class TaskEx {
            public static Task Delay(int dueTimeMs) {
                TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
                CancellationTokenRegistration ctr = new CancellationTokenRegistration();
                System.Threading.Timer timer = new System.Threading.Timer(delegate (object self) {
                    ctr.Dispose();
                    ((System.Threading.Timer)self).Dispose();
                    tcs.TrySetResult(null);
                });
                timer.Change(dueTimeMs, -1);
                return tcs.Task;
            }
        }
    }

    // to be able to detect when camera property window closes
    class FindWindow {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        public delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.Dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr parentHandle, EnumChildProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
        const uint WM_SETTEXT = 0x000C;

        // set window text of a given window
        public static void SetWindowText(IntPtr hWnd, string text) {
            SendMessage(hWnd, WM_SETTEXT, IntPtr.Zero, text);
        }
        // get window text of a given handle
        public static string GetWindowText(IntPtr hWnd) {
            int size = GetWindowTextLength(hWnd);
            if ( size++ > 0 ) {
                var builder = new StringBuilder(size);
                GetWindowText(hWnd, builder, builder.Capacity);
                return builder.ToString();
            }
            return String.Empty;
        }
        // find first top level window containing titleText an return its window handle 
        public static IntPtr FindWindowWithText(string titleText) {
            IntPtr window = IntPtr.Zero;
            List<IntPtr> windows = new List<IntPtr>();
            EnumWindows(delegate (IntPtr wnd, IntPtr param)
            {

                if ( GetWindowText(wnd).Contains(titleText) ) {
                    window = wnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return window;
        }
        // find first window of a given parentHandle containing titleText an return its window handle 
        public static IntPtr FindChildWindowWithText(IntPtr parentHandle, string titleText) {
            var result = IntPtr.Zero;
            if ( parentHandle == IntPtr.Zero ) {
                parentHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            EnumChildWindows(parentHandle, (hwnd, param) =>
            {
                if ( GetWindowText(hwnd).Contains(titleText) ) {
                    result = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }
    }

    class Tools
    {

        public static byte[] Bitmap24bppToByteArray( Bitmap sourceBitmap )
        {
            BitmapData sourceData = sourceBitmap.LockBits(new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte[] pixelBuffer = new byte[sourceData.Stride * sourceData.Height];
            Marshal.Copy(sourceData.Scan0, pixelBuffer, 0, pixelBuffer.Length);
            sourceBitmap.UnlockBits(sourceData);
            return pixelBuffer;
        }

        public static Bitmap ByteArrayToBitmap( int width, int height, byte[] pixelBuffer )
        {
            var resultBitmap = new Bitmap(width, height);
            BitmapData resultData = resultBitmap.LockBits(new Rectangle(0, 0, resultBitmap.Width, resultBitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb/* .Format32bppArgb*/);
            Marshal.Copy(pixelBuffer, 0, resultData.Scan0, pixelBuffer.Length);
            resultBitmap.UnlockBits(resultData);
            pixelBuffer = null;
            return resultBitmap;
        }

        public static Color GetPixelValue( byte[] stride, int bytesPerPixel, int width, int x, int y )
        {
            var position = GetPixelPositionInArray(bytesPerPixel, width, x, y);

            var red = stride[position];
            var green = stride[position + 1];
            var blue = stride[position + 2];

            return Color.FromArgb(red, green, blue);
        }

        private static int GetPixelPositionInArray( int bytesPerPixel, int width, int x, int y )
        {
            var position = bytesPerPixel * (x + y * width);
            return position;
        }

        public static void SetPixel( byte[] stride, int bytesPerPixel, int width, int x, int y, Color color )
        {
            var position = GetPixelPositionInArray(bytesPerPixel, width, x, y);

            var red = color.R;
            var green = color.G;
            var blue = color.B;

            stride[position] = red;
            stride[position + 1] = green;
            stride[position + 2] = blue;
        }
    }
}
