using Microsoft.Extensions.Configuration;

namespace OTSystem
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string systemName = config["AppSettings:SystemName"];
            int port = int.Parse(config["Modbus:Port"]);
            int authKey = int.Parse(config["Modbus:AuthKey"]);

            Console.WriteLine($"[OT] {systemName} startat på port {port} med AuthKey {authKey}");

            var ics = new IndustrialControlSystem(port, authKey);
            ics.Run();
        }
    }
}
