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
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string connectionString = config.GetConnectionString("OrdersDB") ?? throw new Exception("OrdersDB connection string missing");
            string host = config["Modbus:Host"] ?? "127.0.0.1";
            int port = int.Parse(config["Modbus:Port"] ?? "1502");
            int authKey = int.Parse(config["Modbus:AuthKey"] ?? "48879");
            int pollInterval = int.Parse(config["Integration:PollingIntervalMs"] ?? "5000");
            int retryAttempts = int.Parse(config["Integration:RetryAttempts"] ?? "3");

            var services = new ServiceCollection();
            services.AddDbContext<OrdersDbContext>(options => options.UseSqlServer(connectionString));
            var provider = services.BuildServiceProvider();

            ModbusClient modbusClient = new ModbusClient(host, port);
            string logFile = "Integration_log.txt";

            File.WriteAllText(logFile, $"[INFO] Integration System started - {DateTime.Now}\n");

            while (true)
            {
                try
                {
                    if (!modbusClient.Connected)
                    {
                        ConnectWithRetry(modbusClient, host, port, retryAttempts, logFile);
                    }

                    using (var scope = provider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                        var pendingOrders = db.Orders
                            .Where(o => o.Status == OrderStatus.Pending)
                            .OrderBy(o => o.CreatedAt)
                            .ToList();

                        foreach (var order in pendingOrders)
                        {
                            int[] statusRegs = modbusClient.ReadInputRegisters(3, 1);
                            if (statusRegs[0] == 1)
                            {
                                Console.WriteLine($"[INT] OT busy, skipping order {order.Id}");
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
                                    Console.WriteLine($"[INT] Started order {order.Id} (Qty: {order.Quantity})");
                                    File.AppendAllText(logFile, $"[INFO] Started order {order.Id} (Qty: {order.Quantity}, Nonce: {nonce}) - {DateTime.Now}\n");
                                    success = true;
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[INT] Failed to start order {order.Id} (attempt {attempt}/{retryAttempts}): {ex.Message}");
                                    File.AppendAllText(logFile, $"[ERROR] Failed to start order {order.Id} (attempt {attempt}): {ex.Message} - {DateTime.Now}\n");
                                    Thread.Sleep(1000);
                                }
                            }

                            if (!success)
                            {
                                order.Status = OrderStatus.Failed;
                                db.SaveChanges();
                                Console.WriteLine($"[INT] Order {order.Id} marked as Failed after {retryAttempts} attempts");
                                File.AppendAllText(logFile, $"[ERROR] Order {order.Id} failed after {retryAttempts} attempts - {DateTime.Now}\n");
                            }
                        }
                    }

                    int[] registers = modbusClient.ReadInputRegisters(1, 4);
                    int produced = registers[0];
                    int runtime = registers[1];
                    bool[] doneInputs = modbusClient.ReadDiscreteInputs(1, 1);

                    if (produced < 0 || runtime < 0)
                    {
                        Console.WriteLine($"[INT] Invalid Modbus data: Produced={produced}, Runtime={runtime}");
                        File.AppendAllText(logFile, $"[SECURITY] Invalid Modbus data: Produced={produced}, Runtime={runtime} - {DateTime.Now}\n");
                        continue;
                    }

                    Console.WriteLine($"[INT] Produced/Runtime = {produced}/{runtime} units/seconds");
                    File.AppendAllText(logFile, $"[INFO] Polled OT: Produced={produced}, Runtime={runtime}s - {DateTime.Now}\n");

                    using (var scope = provider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
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
                                        db.SaveChanges();
                                        File.AppendAllText(logFile, $"[INFO] Completed order {inProgressOrder.Id} with runtime {runtime}s - {DateTime.Now}\n");

                                        db.MachineRuntimes.Add(new MachineRuntime { Timestamp = DateTime.Now, RuntimeSeconds = runtime });
                                        db.SaveChanges();
                                    }

                                    db.SaveChanges();
                                    success = true;
                                    Console.WriteLine($"[INT] Updated order {inProgressOrder.Id}: Progress={inProgressOrder.Progress}, Status={inProgressOrder.Status}");
                                    File.AppendAllText(logFile, $"[INFO] Updated order {inProgressOrder.Id}: Progress={inProgressOrder.Progress}, Status={inProgressOrder.Status}, Runtime={runtime}s - {DateTime.Now}\n");
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[INT] Failed to update order {inProgressOrder.Id} (attempt {attempt}/{retryAttempts}): {ex.Message}");
                                    File.AppendAllText(logFile, $"[ERROR] Failed to update order {inProgressOrder.Id} (attempt {attempt}): {ex.Message} - {DateTime.Now}\n");
                                    Thread.Sleep(1000);
                                }
                            }

                            if (!success)
                            {
                                Console.WriteLine($"[INT] Failed to update order {inProgressOrder.Id} after {retryAttempts} attempts");
                                File.AppendAllText(logFile, $"[ERROR] Failed to update order {inProgressOrder.Id} after {retryAttempts} attempts - {DateTime.Now}\n");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[INT] Cycle error: {ex.Message}");
                    File.AppendAllText(logFile, $"[ERROR] Cycle error: {ex.Message} - {DateTime.Now}\n");
                    if (modbusClient.Connected)
                    {
                        modbusClient.Disconnect();
                    }
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
                    Console.WriteLine($"[INT] Attempting to connect to Modbus {host}:{port} (try {attempt}/{retries})...");
                    client.Connect();
                    Console.WriteLine("[INT] Connected.");
                    File.AppendAllText(logFile, $"[INFO] Connected to Modbus {host}:{port} - {DateTime.Now}\n");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[INT] Connection failed (try {attempt}/{retries}): {ex.Message}");
                    File.AppendAllText(logFile, $"[ERROR] Modbus connection failed (try {attempt}/{retries}): {ex.Message} - {DateTime.Now}\n");
                    Thread.Sleep(2000);
                    if (attempt == retries)
                    {
                        File.AppendAllText(logFile, $"[ERROR] Failed to connect to Modbus after {retries} attempts, pausing before next cycle - {DateTime.Now}\n");
                        throw;
                    }
                }
            }
        }
    }
}
