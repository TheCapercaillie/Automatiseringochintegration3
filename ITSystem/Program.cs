using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;

namespace ITSystem
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();
            services.AddDbContext<OrdersDbContext>(options =>
                options.UseSqlServer(config.GetConnectionString("OrdersDB")));

            var provider = services.BuildServiceProvider();
            string logFile = "ITSystem_log.txt";

            try
            {
                using (var scope = provider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                    db.Database.EnsureCreated();
                    Console.WriteLine("DB connection OK");
                    File.AppendAllText(logFile, $"[INFO] Database connection established - {DateTime.Now}\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fel vid anslutning till databasen: {ex.Message}");
                File.AppendAllText(logFile, $"[ERROR] Database connection failed: {ex.Message} - {DateTime.Now}\n");
                return;
            }

            while (true)
            {
                Console.WriteLine("\n--- IT OrderApp ---");
                Console.WriteLine("1) Lista ordrar");
                Console.WriteLine("2) Skapa ny order");
                Console.WriteLine("0) Avsluta");
                Console.Write("> ");
                var input = Console.ReadLine()?.Trim();

                try
                {
                    using (var scope = provider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

                        if (input == "1")
                        {
                            var orders = db.Orders.ToList();
                            if (orders.Any())
                            {
                                foreach (var o in orders)
                                    Console.WriteLine($"{o.Id}: {o.Product} x{o.Quantity} [{o.Status}] (Progress: {o.Progress}, Runtime: {o.RuntimeSeconds ?? 0}s)");
                                File.AppendAllText(logFile, $"[INFO] Listed {orders.Count} orders - {DateTime.Now}\n");
                            }
                            else
                            {
                                Console.WriteLine("Inga ordrar hittades.");
                                File.AppendAllText(logFile, $"[INFO] No orders found - {DateTime.Now}\n");
                            }
                        }
                        else if (input == "2")
                        {
                            Console.Write("Produkt: ");
                            var prod = Console.ReadLine()?.Trim();
                            if (string.IsNullOrWhiteSpace(prod) || prod.Length > 100)
                            {
                                Console.WriteLine("Ogiltig produkt: Måste vara 1-100 tecken.");
                                File.AppendAllText(logFile, $"[ERROR] Invalid product name: {prod} - {DateTime.Now}\n");
                                continue;
                            }

                            Console.Write("Antal: ");
                            if (!int.TryParse(Console.ReadLine(), out int qty) || qty <= 0 || qty > 10000)
                            {
                                Console.WriteLine("Ogiltigt antal: Måste vara 1-10000.");
                                File.AppendAllText(logFile, $"[ERROR] Invalid quantity: {qty} - {DateTime.Now}\n");
                                continue;
                            }

                            var newOrder = new Order { Product = prod, Quantity = qty };
                            db.Orders.Add(newOrder);
                            db.SaveChanges();
                            Console.WriteLine("Order skapad!");
                            File.AppendAllText(logFile, $"[INFO] Order created: {prod} x{qty} (ID: {newOrder.Id}) - {DateTime.Now}\n");
                        }
                        else if (input == "0")
                        {
                            Console.WriteLine("Avslutar...");
                            File.AppendAllText(logFile, $"[INFO] Application shutdown - {DateTime.Now}\n");
                            break;
                        }
                        else
                        {
                            Console.WriteLine("Ogiltigt val, försök igen.");
                            File.AppendAllText(logFile, $"[ERROR] Invalid input: {input} - {DateTime.Now}\n");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ett fel uppstod: {ex.Message}");
                    File.AppendAllText(logFile, $"[ERROR] Operation failed: {ex.Message} - {DateTime.Now}\n");
                }
            }
        }
    }
}

