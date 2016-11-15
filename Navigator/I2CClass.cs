using System;
using System.Threading.Tasks;
using Windows.Devices.I2c;
using System.Diagnostics;
using Windows.Devices.Enumeration;
using Microsoft.IoT.Lightning.Providers;



/* *****************************
 * GPS and Compass Module Data. Manuals for these registers are located in 
 * "C:\Users\Vijaya\Documents\Data\Books Library\Technical Books\GPSModule"
 * Also check http://www.meccanismocomplesso.org/en/arduino-magnetic-magnetic-magnetometer-hmc5883l/
 * for complete article on using the compass module
 * 
 * */

namespace Navigator
{
    public class I2CClass
    {
        const byte deviceAddress = 0x1e; //0x02 for Lego & 0x1e for Compass
        const byte deviceType = 0x0a;
        const string I2CControllerName = "I2C1";
        const string CompassSign = "H43";
        const byte SignB = 0x34;
        const byte SignC = 0x33;
        const bool UPSIDEDOWN = false;
        private I2cDevice compass = null;
        public bool init = false;        //Variable to check if device is initialized
        byte[] WriteBuffer;
        byte[] ReadBuffer;
        const int DELAY = 70;
        double scaleFactor;
        
        class MagnetometerRaw
        {
            public Int16 XaxisRaw;
            public Int16 YaxisRaw;
            public Int16 ZaxisRaw;
        }

        class MagmetometerScaled
        {
            public double XaxisScaled;
            public double YaxisScaled;
            public double ZaxisScaled;

        }
        enum CompassRegisters : byte
        {
            CONFIGURATIONA = 00, //Configuration Register A Read/Write
            CONFIGURATIONB = 01, //Configuration Register B Read/Write
            MODE = 02, //Mode Register Read/Write
            DATAXHI = 03, //Data Output X MSB Register Read
            DATAXLO = 04, //Data Output X LSB Register Read
            DATAZHI = 05, //Data Output Z MSB Register Read
            DATAZLO = 06, //Data Output Z LSB Register Read
            DATAYHI = 07, //Data Output Y MSB Register Read
            DATAYLO = 08, //Data Output Y LSB Register Read
            STATUS = 09, //Status Register Read
            IDENTIFICATIONA = 10, //Identification Register A Read
            IDENTIFICATIONB = 11, //Identification Register B Read
            IDENTIFICATIONC = 12 //Identification Register C Read
        }

        double[] resolution = new double[] { 0.73f, 0.92f, 1.22f, 1.52f, 2.27f, 2.56f, 3.03f, 4.35f };

        MagmetometerScaled scaledValue = new MagmetometerScaled();
        MagnetometerRaw rawValue = new MagnetometerRaw();

        public async Task<bool> Initialize()
        {
            try
            {
                //Instantiate the I2CConnectionSettings using the device address of the BMP280
                I2cConnectionSettings settings = new I2cConnectionSettings(deviceAddress);

                //Set the I2C bus speed of connection to fast mode
                settings.BusSpeed = I2cBusSpeed.FastMode;

                if (LightningProvider.IsLightningEnabled)
                {
                    I2cController controller = (await I2cController.GetControllersAsync(LightningI2cProvider.GetI2cProvider()))[0];
                    //I2cDevice sensor = controller.GetDevice(new I2cConnectionSettings(0x40));
                    compass = controller.GetDevice(settings);

                }

                else
                {
                    //Use the I2CBus device selector to create an advanced query syntax string
                    string aqs = I2cDevice.GetDeviceSelector(I2CControllerName);

                    //Use the Windows.Devices.Enumeration.DeviceInformation class to create a collection using the advanced query syntax string
                    DeviceInformationCollection dis = await DeviceInformation.FindAllAsync(aqs);

                    //Instantiate the the BMP280 I2C device using the device id of the I2CBus and the I2CConnectionSettings
                    compass = await I2cDevice.FromIdAsync(dis[0].Id, settings);
                }

                //Check if device was found
                if (compass == null)
                {
                    return false;
                }

            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception: " + e.Message + "\n" + e.StackTrace);
            }

            return CheckId();

        }

        public void configCompass()
        {
            /* Configuration A: No. Of Samples Averaged = 2
             * CRA7 CRA6    CRA5    CRA4    CRA3    CRA2    CRA1    CRA0
             * (0)  MA1(0)  MA0(0)  DO2(1)  DO1(0)  DO0(0)  MS1(0)  MS0(0)  =>Default : 0x10
             *  0   1       1       1       1       0       0       0       =>Value : 0x78
             *  CRA7 Reserved; MA Samples Averaged 1 to 8; DO Data Out Bit rate; MS Bias adustment to measurement
             *  Samples Averaged = 2 and Output speed = 75 hertz       
             *  Update frequency has to be more than 13.33ms.
             * */

            /* Configuration Register B is for setting device gain. Leave at the default value
             * CRB7	    CRB6    CRB5	CRB4	CRB3	CRB2	CRB1	CRB0
             * GN2(0)	GN1(0)	GN0(1)	0	    0	    0	    0	    0   =>Default : 0x20
             * 0        0       1       0       0       0       0       0   =>Value : 0x20
             * Gain will be 1090 Gauss - default
             * CRB4 to CRB0 must be 0
             * sensitivity is determined by the recommended Sensor Field Range  GN0 ~ GN2
             * */

            /* Mode Register - Last two bit selects the Mode 
             * 00 -> Continuous mode; 01 -> Single Shot mode; 11 & 10 -> Idle mode.
             * We select continuous mode here */


            WriteBuffer = new byte[] { (byte)CompassRegisters.CONFIGURATIONA, 0x78 };
            compass.Write(WriteBuffer);

            WriteBuffer = new byte[] { (byte)CompassRegisters.CONFIGURATIONB, 0x20 };
            compass.Write(WriteBuffer);
            scaleFactor = resolution[1]; //Values of GN2, GN1 & GN0 of Configuration Register B

            WriteBuffer = new byte[] { (byte)CompassRegisters.MODE, 0x00 };
            compass.Write(WriteBuffer);

        }

        private bool CheckId()
        {
            WriteBuffer = new byte[] { (byte)CompassRegisters.IDENTIFICATIONA };
            ReadBuffer = new byte[3];

            //Read the device signatureA
            compass.WriteRead(WriteBuffer, ReadBuffer);

            string str = System.Text.Encoding.UTF8.GetString(ReadBuffer);
            //Verify the device signature
            if (str != CompassSign)
            {
                return false;
            }
            return true;
        }

        //Get the XYZ axis of the compass. Store it in rawValue and scaledValue class buffer
        public string GetXYZ()
        {
            try
            {

                byte[] write = new byte[] { (byte)CompassRegisters.DATAXHI };
                byte[] read = new byte[6];

                timedelay();
                compass.WriteRead(write, read);

                rawValue.XaxisRaw = (short)(read[0] << 8 | read[1]); //swap MSB & LSB to convert Big-Endian returned
                if (rawValue.XaxisRaw == -4096) //Overflow error
                    return "";

                rawValue.ZaxisRaw = (short)(read[2] << 8 | read[3]);
                if (rawValue.ZaxisRaw == -4096) //Overflow error
                    return "";

                rawValue.YaxisRaw = (short)(read[4] << 8 | read[5]);
                if (UPSIDEDOWN)
                    rawValue.YaxisRaw = (short)-rawValue.YaxisRaw;

                if (rawValue.YaxisRaw == -4096) //Overflow error
                    return "";

                /****** Note the convention of heading direction***********
                 * y = 0, x < 0 = South
                 * y = 0, x > 0 = 0.0 North
                 * ********************************************************/


                scaledValue.XaxisScaled = rawValue.XaxisRaw * scaleFactor;
                scaledValue.YaxisScaled = rawValue.YaxisRaw * scaleFactor;
                scaledValue.ZaxisScaled = rawValue.ZaxisRaw * scaleFactor;

                double direction = GetDirection();

                return string.Format("Raw Value: X = {0:N0}; Y = {1:N0}; Z = {2:N0};\nScaledValue: X = {3:N0}; Y = {4:N0}; Z = {5:N0};\nDirection: {6:N2}degrees;\n",   
                                    rawValue.XaxisRaw, rawValue.YaxisRaw, rawValue.ZaxisRaw, scaledValue.XaxisScaled, 
                                    scaledValue.YaxisScaled, scaledValue.ZaxisScaled,direction) ;
            }
            catch
            {
                return "";
            }


        }

        public double GetDirection()
        {
            double direction = 0;            
            direction = Math.Atan2(scaledValue.YaxisScaled, scaledValue.XaxisScaled);

            /**** Compensation for Magnetic field. ********
             * Use the site http://www.magnetic-declination.com/ to get the declination angle of your city
             * Use the site http://www.wolframalpha.com/ to convert that to radian. Input
             * (12° 35') in radians 
             * in the input box at the site. Look for value of mrad (219.6 milli radian)
             * The example above is for Sydney. Use your city value. If the declination is East then
             * add to direction. if the declination is west, then subtract from direction.
             * ***********************/
            double declinationAngle = 219.6f/1000; //declination angle is East

            direction += declinationAngle; //direction -= if the declinationAngle is West

            // Correct for when signs are reversed.
            if (direction < 0)
                direction += 2* Math.PI;

            if (direction > 2 * Math.PI)
                direction -= 2 * Math.PI;

            return direction * 180.0 /Math.PI; //radians to degrees
              
        }

        private void timedelay()
        {
            System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(DELAY)).Wait();
        }


    }
}
