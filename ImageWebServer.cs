using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GrzTools;

namespace MotionUVC {

    // embedded image webserver
    public static class ImageWebServer {

        // local vars
        private static Bitmap _image = null;
        private static bool _bRunWebserver = false;
        private static TcpListener _tcpListener = null;
        private static List<Task> _clientsTaskList = new List<Task>();
        private static readonly Object _obj = new Object();

        // public set Bitmap image to show
        public static Bitmap Image {
            set {
                lock ( _obj ) {
                    if ( _image != null ) {
                        _image.Dispose();
                    }
                    _image = value != null ? (Bitmap)value.Clone() : new Bitmap(100, 100);
                }
            }
        }
        // public get webserver status running
        public static bool IsRunning {
            get {
                return _bRunWebserver;
            }
        }
        // public start werbserver
        public static void Start() {
            // do nothing and return. if already running
            if ( _bRunWebserver ) {
                return;
            }
            // start the listener / webserver
            _bRunWebserver = true;
            Task.Run(() => {
                execWebServer();
            });
        }
        // public stop werbserver
        public static void Stop() {
            // now stop the listener / webserver
            _bRunWebserver = false;
            // terminate the listener
            if ( _tcpListener != null ) {
                _tcpListener.Stop();
                _tcpListener.Server.Close();
            }
            // clear clients list
            _clientsTaskList.Clear();
        }

        // local webserver is a TcpListnener, waiting for TcpClients to whose images are send via html formatted NetStreams
        private static void execWebServer() {
            IPAddress ip = IPAddress.Any;
            int port = 8080;
            try {
                // new TcpListener object
                if ( _tcpListener != null ) {
                    _tcpListener.Stop();
                    _tcpListener.Server.Close();
                }
                _tcpListener = new TcpListener(ip, port);
                // start TcpListener
                try {
                    _tcpListener.Start();
                    Logger.logTextLn(DateTime.Now, "execWebServer started");
                } catch ( Exception ex ) {
                    Logger.logTextLn(DateTime.Now, "execWebServer ex: " + ex.Message);
                    return;
                }
                // loop the tcpListener for incoming clients
                while ( _bRunWebserver ) {
                    // AcceptTcpClient() is a blocking call until a client request is accepted
                    TcpClient client = _tcpListener.AcceptTcpClient();
                    // check count of active tasks, delete completed tasks
                    for ( int i = _clientsTaskList.Count - 1; i >= 0; i-- ) {
                        if ( _clientsTaskList[i].IsCompleted ) {
                            _clientsTaskList.RemoveAt(i);
                        }
                    }
                    // limit the number of clients to max 5
                    if ( _clientsTaskList.Count < 5 ) {
                        // deal with the new client in a separate task
                        Task t = Task.Run(() => {
                            handleTcpClient(ref _bRunWebserver, ref client);
                        });
                        _clientsTaskList.Add(t);
                    }
                }
            } catch ( SocketException e ) {
                Logger.logTextLn(DateTime.Now, "execWebServer e: " + e.Message);
            }
            // clean up
            if ( _tcpListener != null ) {
                _tcpListener.Stop();
                _tcpListener.Server.Close();
            }
            if ( _image != null ) {
                _image.Dispose();
            }
            for ( int i = _clientsTaskList.Count - 1; i >= 0; i-- ) {
                if ( _clientsTaskList[i].IsCompleted ) {
                    _clientsTaskList.RemoveAt(i);
                }
            }
            Logger.logTextLn(DateTime.Now, "execWebServer finished");
        }

        // handle a single tcp client
        private static void handleTcpClient(ref bool bRun, ref TcpClient client) {
            // stream object for later reading from and writing to TcpClient
            NetworkStream stream = client.GetStream();
            // reader loop will receive data sent by the client
            StreamReader reader = new StreamReader(stream, Encoding.ASCII);
            // stream read loop 
            string inputLines = "";
            while ( inputLines != null && bRun ) {
                try {
                    // reader.ReadLine() is a blocking call: until 'something useful' was received from client OR client disconnects -> Exception -> inputLine == null
                    string line;
                    while ( (line = reader.ReadLine()) != "" ) {
                        inputLines += line;
                    }
                    if ( inputLines.StartsWith("GET /?action=stream") ) {
                        Logger.logTextLn(DateTime.Now, String.Format("handleTcpClient #{0}: sending images", client.Client.Handle));
                        // send images in a loop
                        sendImagesToWebClient(ref bRun, client, stream);
                    } else {
                        // send stream awareness
                        sendActionStreamAwareness(stream);
                    }
                    // only way to get here is bRun == false: leave this inner client loop and quit everything
                    inputLines = null;
                } catch ( Exception ex ) {
                    // only way to get here is, if client disconnects (X in browser, close browser) -> IOException is thrown
                    Logger.logTextLn(DateTime.Now, String.Format("handleTcpClient ex: client #{0} was closed", client.Client.Handle));
                    // leave this inner client loop with 'inputLines = null' and quit
                    inputLines = null;
                }
            }
            if ( !bRun ) {
                Logger.logTextLn(DateTime.Now, String.Format("handleTcpClient: client #{0} stopped on purpose", client.Client.Handle));
            }
            if ( stream != null ) {
                stream.Close();
                stream = null;
            }
            if ( client != null ) {
                client.Close();
            }
        }

        // continously send images to a web client (browser) via html protocol
        private static void sendImagesToWebClient(ref bool bRun, TcpClient client, NetworkStream stream) {

            // send image awareness #1 (multipart data) to client
            string message = "HTTP/1.0 200 OK\r\n" +
                             "Connection: keep-alive\r\n" +
                             "Server: MotionUVC\r\n" +
                             "Cache-Control: no-store, no-cache, must-revalidate, pre-check=0, post-check=0, max-age=0\r\n" +
                             "Pragma: no-cache\r\n" +
                             "Content-Type: multipart/x-mixed-replace;boundary=" +
                             "boundarystring" +
                             "\r\n" +
                             "\r\n";
            byte[] bufTxt = System.Text.ASCIIEncoding.ASCII.GetBytes(message);
            stream.Write(bufTxt, 0, bufTxt.Length);

            // send image awareness #2 (begin image section) to client
            message = "--boundarystring\r\n";
            bufTxt = System.Text.ASCIIEncoding.ASCII.GetBytes(message);
            stream.Write(bufTxt, 0, bufTxt.Length);

            // looper: send images to client 
            while ( bRun ) {

                try {
                    // image to byte buffer
                    byte[] bufImg = null;
                    lock ( _obj ) {
                        try {
                            bufImg = bitmapToByteArray(_image);
                        } catch ( Exception e ) {
                            Logger.logTextLn(DateTime.Now, String.Format("sendImagesToWebClient #[1} e: {0}", e.Message, client.Client.Handle));
                            continue;
                        }
                    }

                    // send image awareness #3 (actual image type & size) to client
                    message = "Content-Type: image/jpeg\r\n" +
                              "Content-Length: " +
                              bufImg.Length.ToString() +
                              "\r\n" +
                              "\r\n";
                    bufTxt = System.Text.ASCIIEncoding.ASCII.GetBytes(message);
                    stream.Write(bufTxt, 0, bufTxt.Length);

                    // send image data to client
                    stream.Write(bufImg, 0, bufImg.Length);

                    // send image awareness #4 (current image is ended) to client
                    message = "\r\n--boundarystring\r\n";
                    bufTxt = System.Text.ASCIIEncoding.ASCII.GetBytes(message);
                    stream.Write(bufTxt, 0, bufTxt.Length);
                } catch ( InvalidOperationException ioe ) {
                    Logger.logTextLn(DateTime.Now, String.Format("sendImagesToWebClient #{1} ioe: {0}", ioe.Message, client.Client.Handle));
                }

                Thread.Sleep(500);
            }
            Logger.logTextLn(DateTime.Now, String.Format("sendImagesToWebClient #{0} ended", client.Client.Handle));
        }

        // send html header with '/?action=stream' awareness to client
        private static void sendActionStreamAwareness(NetworkStream stream) {
            String content = "<html>" +
                    "<head><title>Webcam</title></head>" +
                    "<body><img src='/?action=stream' alt='Camera is not available.' /></body>" +
                    "</html>";
            String header = "HTTP/1.0 200 OK\r\n" +
                    "Cache-Control: no-store, no-cache, must-revalidate, pre-check=0, post-check=0, max-age=0\r\n" +
                    "Pragma: no-cache\r\n" +
                    "Content-Type: text/html\r\n" +
                    "Expires: 0\r\n" +
                    "Content-Lenght: " + content.Length + "\r\n" +
                    "\r\n";
            byte[] bufTxt = System.Text.ASCIIEncoding.ASCII.GetBytes(header);
            stream.Write(bufTxt, 0, bufTxt.Length);
            bufTxt = System.Text.ASCIIEncoding.ASCII.GetBytes(content);
            stream.Write(bufTxt, 0, bufTxt.Length);
        }

        // return a Bitmap as byte array
        private static byte[] bitmapToByteArray(Bitmap bmp) {
            using ( MemoryStream memoryStream = new MemoryStream() ) {
                bmp.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                return memoryStream.ToArray();
            }
        }
    }

}
