using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

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

            try
            {
                using (var scope = provider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                    db.Database.EnsureCreated();
                    Console.WriteLine("DB connection OK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fel vid anslutning till databasen: {ex.Message}");
                return;
            }

            while (true)
            {
                Console.WriteLine("\n--- IT OrderApp ---");
                Console.WriteLine("1) Lista ordrar");
                Console.WriteLine("2) Skapa ny order");
                Console.WriteLine("0) Avsluta");
                Console.Write("> ");
                var input = Console.ReadLine();

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
                                    Console.WriteLine($"{o.Id}: {o.Product} x{o.Quantity} [{o.Status}]");
                            }
                            else
                            {
                                Console.WriteLine("Inga ordrar hittades.");
                            }
                        }
                        else if (input == "2")
                        {
                            Console.Write("Produkt: ");
                            var prod = Console.ReadLine();
                            if (string.IsNullOrWhiteSpace(prod))
                            {
                                Console.WriteLine("Produktnamn kan inte vara tomt!");
                                continue;
                            }

                            Console.Write("Antal: ");
                            if (!int.TryParse(Console.ReadLine(), out int qty) || qty <= 0)
                            {
                                Console.WriteLine("Ogiltigt antal, ange ett positivt heltal.");
                                continue;
                            }

                            db.Orders.Add(new Order { Product = prod, Quantity = qty });
                            db.SaveChanges();
                            Console.WriteLine("Order skapad!");
                        }
                        else if (input == "0")
                        {
                            break;
                        }
                        else
                        {
                            Console.WriteLine("Ogiltigt val, försök igen.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ett fel uppstod: {ex.Message}");
                }
            }
        }
    }
}
