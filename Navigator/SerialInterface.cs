using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.SerialCommunication;
using Windows.Devices.Enumeration;
using System.Diagnostics;
using Windows.Storage.Streams;

/*******GPS Data Decoding the line starting with $GPGGA**************
 * Ref: http://www.gpsinformation.org/dale/nmea.htm
 * Split the line starting with $GPGGA with comma. Data as follows  All values are strings
 * 0    =   $GPGGA or $GNGGA    
 * 1    =   UTC of Position     Time Represented as 1st two for hours, 2nd two for Minutes and the 3rd two for seconds
 * 2    =   Latitude            double as string
 * 3    =   N or S
 * 4    =   Longitude           double as string
 * 5    =   E or W
 * 6    =   GPS quality indicator (0=invalid; 1=GPS fix; 2=Diff. GPS fix)
 * 7    =   Number of satellites in use [not those in view]
 * 8    =   Horizontal dilution of position
 * 9    =   Antenna altitude above/below mean sea level (geoid)
 * 10   =   Meters  (Antenna height unit)
 * 11   =   Geoidal separation (Diff. between WGS-84 earth ellipsoid and mean sea level)
 * 12   =   Meters  (Units of geoidal separation)
 * 13   =   Age in seconds since last update from diff. reference station
 * 14   =   Diff. reference station ID#
 * 15   =   Checksum
 * 
 * */

/******** GPS Data decoding for Speed - Line starting with $GPVTG******
 * 0    = $GPVTG or $GNVTG
 * 1    = Track made good
 * 2    = Fixed text 'T' indicates that track made good is relative to true north
 * 3    = not used
 * 4    = not used
 * 5    = Speed over ground in knots
 * 6    = Fixed text 'N' indicates that speed over ground in in knots
 * 7    = Speed over ground in kilometers/hour
 * 8    = Fixed text 'K' indicates that speed over ground is in kilometers/hour
 * 9    = Checksum
 * */


namespace Navigator
{
    public class SerialInterface
    {
        const int TIMEOUT = 300;
        public bool bInitialize;
        private SerialDevice sd;
        private DataReader dr;
        public string latLong = "";
        public double Speed = 0d;
        class GPSSpeed
        {
            public bool status; // [2]
            public double SpeedKnots;  //[5]
            public double SpeedKM;  //[7]

            public GPSSpeed()
            {
                status = false;
            }

            public bool SetData (string data)
            {
                var val = data.Split(',');
                if (val[2] == "T")
                {
                    status = true;
                    SpeedKnots = Convert.ToDouble(val[5]);
                    SpeedKM = Convert.ToDouble(val[7]);
                }
                else
                    status = false;

                return status;

            }
        }

        class GPSData
        {
            public string gpsTime; // [1]
            public string latitude; //[2]
            public string longitude; //[4]
            public int satellites; //[7]
            public double dilution;//[8]
            public double elevation; //[9]
            public bool status = false;

            public GPSData()
            {
                status = false;
            }

            public bool SetData (string data)
            {
                var val = data.Split(',');

                if (val[6] != "0")
                {
                    if (val[1].Length > 6)
                        gpsTime = string.Format("{0}:{1}:{2}", val[1].Substring(0, 2), val[1].Substring(2, 2), val[1].Substring(4,2));
                    else
                        gpsTime = "";
                    int dot = val[2].IndexOf('.');
                    latitude = string.Format("{0}° {1}' {2}.{3}\" ", val[2].Substring(0,dot-2),val[2].Substring(dot-2,2),val[2].Substring(dot+1,2),val[2].Substring(dot+3));
                    latitude += val[3] == "N" ? "North" : "South";

                    dot = val[4].IndexOf('.');

                    longitude = string.Format("{0}° {1}' {2}.{3}\" ", val[4].Substring(0, dot - 2), val[4].Substring(dot - 2, 2), val[4].Substring(dot + 1, 2),val[4].Substring(dot+3));
                    longitude += val[5] == "E" ? "East" : "West";
                    satellites = Convert.ToUInt16(val[7]);
                    dilution = Convert.ToDouble(val[8]);
                    elevation = Convert.ToDouble(val[9]);
                    status = true;
                    
                }
                else
                    status = false;

                return status;
            }
              
        }

        public async Task<bool> Initialize()
        {
            bInitialize = false;
            try
            {
                string aqs = SerialDevice.GetDeviceSelector();
                //Enumerate all serial devices
                DeviceInformationCollection dis = await DeviceInformation.FindAllAsync(aqs);
                //Since we have only one device, it is safe to assume the first device 
                DeviceInformation selectedDevice = dis[0];
                
                //Configure the Serial port
                sd = await SerialDevice.FromIdAsync(selectedDevice.Id);
                sd.WriteTimeout = TimeSpan.FromMilliseconds(TIMEOUT);
                sd.ReadTimeout = TimeSpan.FromMilliseconds(TIMEOUT);
                sd.BaudRate = 9600;
                sd.Parity = SerialParity.None;
                sd.StopBits = SerialStopBitCount.One;
                sd.DataBits = 8;
                sd.Handshake = SerialHandshake.XOnXOff;
                
                if (sd != null)
                {
                    
                    dr = new DataReader(sd.InputStream);
                    dr.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                    dr.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;
                    bInitialize = true;
                }
            }
            catch (Exception ex)
            {
                    Debug.WriteLine("SerialReceiver failed to initialize!" + ex.Message);
            }

            return bInitialize;
        }


        public async Task<string> ReadAsync(uint bufferLength)
        {

            String str = ""; // new StringBuilder();
            GPSData gps = new GPSData();
            GPSSpeed gs = new GPSSpeed();
            string retVal = "GPS not initialised";

            try
            {
                // Once we have written the contents successfully we load the stream. By getting the 
                //right bufferlength, we can get the read more accurate without over or under buffer read
                bufferLength = await dr.LoadAsync(bufferLength);

                while (dr.UnconsumedBufferLength > 0)
                {
                    str += dr.ReadString(bufferLength) + "\n";
                    //if (str.Contains("GGA"))
                        //break;
                }

                if (str == "")
                    return "GPS Data not available";

                var dataGPS = str.Substring(str.IndexOf("GGA")).Split('\n');
                var dataSpeed = str.Substring(str.IndexOf("VTG")).Split('\n');


                if (dataGPS.Count() > 0 && gps.SetData(dataGPS[0]))
                {
                    retVal = string.Format("Time: {0}; Latitude: {1}; Longitude: {2}; Elevation: {3}; Satellites: {4}; ",
                                            gps.gpsTime, gps.latitude, gps.longitude, gps.elevation, gps.satellites);
                    latLong = string.Format("{0}; {1}", gps.latitude, gps.longitude);
                }

                if (dataSpeed.Count() > 0 && gs.SetData(dataSpeed[0]))
                    retVal += string.Format("Speed in Km: {0}", gs.SpeedKM);

                Speed = gs.SpeedKM;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Reading serial data failed!" + ex.Message);
                return "";
            }

            return retVal;
        }

    }



}

