using System;
using System.Threading;
using Microsoft.Data.SqlClient;
using EasyModbus;
using Microsoft.Extensions.Configuration;

namespace IntegrationSystem
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Integration System started...");


            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string connectionString = config.GetConnectionString("OrdersDB");
            string host = config["Modbus:Host"] ?? "127.0.0.1";
            int port = int.Parse(config["Modbus:Port"] ?? "1502");
            int pollInterval = int.Parse(config["Integration:PollingIntervalMs"] ?? "5000");
            int retryAttempts = int.Parse(config["Integration:RetryAttempts"] ?? "3");

            ModbusClient modbusClient = new ModbusClient(host, port);

            while (true)
            {
                bool success = false;

                for (int attempt = 1; attempt <= retryAttempts; attempt++)
                {
                    try
                    {
                        if (!modbusClient.Connected)
                        {
                            Console.WriteLine($"[INT] Attempting to connect to Modbus {host}:{port} (try {attempt}/{retryAttempts})...");
                            modbusClient.Connect();
                            Console.WriteLine("[INT] Connected.");
                        }

                        int[] registers = modbusClient.ReadInputRegisters(0, 1);
                        int runtime = registers[0];

                        Console.WriteLine($"[INT] Runtime = {runtime} sekunder");

                        using (SqlConnection conn = new SqlConnection(connectionString))
                        {
                            conn.Open();
                            string sql = "INSERT INTO MachineRuntime (Timestamp, RuntimeSeconds) VALUES (@time, @runtime)";
                            using (SqlCommand cmd = new SqlCommand(sql, conn))
                            {
                                cmd.Parameters.AddWithValue("@time", DateTime.Now);
                                cmd.Parameters.AddWithValue("@runtime", runtime);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[INT] Error (try {attempt}/{retryAttempts}): {ex.Message}");
                        Thread.Sleep(1000);
                    }
                }

                if (!success)
                {
                    Console.WriteLine($"[INT] Failed after {retryAttempts} attempts. Skipping this cycle.");
                }

                Thread.Sleep(pollInterval);
            }
        }
    }
}
