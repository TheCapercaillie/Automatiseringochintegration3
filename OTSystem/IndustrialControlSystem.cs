using EasyModbus;
using System;
using System.Threading;

namespace OTSystem
{
    internal class IndustrialControlSystem
    {
        private readonly int _port;
        private readonly int _authKey;
        private static readonly object _lock = new();
        private static ModbusServer? modbusServer;
        private static bool isBusy = false;
        private static int produced = 0; // Track current production
        private static int completedOrders = 0; // Track total completed orders

        public IndustrialControlSystem(int port, int authKey)
        {
            _port = port;
            _authKey = authKey;
        }

        public void Run()
        {
            string logFile = "OTSystem_log.txt";
            Console.WriteLine("=== OT System Dashboard ===");
            Console.WriteLine("---------------------------");
            Console.WriteLine($"Status: {(isBusy ? "Busy" : "Idle")}");
            Console.WriteLine($"Produced: {produced} units");
            Console.WriteLine($"Completed Orders: {completedOrders}");
            Console.WriteLine("---------------------------");

            File.AppendAllText(logFile, $"[INFO] OT system started on port {_port} with AuthKey {_authKey} - {DateTime.Now}\n");

            Thread modbusThread = new Thread(() => StartEasyModbusTcpSlave(_port));
            modbusThread.IsBackground = true;
            modbusThread.Start();

            // Continuous UI update thread
            new Thread(() =>
            {
                while (true)
                {
                    if (modbusServer != null)
                    {
                        lock (_lock)
                        {
                            Console.SetCursorPosition(0, 2); // Update status line
                            Console.WriteLine($"Status: {(isBusy ? "Busy" : "Idle")}".PadRight(Console.WindowWidth));
                            Console.SetCursorPosition(0, 3); // Update produced line
                            Console.WriteLine($"Produced: {modbusServer.inputRegisters[1]} units".PadRight(Console.WindowWidth));
                            Console.SetCursorPosition(0, 4); // Update completed line
                            Console.WriteLine($"Completed Orders: {completedOrders}".PadRight(Console.WindowWidth));
                        }
                    }
                    Thread.Sleep(500); // Update every 500ms
                }
            }).Start();

            while (true) Thread.Sleep(1000); // Keep app running
        }

        private void StartEasyModbusTcpSlave(int port)
        {
            string logFile = "OTSystem_log.txt";
            try
            {
                modbusServer = new ModbusServer();
                modbusServer.Port = port;

                // Event handler for coil changes
                modbusServer.CoilsChanged += (startAddress, numberOfCoils) =>
                {
                    Console.WriteLine($"[OT] CoilsChanged event: Start {startAddress}, Count {numberOfCoils}");
                    File.AppendAllText(logFile, $"[INFO] CoilsChanged: Start {startAddress}, Count {numberOfCoils} - {DateTime.Now}\n");

                    lock (_lock)
                    {
                        if (isBusy)
                        {
                            Console.WriteLine("[OT] Start ignored – machine already active.");
                            File.AppendAllText(logFile, $"[INFO] Start ignored, already busy - {DateTime.Now}\n");
                            return;
                        }

                        // Check if coil 1 is set (from IntegrationSystem)
                        if (modbusServer.coils[1])
                        {
                            // Authentication check
                            short keyCandidate = (short)modbusServer.holdingRegisters[11];
                            short nonceCandidate = (short)modbusServer.holdingRegisters[12];
                            if (keyCandidate != _authKey || nonceCandidate <= 0 || nonceCandidate == lastNonce)
                            {
                                Console.WriteLine("[OT] Unauthorized start attempt detected.");
                                File.AppendAllText(logFile, $"[SECURITY] Unauthorized: Key={keyCandidate}, Nonce={nonceCandidate} - {DateTime.Now}\n");
                                modbusServer.coils[1] = false; // Reset coil
                                return;
                            }
                            lastNonce = nonceCandidate;

                            int orderId = modbusServer.holdingRegisters[0];
                            int qty = modbusServer.holdingRegisters[1];
                            if (qty <= 0) qty = 1; // Fallback to 1 if invalid

                            Console.WriteLine($"[OT] ---> Starting order {orderId}, target {qty} units");
                            File.AppendAllText(logFile, $"[INFO] Starting order {orderId}, qty {qty}, nonce {nonceCandidate} - {DateTime.Now}\n");

                            isBusy = true;
                            modbusServer.inputRegisters[1] = 0; // Reset produced
                            modbusServer.inputRegisters[2] = 0; // Reset runtime
                            modbusServer.discreteInputs[1] = false; // Reset done signal

                            // Start production thread
                            new Thread(() =>
                            {
                                try
                                {
                                    int localProduced = 0;
                                    int runtime = 0;
                                    while (localProduced < qty)
                                    {
                                        Thread.Sleep(1000); // Simulate 1 second per unit
                                        localProduced++;
                                        runtime++;
                                        lock (_lock)
                                        {
                                            modbusServer.inputRegisters[1] = (short)localProduced; // Update produced
                                            modbusServer.inputRegisters[2] = (short)runtime; // Update runtime
                                        }
                                        Console.WriteLine($"[OT] Producing: {localProduced}/{qty} units, runtime {runtime}s");
                                    }

                                    lock (_lock)
                                    {
                                        modbusServer.discreteInputs[1] = true; // Signal completion
                                        modbusServer.inputRegisters[4] = (short)runtime; // Total runtime
                                        isBusy = false;
                                        modbusServer.coils[1] = false; // Reset coil
                                        completedOrders++; // Increment completed orders
                                    }
                                    Console.WriteLine($"[OT] <--- Finished order {orderId}, produced {qty}, runtime {runtime}s");
                                    File.AppendAllText(logFile, $"[INFO] Finished order {orderId}, produced {qty}, runtime {runtime}s - {DateTime.Now}\n");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[OT] Production error: {ex.Message}");
                                    File.AppendAllText(logFile, $"[ERROR] Production error: {ex.Message} - {DateTime.Now}\n");
                                    lock (_lock)
                                    {
                                        isBusy = false;
                                        modbusServer.discreteInputs[1] = false;
                                    }
                                }
                            }).Start();
                        }
                    }
                };

                Console.WriteLine($"[OT] Starting EasyModbus TCP Slave on port {port}...");
                modbusServer.Listen();
                Console.WriteLine("[OT] EasyModbus TCP Slave started. Press any key to exit.");
                File.AppendAllText(logFile, $"[INFO] Modbus server started on port {port} - {DateTime.Now}\n");
                Console.ReadKey();
                modbusServer.StopListening();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OT] Failed to start Modbus server: {ex.Message}");
                File.AppendAllText(logFile, $"[ERROR] Failed to start Modbus server: {ex.Message} - {DateTime.Now}\n");
                throw;
            }
        }

        private static int lastNonce = -1;
    }
}

