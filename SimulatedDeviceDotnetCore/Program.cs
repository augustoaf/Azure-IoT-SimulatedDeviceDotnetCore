using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using System.Security.Cryptography.X509Certificates;
using System.IO;

namespace SimulatedDeviceDotnetCore
{
    class Program
    {
        //DPS Scope ID
        private static string s_idScope = "teste";//Environment.GetEnvironmentVariable("DPS_IDSCOPE");
        //keys from DPS enrollment group. 
        private const string enrollmentGroupPrimaryKey = "teste";
        private const string enrollmentGroupSecondaryKey = "teste";
        //Registration Id for this Device - required if using DPS
        private static string registrationId = "device2";

        //Device String Connection - Use this or DPS info above to connect the device - add GatewayHostName at the end if a downstream device using a Edge Gateway
        private static string deviceStringConnection = "HostName=teste.azure-devices.net;DeviceId=device2;SharedAccessKey=teste";
        //private static string deviceStringConnection = "HostName=iothubbyaugusto.azure-devices.net;DeviceId=device1;SharedAccessKey=bGyzWD5D04IOJDSiRK13tYHizgrMqBFa+MfwEB+eauo=;GatewayHostName=52.184.226.192";

        private static int counter = 0;

        static void Main(string[] args)
        {
            bool dpsInfoOk = !string.IsNullOrWhiteSpace(s_idScope) && !string.IsNullOrWhiteSpace(enrollmentGroupPrimaryKey) &&
                 !string.IsNullOrWhiteSpace(enrollmentGroupSecondaryKey) && !string.IsNullOrWhiteSpace(registrationId);
            bool directConnection = !string.IsNullOrWhiteSpace(deviceStringConnection);

            if (!(dpsInfoOk || directConnection))
            {
                Console.WriteLine("ID Scope, Keys and Registration ID must be provided if using DPS, otherwise fill the Device Connection String");
                Console.ReadLine();
            }
            else
            {
                DeviceClient deviceClient;

                //connect device directly if device string connection is known
                if (!string.IsNullOrWhiteSpace(deviceStringConnection))
                {
                    //this install a root certificate in the OS - applicable if you using this device as a downstream to connect through a Edge Gateway. 
                    if (deviceStringConnection.Contains("GatewayHostName"))
                    {
                        InstallCACert();
                    }

                    deviceClient = DeviceClient.CreateFromConnectionString(deviceStringConnection, TransportType.Mqtt_Tcp_Only);
                }
                else
                {
                    //Provision through DPS and return DeviceClient object
                    ProvisioningDeviceClientWrapper provisionWrapper = new ProvisioningDeviceClientWrapper(
                        s_idScope, registrationId, enrollmentGroupPrimaryKey, enrollmentGroupSecondaryKey);

                    deviceClient = provisionWrapper.RunAsync().GetAwaiter().GetResult();
                }

                // Create a handler for the direct method call
                deviceClient.SetMethodHandlerAsync("RestartCounter", RestartCounter, null).Wait();

                //send data
                sendTelemetryData(deviceClient);
                Console.ReadLine();
            }

        }

        private static async void sendTelemetryData(DeviceClient deviceClient)
        {
            if (deviceClient != null)
            {
                Console.WriteLine("DeviceClient OpenAsync.");
                await deviceClient.OpenAsync().ConfigureAwait(false);

                while (true)
                {
                    counter = counter + 1;

                    Message message = new Message(Encoding.UTF8.GetBytes(counter.ToString()));
                    await deviceClient.SendEventAsync(message).ConfigureAwait(false);

                    Console.WriteLine("Message sent from " + registrationId + ": " + counter.ToString());
                    await Task.Delay(5000);

                }

                await deviceClient.CloseAsync().ConfigureAwait(false);

            }

        }

        // Handle the direct method call - this method restart the counter according the paylod (parameter value) received
        private static Task<MethodResponse> RestartCounter(MethodRequest methodRequest, object userContext)
        {
            try
            {
                var data = Encoding.UTF8.GetString(methodRequest.Data);

                if (!string.IsNullOrWhiteSpace(data) && !(data == "null"))
                {
                    // Remove quotes from data, if any
                    data = data.Replace("\"", "");

                    counter = Convert.ToInt32(data);
                }
                else
                {
                    counter = 0;
                }
                Console.WriteLine("Counter updated to: " + counter);

                // Acknowledge the direct method call with a 200 success message.
                string result = "{\"result\":\"Executed direct method: " + methodRequest.Name + "\"}";
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 200));
            }
            catch
            {
                // Acknowledge the direct method call with a 400 error message.
                string result = "{\"result\":\"Invalid parameter\"}";
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 400));
            }

        }

        /// <summary>
        /// Add certificate in local cert store (at your OS) for use by downstream device
        /// client for secure connection to IoT Edge runtime.
        ///
        ///    Note: On Windows machines, if you have not run this from an Administrator prompt,
        ///    a prompt will likely come up to confirm the installation of the certificate.
        ///    This usually happens the first time a certificate will be installed.
        /// </summary>
        static void InstallCACert()
        {
            string trustedCACertPath = "azure-iot-test-only.root.ca.cert.pem";
            if (!string.IsNullOrWhiteSpace(trustedCACertPath))
            {
                Console.WriteLine("User configured CA certificate path: {0}", trustedCACertPath);
                if (!File.Exists(trustedCACertPath))
                {
                    // cannot proceed further without a proper cert file
                    Console.WriteLine("Certificate file not found: {0}", trustedCACertPath);
                    throw new InvalidOperationException("Invalid certificate file.");
                }
                else
                {
                    Console.WriteLine("Attempting to install CA certificate: {0}", trustedCACertPath);
                    X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(new X509Certificate2(X509Certificate.CreateFromCertFile(trustedCACertPath)));
                    Console.WriteLine("Successfully added certificate: {0}", trustedCACertPath);
                    store.Close();
                }
            }
            else
            {
                Console.WriteLine("trustedCACertPath was not set or null, not installing any CA certificate");
            }
        }
    }
}
