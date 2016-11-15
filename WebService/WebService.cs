using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.IO;
using System.Text;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.ApplicationModel.AppService;
using System.Linq;
using Windows.Foundation.Collections;
using System.Collections.Generic;
using Windows.Foundation.Diagnostics;
using System.Diagnostics;

namespace WebService
{
    public sealed class WebService
    {
        const uint BUFFERSIZE = 8192;
        private AppServiceConnection serviceConnection;
        private string feedBack;
        private string headerFile;
        private string cssFile;
        private string bodyFile;
        private bool bAction;
        private bool bMsg = false;
        private LoggingChannel lc; //Used for debugging
        public uint BufferSize { get; private set; }

        /************************************
         * Note the use of LoggingChannel at various methods. These are commented out. If you want to
         * debug your application, ensure that 'Microsoft-Windows-Diagnostics-LoggingChannel' in 
         * 'Registered Providers' under ETW screen is enabled. This is in your Windows IoT screen
         ************************************/

        public WebService()
        {

        }

        /***********************************
         * Start of the Webpage. Load all the files required for webpage presentation. 
         * Bind and listen to socket
         ***********************************/
        public async void Start()
        {
            headerFile = File.ReadAllText("web\\header.html");
            cssFile = File.ReadAllText("web\\theme.css");
            bodyFile = File.ReadAllText("web\\body.html");
            StreamSocketListener listener = new StreamSocketListener();
            await listener.BindServiceNameAsync("8090");
            listener.ConnectionReceived += Listener_ConnectionReceived;
        }

        /***********************************
         * Calling application will start this service. To ensure that messages can be
         * dispatched to the calling program, save the service connection info 
         ***********************************/
        public void SetConnection(AppServiceConnection asc)
        {
            serviceConnection = asc;
            serviceConnection.RequestReceived += ServiceConnection_RequestReceived;

        }

        /**************************************
         * This is used as feedback to the Webclient. Text message from Application controlling the Pi
         * will be received here. This will be stored in feedback variable
         ***************************************/
        private void ServiceConnection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            feedBack = (string)args.Request.Message.First().Value;

            //Use the next two lines for event logging
            //lc = new LoggingChannel("my provider", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
            //lc.LogMessage(feedBack);
        }


        /******************************************
         * This procedure listens for messages arriving to socket 8090. This information will be 
         * built to string. It will then extract the data from the received string
         * by calling GetQuery method. The data is properly dispatched to the service connection
         * established / stored by this application. 
         * Finally OutputWebpage is called to provide feedback to the webclient. 
         * bMsg bool is added to ensure that when multiple messages are received, the messages are discarded.
         * This is required. Without this, the Webservice crashes after some time.
         ******************************************/
        private async void Listener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            if (!bMsg)
            {
                bMsg = true;
                StringBuilder request = new StringBuilder();
                try
                {
                    using (IInputStream input = args.Socket.InputStream)
                    {
                        byte[] data = new byte[BUFFERSIZE];
                        IBuffer buffer = data.AsBuffer();
                        uint dataRead = BUFFERSIZE;
                        while (dataRead == BUFFERSIZE)
                        {
                            await input.ReadAsync(buffer, BUFFERSIZE, InputStreamOptions.Partial);
                            request.Append(Encoding.UTF8.GetString(data, 0, data.Length));
                            dataRead = buffer.Length;
                        }
                    }

                    string inputString = GetQuery(request);

                    //If you are running as a background webserver serviceConnection will be null
                    //Dispatch the message to the service connection.
                    if (serviceConnection != null)
                        await serviceConnection.SendMessageAsync(new ValueSet { new KeyValuePair<string, object>("Query", inputString) });

                    OutputWebPage(args, inputString);
                }
                catch (Exception Ex)
                {
                    Debug.WriteLine("Exception occurred: {0}", Ex);
                    //Uncomment next two lines for logging the exception errors
                    lc = new LoggingChannel("my provider", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
                    lc.LogMessage("Exception at Input String: " + Ex);
                    bMsg = false;
                }
                bMsg = false;
            }
            //Uncomment the else statement if you want to use debugging
            /*
            else
            {
                lc = new LoggingChannel("my provider", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
                lc.LogMessage("Multiple messages received");
            }   
            */             
        }

        /*************************
         * OutputWebPages checks whether the data coming in is "Request=Feedback" if it is then
         * only the feedback variable is sent back prefixed with "Result: ". Thus the jquery
         * in HTML webpage can look for this keyword at the begining of the text to take any 
         * action.
         * If Feedback info is not requested, a webpage is constructed and sent. Note that the
         * Header and body tags are added to the webpage while the contents are read from
         * respective files. A line for providing the log is added at the end with an ID of retVal for
         * calling theme benefit. The log line provides decoded input passed by webpage
         * ***********************/
        private async void OutputWebPage(StreamSocketListenerConnectionReceivedEventArgs args, string inputString)
        {
            bAction = inputString.Contains("Action") ;
            
            //Use the next two lines for event logging
            //lc = new LoggingChannel("my provider", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
            //lc.LogMessage(inputString.ToString());

            string data = string.IsNullOrWhiteSpace(feedBack) ? "None" : feedBack;
            try
            {
                using (IOutputStream output = args.Socket.OutputStream)
                {
                    using (Stream response = output.AsStreamForWrite())
                    {
                        if (bAction)
                        {
                            //data = "Results: " + feedBack;
                            //data = DateTime.Now.TimeOfDay.ToString();
                            byte[] fbArray = Encoding.UTF8.GetBytes(data);
                            await response.WriteAsync(fbArray, 0, fbArray.Length);
                            await response.FlushAsync();
                            //Use the next two lines for event logging
                            //lc = new LoggingChannel("my provider", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
                            //lc.LogMessage(feedBack);
                        }
                        else
                        {
                            byte[] bodyArray = Encoding.UTF8.GetBytes(
                                    $"<!DOCTYPE html>\n<html>\n<head>{headerFile}<style>{cssFile}</style>\n</head>\n<body>{bodyFile}<p>Feedback Received: <span id=\"retVal\">{data}</span></p>\n</body>\n</html>");

                            var bodyStream = new MemoryStream(bodyArray);

                            var header = "HTTP/1.1 200 OK\r\n" +
                                        $"Content-Length: {bodyStream.Length}\r\n" +
                                            "Connection: close\r\n\r\n";

                            byte[] headerArray = Encoding.UTF8.GetBytes(header);
                            await response.WriteAsync(headerArray, 0, headerArray.Length);
                            await bodyStream.CopyToAsync(response);
                            await response.FlushAsync();
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception Occured: {0}", ex);
                //Uncomment next two lines for logging the exception errors
                lc = new LoggingChannel("my provider", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
                lc.LogMessage("Exception at Output Page: " + ex);
                bMsg = false;

            }
        }

        /********************************
         * GET or POST method can be used to submit data. The queried data appears as '/?xxx=xx' immdediately
         * followed the GET while for POST this will be of the form 'xxx=xx' at the end of HTTP text.
         * The GetQuery caters for both. It will check whether GET or POST is used and then separate the
         * data. Note using Space to split the contents. 
         * ********************************/
        private string GetQuery(StringBuilder request)
        {
            string data = "";
            var requestLines = request.ToString().Split(' ');

            if (requestLines[0] == "POST")
            {
                return requestLines[requestLines.Length - 1];
            }

            data = requestLines.Length > 1 ? requestLines[1] : "Unregistered".ToString();

            //Use the next two lines for event logging
            //lc = new LoggingChannel("my provider", null, new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a"));
            //lc.LogMessage(data);
            
            return data;
        }
    }
}
