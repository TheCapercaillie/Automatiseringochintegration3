using System;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EasyModbus;
using ITSystem;
using System.Security.Cryptography;
using System.IO;

namespace IntegrationSystem
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Set up configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Load configuration settings
            string connectionString = config.GetConnectionString("OrdersDB") ?? throw new Exception("OrdersDB connection string missing");
            string host = config["Modbus:Host"] ?? "127.0.0.1";
            int port = int.Parse(config["Modbus:Port"] ?? "1502");
            int authKey = int.Parse(config["Modbus:AuthKey"] ?? "48879");
            int pollInterval = int.Parse(config["Integration:PollingIntervalMs"] ?? "5000");
            int retryAttempts = int.Parse(config["Integration:RetryAttempts"] ?? "3");

            // Set up dependency injection for EF Core
            var services = new ServiceCollection();
            services.AddDbContext<OrdersDbContext>(options => options.UseSqlServer(connectionString));
            var provider = services.BuildServiceProvider();

            // Initialize Modbus client
            ModbusClient modbusClient = new ModbusClient(host, port);
            string logFile = "Integration_log.txt";

            // Ensure log file is created or cleared
            File.WriteAllText(logFile, $"[INFO] Integration System started - {DateTime.Now}\n");

            while (true)
            {
                try
                {
                    if (!modbusClient.Connected)
                    {
                        ConnectWithRetry(modbusClient, host, port, retryAttempts, logFile);
                    }

                    // Clear and set up console layout
                    Console.Clear();
                    Console.WriteLine("=== Integration System Dashboard ===");
                    Console.WriteLine("-----------------------------------");
                    Console.WriteLine("Status: " + (modbusClient.Connected ? "Connected" : "Disconnected"));
                    Console.WriteLine("Current Order: ");
                    using (var scope = provider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                        var inProgressOrder = db.Orders.FirstOrDefault(o => o.Status == OrderStatus.InProgress);
                        if (inProgressOrder != null)
                        {
                            Console.WriteLine($"  ID: {inProgressOrder.Id}");
                            Console.WriteLine($"  Product: {inProgressOrder.Product}");
                            Console.WriteLine($"  Quantity: {inProgressOrder.Quantity}");
                            Console.WriteLine($"  Progress: {inProgressOrder.Progress}/{inProgressOrder.Quantity}");
                            Console.WriteLine($"  Runtime: {inProgressOrder.RuntimeSeconds ?? 0}s");
                            Console.WriteLine($"  Status: {inProgressOrder.Status}");
                        }
                        else
                        {
                            Console.WriteLine("  No active order.");
                        }
                    }

                    Console.WriteLine("-----------------------------------");
                    Console.WriteLine("Recent Activity:");
                    // Poll for new orders and update
                    using (var scope = provider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                        var pendingOrders = db.Orders
                            .Where(o => o.Status == OrderStatus.Pending)
                            .OrderBy(o => o.CreatedAt)
                            .Take(1)
                            .ToList();

                        foreach (var order in pendingOrders)
                        {
                            int[] statusRegs = modbusClient.ReadInputRegisters(3, 1);
                            if (statusRegs[0] == 1)
                            {
                                Console.WriteLine($"[INFO] OT busy, skipping order {order.Id}");
                                File.AppendAllText(logFile, $"[INFO] OT busy, skipping order {order.Id} - {DateTime.Now}\n");
                                continue;
                            }

                            int nonce = RandomNumberGenerator.GetInt32(0, short.MaxValue);
                            bool success = false;
                            for (int attempt = 1; attempt <= retryAttempts; attempt++)
                            {
                                try
                                {
                                    modbusClient.WriteMultipleRegisters(0, new int[] { order.Id, order.Quantity });
                                    modbusClient.WriteMultipleRegisters(11, new int[] { authKey, nonce });
                                    modbusClient.WriteSingleCoil(1, true);

                                    order.Status = OrderStatus.InProgress;
                                    db.SaveChanges();
                                    Console.WriteLine($"[INFO] Started order {order.Id} (Qty: {order.Quantity}, Nonce: {nonce})");
                                    File.AppendAllText(logFile, $"[INFO] Started order {order.Id} (Qty: {order.Quantity}, Nonce: {nonce}) - {DateTime.Now}\n");
                                    success = true;
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[ERROR] Failed to start order {order.Id} (attempt {attempt}/{retryAttempts}): {ex.Message}");
                                    File.AppendAllText(logFile, $"[ERROR] Failed to start order {order.Id} (attempt {attempt}): {ex.Message} - {DateTime.Now}\n");
                                    Thread.Sleep(1000);
                                }
                            }
                            if (!success)
                            {
                                order.Status = OrderStatus.Failed;
                                db.SaveChanges();
                                Console.WriteLine($"[ERROR] Order {order.Id} marked as Failed after {retryAttempts} attempts");
                                File.AppendAllText(logFile, $"[ERROR] Order {order.Id} failed after {retryAttempts} attempts - {DateTime.Now}\n");
                            }
                        }

                        // Monitor OT production
                        int[] registers = modbusClient.ReadInputRegisters(1, 4);
                        int produced = registers[0];
                        int runtime = registers[1];
                        bool[] doneInputs = modbusClient.ReadDiscreteInputs(1, 1);

                        if (produced < 0 || runtime < 0)
                        {
                            Console.WriteLine($"[SECURITY] Invalid Modbus data: Produced={produced}, Runtime={runtime}");
                            File.AppendAllText(logFile, $"[SECURITY] Invalid Modbus data: Produced={produced}, Runtime={runtime} - {DateTime.Now}\n");
                        }
                        else
                        {
                            Console.WriteLine($"[INFO] Produced/Runtime = {produced}/{runtime} units/seconds");
                        }

                        var inProgressOrder = db.Orders.FirstOrDefault(o => o.Status == OrderStatus.InProgress);
                        if (inProgressOrder != null)
                        {
                            bool success = false;
                            for (int attempt = 1; attempt <= retryAttempts; attempt++)
                            {
                                try
                                {
                                    inProgressOrder.Progress = Math.Min(produced, inProgressOrder.Quantity);
                                    inProgressOrder.RuntimeSeconds = runtime;

                                    if (doneInputs[0] && produced >= inProgressOrder.Quantity)
                                    {
                                        inProgressOrder.Status = OrderStatus.Completed;
                                        db.MachineRuntimes.Add(new MachineRuntime { Timestamp = DateTime.Now, RuntimeSeconds = runtime });
                                        db.SaveChanges();
                                        Console.WriteLine($"[INFO] Completed order {inProgressOrder.Id} with runtime {runtime}s");
                                        File.AppendAllText(logFile, $"[INFO] Completed order {inProgressOrder.Id} with runtime {runtime}s - {DateTime.Now}\n");
                                    }
                                    else
                                    {
                                        db.SaveChanges();
                                        Console.WriteLine($"[INFO] Updated order {inProgressOrder.Id}: Progress={inProgressOrder.Progress}, Status={inProgressOrder.Status}");
                                        File.AppendAllText(logFile, $"[INFO] Updated order {inProgressOrder.Id}: Progress={inProgressOrder.Progress}, Status={inProgressOrder.Status}, Runtime={runtime}s - {DateTime.Now}\n");
                                    }
                                    success = true;
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[ERROR] Failed to update order {inProgressOrder.Id} (attempt {attempt}/{retryAttempts}): {ex.Message}");
                                    File.AppendAllText(logFile, $"[ERROR] Failed to update order {inProgressOrder.Id} (attempt {attempt}): {ex.Message} - {DateTime.Now}\n");
                                    Thread.Sleep(1000);
                                }
                            }
                            if (!success)
                            {
                                Console.WriteLine($"[ERROR] Failed to update order {inProgressOrder.Id} after {retryAttempts} attempts");
                                File.AppendAllText(logFile, $"[ERROR] Failed to update order {inProgressOrder.Id} after {retryAttempts} attempts - {DateTime.Now}\n");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Cycle error: {ex.Message}");
                    File.AppendAllText(logFile, $"[ERROR] Cycle error: {ex.Message} - {DateTime.Now}\n");
                    if (modbusClient.Connected) modbusClient.Disconnect();
                    Thread.Sleep(10000);
                }

                Thread.Sleep(pollInterval);
            }
        }

        private static void ConnectWithRetry(ModbusClient client, string host, int port, int retries, string logFile)
        {
            for (int attempt = 1; attempt <= retries; attempt++)
            {
                try
                {
                    Console.WriteLine($"[INFO] Attempting to connect to Modbus {host}:{port} (try {attempt}/{retries})...");
                    client.Connect();
                    Console.WriteLine("[INFO] Connected to Modbus.");
                    File.AppendAllText(logFile, $"[INFO] Connected to Modbus {host}:{port} - {DateTime.Now}\n");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Connection failed (try {attempt}/{retries}): {ex.Message}");
                    File.AppendAllText(logFile, $"[ERROR] Modbus connection failed (try {attempt}/{retries}): {ex.Message} - {DateTime.Now}\n");
                    Thread.Sleep(2000);
                    if (attempt == retries) throw;
                }
            }
        }
    }
}

