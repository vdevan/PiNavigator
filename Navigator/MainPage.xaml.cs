using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.AppService;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Navigator
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private static I2CClass i2c;
        private static SerialInterface si;
        private static PWMController pwmc;
        const int READBYTES = 2048;
        const double REFRESHCYCLE = 750;
        private AppServiceConnection asc = null;
        private DispatcherTimer refreshTimer;
        private bool bGPS = false;
        private bool bWebInit = false;

        public MainPage()
        {
            this.InitializeComponent();
            SetupAppServerService();
            InitializeDevices();
            refreshTimer = new DispatcherTimer();
            refreshTimer.Interval = TimeSpan.FromMilliseconds(REFRESHCYCLE);
            refreshTimer.Tick += RefreshTimer_Tick; ;
            refreshTimer.Start();

        }

        private void RefreshTimer_Tick(object sender, object e)
        {
            //Update Compass values if not error in measurement
            if (i2c.GetXYZ() != "")
            {
                tbCompass.Text = i2c.GetXYZ();
                int head = Convert.ToInt16(i2c.GetDirection());
                switch (head)
                {
                    case 0:
                        tbHeading.Text = "North";
                        break;
                    case 90:
                        tbHeading.Text = "East";
                        break;
                    case 180:
                        tbHeading.Text = "South";
                        break;
                    case 270:
                        tbHeading.Text = "West";
                        break;
                    default:
                        if (head > 270)
                        {
                            tbHeading.Text = "North-West";
                            break;
                        }
                        if (head > 180)
                        {
                            tbHeading.Text = "South-West";
                            break;
                        }
                        if (head > 90)
                        {
                            tbHeading.Text = "South-East";
                            break;
                        }
                        tbHeading.Text = "North-East";
                        break;

                }

                //Next we will get the data from GPS and update
                if (bGPS)
                {
                    tbHeading.Text += string.Format("; Speed in KM: {0:N2}", si.Speed);
                    GetGPSData();
                }
                

                //finally send this as feedback
                if (bWebInit)
                    SendMessageToService();

            }
        }

        private async void GetGPSData()
        {
            string str = await si.ReadAsync(READBYTES);
            if (str != "")
                tbGPS.Text = str;
        }

        private async void SendMessageToService()
        {
            ValueSet st = new ValueSet();
            string str = "Heading: " + tbHeading.Text + "; GPS Co-ordinates: " + si.latLong;
            st.Add(new KeyValuePair<string, object>("Value", str));
            var x = await asc.SendMessageAsync(st);
            Debug.WriteLine(x.Status.ToString());
        }

        private async void InitializeDevices()
        {
            //First Initialise Compass
            i2c = new I2CClass();
            if (!await i2c.Initialize())
                tbStatus.Text += "Compass failed to Initialise";

            i2c.configCompass();
            tbStatus.Text += " Compass Configured and ready...";

            //Next Initialise GPS 
            si = new SerialInterface();
            bGPS = await si.Initialize();
            if (bGPS)
                tbStatus.Text += " GPS Configured and ready...";

            //Initialise Motor Controller
            pwmc = new PWMController();
            if (await pwmc.Initialize())
            {
                tbStatus.Text += " PWM Controller Initialized and ready...";
                pwmc.SetMotorConfig();
            }

        }

        /* This procedure will look for WebService application and then initiate and connect it. The  
         * connection itself will be stored in the ApplicationServiceConnection variable which will be 
         * used for communication with the Web Service. This will ensure to get the messages passed
         * by the Web client (browser) and at the same time send a feedback to the WebService 
         * which will be picked by the Web client.
         * **/
        private async void SetupAppServerService()
        {
            var listing = await AppServiceCatalog.FindAppServiceProvidersAsync("com.brahas.WebService");
            var packageName = (listing.Count == 1) ? listing[0].PackageFamilyName : string.Empty;
            asc = new AppServiceConnection();
            asc.AppServiceName = "com.brahas.WebService";
            asc.PackageFamilyName = packageName;
            var status = await asc.OpenAsync(); //The service will be started and then open a connection to it

            if (status != AppServiceConnectionStatus.Success)
            {
                tbStatus.Text = "Could not connect: " + status.ToString();
                tbStatus.Foreground = new SolidColorBrush(Colors.DarkRed);
                tbStatus.FontWeight = FontWeights.Bold;
            }
            else
            {
                asc.RequestReceived += Asc_RequestReceived;
            }
        }

        private async void Asc_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            //We will use this flag to ensure that data is not sent before a web client has connected. 
            bWebInit = true;

            string str = args.Request.Message.First().Value.ToString();
            str = (str.Length > 2) ? str.Substring(2) : "Initialised";
            int post = str.IndexOf("TextToPi");
            if (post >= 0)
            {
                str = str.Substring(post);
            }

            await Dispatcher.RunAsync(
              Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
              {
                  str = str.Replace("_Action", "");
                  if (!str.Contains("Request"))
                      tbStatus.Text = str;
                  if (str.Contains("TextToPi"))
                  {
                      str = str.Substring(str.IndexOf('=') + 1);
                      string Text1 = str.Substring(0, str.IndexOf('&'));
                      str = str.Substring(str.IndexOf('=') + 1);
                      string Text2 = str.Substring(0, str.IndexOf('&'));
                      tbInput.Text = string.Format("Current User is: {0} {1}", Text1, Text2);
                  }
              });
              
            if (str.Contains("Button"))
                ButtonProcedure(str);
            if (str.Contains("Switch"))
                SwitchProcedure(str);
            if (str.Contains("PortSetter"))
                PortSetterProcedure(str);
            if (str.Contains("Slider"))
                SliderProcedure(str);

        }

         private void SliderProcedure(string str)
        {
            int index = str.IndexOf('=');
            string temp = str.Substring(index + 1);
            int value = Convert.ToInt16(temp);
            str = str.Substring(0, index);
            switch (str)
            {
                case "Slider1":
                    pwmc.SetSpeed(value);
                    break;
                case "Slider2":
                    //Slider 2 procedure
                    break;
                case "Slider3":
                    //Slider 3 procedure
                    break;
            }
        }

        private void PortSetterProcedure(string str)
        {
            bool bUnCheck = str.Contains("UnChecked"); 
            str = str.Substring(0, str.IndexOf('='));
            if (bUnCheck)
            {
                switch (str)
                {
                    case "PortSetter1":
                        //run Port Switch 1 Unchecked procedure
                        break;
                    case "PortSetter2":
                        //run Port Switch 2 Unchecked procedure
                        break;

                    case "PortSetter3":
                        //run Port Switch 3 Unchecked procedure
                        break;

                    case "PortSetter4":
                        //run Port Switch 4 Unchecked procedure
                        break;

                    case "PortSetter5":
                        //run Port Switch 5 Unchecked procedure
                        break;

                    case "PortSetter6":
                        //run Port Switch 6 Unchecked procedure
                        break;

                    case "PortSetter7":
                        //run Port Switch 7 Unchecked procedure
                        break;

                    case "PortSetter8":
                        //run Port Switch 8 Unchecked procedure
                        break;
                }
            }
            else
            {
                switch (str)
                {
                    case "PortSetter1":
                        //run Port Switch 1 Checked procedure
                        break;
                    case "PortSetter2":
                        //run Port Switch 2 Checked procedure
                        break;

                    case "PortSetter3":
                        //run Port Switch 3 Checked procedure
                        break;

                    case "PortSetter4":
                        //run Port Switch 4 Checked procedure
                        break;

                    case "PortSetter5":
                        //run Port Switch 5 Checked procedure
                        break;

                    case "PortSetter6":
                        //run Port Switch 6 Checked procedure
                        break;

                    case "PortSetter7":
                        //run Port Switch 7 Checked procedure
                        break;

                    case "PortSetter8":
                        //run Port Switch 8 Checked procedure
                        break;
                }
            }
            
        }

        private void SwitchProcedure(string str)
        {
            str = str.Replace("=Clicked", "");
            switch (str)
            {
                case "Switch1":
                    //Implement process for Switch 1
                    break;
                case "Switch2":
                    //Implement process for Switch 2
                    break;
                case "Switch3":
                    //Implement process for Switch 3
                    break;
                case "Switch4":
                    //Implement process for Switch 4
                    break;
                case "Switch5":
                    //Implement process for Switch 5
                    break;
                case "Switch6":
                    //Implement process for Switch 6
                    break;
                case "Switch7":
                    //Implement process for Switch 7
                    break;
                case "Switch8":
                    //Implement process for Switch 8
                    break;
            }
        }

        private void ButtonProcedure(string str)
        {
            if (str.Contains("ButtonUp"))
            {
                pwmc.Motordirection(App.DIRECTION.FORWARD);
                return;
            }

            if (str.Contains("ButtonDown"))
            {
                pwmc.Motordirection(App.DIRECTION.REVERSE);
                return;
            }

            if (str.Contains("ButtonLeft"))
            {
                pwmc.Motordirection(App.DIRECTION.LEFT);
                return;
            }

            if (str.Contains("ButtonRight"))
            {
                pwmc.Motordirection(App.DIRECTION.RIGHT);
                return;
            }

            if (str.Contains("ButtonStop"))
            {
                pwmc.StopNav();
                return;
            }

        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            if (pwmc.bInit)
                pwmc.StopNav();
            
            App.Current.Exit();
        }

        private void chbMotor_Checked(object sender, RoutedEventArgs e)
        {

            if (pwmc!=null && pwmc.bInit)
            {
                if (chbMotor.IsChecked == true)
                {
                    pwmc.StopNav();
                    pwmc.bStop = true;
                }
                else
                    pwmc.bStop = false;
            }
        }
    }
}
