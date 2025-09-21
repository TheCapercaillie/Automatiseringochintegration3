using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace OTSystem
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string logFile = "OTSystem_log.txt";
            try
            {
                int port = int.Parse(config["Modbus:Port"] ?? "1502");
                int authKey = int.Parse(config["Modbus:AuthKey"] ?? "48879");

                File.AppendAllText(logFile, $"[INFO] Starting OTSystem with port {port} and AuthKey {authKey} - {DateTime.Now}\n");

                var ics = new IndustrialControlSystem(port, authKey);
                ics.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OT] Failed to start OTSystem: {ex.Message}");
                File.AppendAllText(logFile, $"[ERROR] Failed to start OTSystem: {ex.Message} - {DateTime.Now}\n");
                throw;
            }
        }
    }
}
