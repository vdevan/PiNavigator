using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.Devices.Gpio;
using Windows.Devices.Pwm;
using Microsoft.IoT.Lightning.Providers;
using Windows.Devices;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Navigator
{
    class PWMController
    {
        const int MOTORLEFTN = 5; //Left Motor Negative Output - Low for forward; High for Backward
        const int MOTORLEFTP = 6; // Left Motor Positive Output - High for forward; Low for Backward
        const int MOTORRIGHTN = 25; // Right Motor Negative Output - Low for foward; High for Backward
        const int MOTORRIGHTP = 16; //Right Motor Positive Output - High for forward; Low for Backward
        const int PWMLEFT = 13;
        const int PWMRIGHT = 12;

        PwmPin pwmMotorLeft; //PWM for Left Motor
        PwmPin pwmMotorRight; //PWM for Right Motor. //You can fine tune the duty cycle for both Left and right motor in sync
        GpioPin MotorLeftNegative;  //Left Motor T1
        GpioPin MotorLeftPositive;  //Left Motor T2
        GpioPin MotorRightNegative; //Right Motor T1
        GpioPin MotorRightPositive; //Right Motor T2

        public bool bInit;
        public bool bStop;
        const double INITIALPOWER = 100;
        const double FREQUENCY = 100;
        private double motorSpeed;
        PwmController pwmController;
        GpioController gpio;

        public PWMController()
        {
            bInit = false;
            bStop = false;
        }

        //We will set the duty cycle of both the Motors. The procedure will return previous values
        public double SetSpeed(int spValue)
        {
            double ms = motorSpeed;
            motorSpeed = spValue;
            if (bInit)
            {
                pwmMotorLeft.SetActiveDutyCyclePercentage(spValue / 100);
                pwmMotorRight.SetActiveDutyCyclePercentage(spValue / 100);
            }

            return ms;
        }

        /* We will initialize the PWM controller. For this we will be using Microsoft's Lightnening Provider
         * In Order this to work we neeed to change the Default Controller Driver to Direct Memory Access 
         * Mapped driver. The Project Manifest file needs to be modified and low level devices capability
         * must be added. See documentation for details
         * */
          
        public async Task<bool> Initialize()
        {
            bInit = false;
            try
            {
                if (LightningProvider.IsLightningEnabled)
                {
                    LowLevelDevicesController.DefaultProvider = LightningProvider.GetAggregateProvider();
                }

                var pwmControllers = await PwmController.GetControllersAsync(LightningPwmProvider.GetPwmProvider());
                if (pwmControllers == null)
                    return false;

                pwmController = pwmControllers[1];
                pwmController.SetDesiredFrequency(FREQUENCY); //Min: 24hz to Max: 1000 hz

                gpio = await GpioController.GetDefaultAsync();
                if (gpio == null)
                    return false;

                pwmController.SetDesiredFrequency(FREQUENCY); //Min: 24hz to Max: 1000 hz
                bInit = true;
                
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception Error {0} occured", e.ToString());
                bInit = false;
            }
            return true;
        }

        public void SetMotorConfig()
        {
            //Connect the motor Enabler to PWM pin 
            pwmMotorLeft = pwmController.OpenPin(PWMLEFT);
            pwmMotorLeft.SetActiveDutyCyclePercentage(INITIALPOWER / 100);
            pwmMotorLeft.Stop();

            pwmMotorRight = pwmController.OpenPin(PWMRIGHT);
            pwmMotorRight.SetActiveDutyCyclePercentage(INITIALPOWER / 100);
            pwmMotorRight.Stop();

            //Assign the Motor Terminals to gpio pins
            MotorLeftNegative = gpio.OpenPin(MOTORLEFTN, GpioSharingMode.Exclusive);
            MotorLeftPositive = gpio.OpenPin(MOTORLEFTP, GpioSharingMode.Exclusive);
            MotorRightNegative = gpio.OpenPin(MOTORRIGHTN, GpioSharingMode.Exclusive);
            MotorRightPositive = gpio.OpenPin(MOTORRIGHTP, GpioSharingMode.Exclusive);

            //set Input or Output of the pins
            MotorLeftNegative.SetDriveMode(GpioPinDriveMode.Output);
            MotorLeftPositive.SetDriveMode(GpioPinDriveMode.Output);
            MotorRightNegative.SetDriveMode(GpioPinDriveMode.Output);
            MotorRightPositive.SetDriveMode(GpioPinDriveMode.Output);

            /* ******Set initial values for output ports *****
                * Note the following
                * Negative     Positive    Result
                *   Low        High        Forward
                *   High       Low         Backward
                *   Low        Low         Stop
                *   High       High        Right
                *   */
            MotorLeftNegative.Write(GpioPinValue.Low);
            MotorLeftPositive.Write(GpioPinValue.Low);
            MotorRightNegative.Write(GpioPinValue.Low);
            MotorRightPositive.Write(GpioPinValue.Low);
        }

        public void  Motordirection(App.DIRECTION d)
        {
            if (!bStop)
                SetMotorDirection(d);
        }

        public void StopNav()
        {
            MotorLeftNegative.Write(GpioPinValue.Low);
            MotorLeftPositive.Write(GpioPinValue.Low);
            MotorRightNegative.Write(GpioPinValue.Low);
            MotorRightPositive.Write(GpioPinValue.Low);
            pwmMotorLeft.Stop();
            pwmMotorRight.Stop();
        }

        private void SetMotorDirection(App.DIRECTION d)
        {
            //first stop the motors, change direction and then start the motors
            pwmMotorLeft.Stop();
            pwmMotorRight.Stop();

            switch (d)
            {
                case App.DIRECTION.FORWARD:
                    //Both Motors in the same direction - Forward
                    MotorLeftNegative.Write(GpioPinValue.Low);
                    MotorLeftPositive.Write(GpioPinValue.High);
                    MotorRightNegative.Write(GpioPinValue.Low);
                    MotorRightPositive.Write(GpioPinValue.High);
                    break;

                case App.DIRECTION.REVERSE:
                    //Both Motors in the same direction but Reverse
                    MotorLeftNegative.Write(GpioPinValue.High);
                    MotorLeftPositive.Write(GpioPinValue.Low);
                    MotorRightNegative.Write(GpioPinValue.High);
                    MotorRightPositive.Write(GpioPinValue.Low);
                    break;

                case App.DIRECTION.LEFT:
                    //One of the Motor is in the reverse, while the other forward - Friction turn
                    MotorLeftNegative.Write(GpioPinValue.Low);
                    MotorLeftPositive.Write(GpioPinValue.High); //Left Motor Forward
                    MotorRightNegative.Write(GpioPinValue.High);
                    MotorRightPositive.Write(GpioPinValue.Low); //Right Motor Reverse
                    break;

                case App.DIRECTION.RIGHT:
                    //One of the Motor is in the reverse, while the other forward - Friction turn
                    MotorLeftNegative.Write(GpioPinValue.High);
                    MotorLeftPositive.Write(GpioPinValue.Low);
                    MotorRightNegative.Write(GpioPinValue.Low);
                    MotorRightPositive.Write(GpioPinValue.High);
                    break;
            }
            pwmMotorLeft.Start();
            pwmMotorRight.Start();
        }
    }
}
