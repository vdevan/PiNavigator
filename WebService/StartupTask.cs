using System;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.AppService;
using Windows.System.Threading;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

/* The Main Socket Service for the remote Control. This is built using Background Application Template
 * and then converted to Service. The service will be started by calling application.
 * This Application will listen to HTTP Port 8090 and then pass the information received from web client 
 * to Pi application. Pi application will also communicate with the browser by sending feedback messages 
 * to this application. This application will host the page for Web browser
 * ****/

namespace WebService
{
    public sealed class StartupTask : IBackgroundTask
    {
        private BackgroundTaskDeferral deferral;
        private AppServiceConnection asc;

        // If you start any asynchronous methods here, prevent the task
        // from closing prematurely by using BackgroundTaskDeferral as
        // described in http://aka.ms/backgroundtaskdeferral
        //

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            deferral = taskInstance.GetDeferral();
            var ws = new WebService();

            /* Comment the next three lines to test this application as Background Application */
            var td = taskInstance.TriggerDetails as AppServiceTriggerDetails;
            asc = td.AppServiceConnection;
            ws.SetConnection (asc);
            

            await ThreadPool.RunAsync(wi =>
            {
                ws.Start();
            });

        }
    }
}
