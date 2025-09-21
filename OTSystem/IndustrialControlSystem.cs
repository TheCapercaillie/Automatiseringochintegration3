using EasyModbus;
using System;
using System.Threading;

namespace OTSystem
{
    public class IndustrialControlSystem
    {
        private readonly int _port;
        private readonly int _authKey;
        private static readonly object _lock = new object();
        private static ModbusServer? modbusServer;
        private static bool isBusy = false;

        public IndustrialControlSystem(int port, int authKey)
        {
            _port = port;
            _authKey = authKey;
        }

        public void Run()
        {
            string logFile = "OTSystem_log.txt";
            Console.WriteLine($"[OT] Simulated OT system with Modbus support on port {_port} with AuthKey {_authKey}");
            File.AppendAllText(logFile, $"[INFO] Simulated OT system with Modbus support on port {_port} with AuthKey {_authKey} - {DateTime.Now}\n");

            Thread modbusThread = new Thread(() => StartEasyModbusTcpSlave(_port));
            modbusThread.IsBackground = true;
            modbusThread.Start();
            while (true) Thread.Sleep(1000);
        }

        private void StartEasyModbusTcpSlave(int port)
        {
            string logFile = "OTSystem_log.txt";
            try
            {
                modbusServer = new ModbusServer();
                modbusServer.Port = port;
                modbusServer.CoilsChanged += (register, numberOfCoils) =>
                {
                    lock (_lock)
                    {
                        if (modbusServer.coils[1])
                        {
                            int keyCandidate = modbusServer.holdingRegisters[11];
                            int nonceCandidate = modbusServer.holdingRegisters[12];
                            if (keyCandidate == _authKey && nonceCandidate > 0)
                            {
                                int qty = modbusServer.holdingRegisters[2];
                                int produced = 0;
                                isBusy = true;
                                modbusServer.inputRegisters[3] = 1;
                                File.AppendAllText(logFile, $"[INFO] Starting production for quantity {qty} - {DateTime.Now}\n");
                                new Thread(() =>
                                {
                                    int runtime = 0;
                                    while (produced < qty)
                                    {
                                        Thread.Sleep(1000);
                                        produced++;
                                        runtime++;
                                        lock (_lock)
                                        {
                                            modbusServer.inputRegisters[1] = (short)produced;
                                            modbusServer.inputRegisters[2] = (short)runtime;
                                        }
                                    }
                                    lock (_lock)
                                    {
                                        isBusy = false;
                                        modbusServer.inputRegisters[3] = 0;
                                        modbusServer.discreteInputs[1] = true;
                                        modbusServer.inputRegisters[4] = (short)runtime;
                                        File.AppendAllText(logFile, $"[INFO] Production completed for quantity {qty}, runtime {runtime}s - {DateTime.Now}\n");
                                    }
                                }).Start();
                            }
                            else
                            {
                                Console.WriteLine("[OT] Unauthorized start attempt detected.");
                                File.AppendAllText(logFile, $"[SECURITY] Unauthorized attempt: Key={keyCandidate}, Nonce={nonceCandidate} - {DateTime.Now}\n");
                            }
                        }
                    }
                };
                Console.WriteLine($"[OT] Starting EasyModbus TCP Slave on port {port}...");
                modbusServer.Listen();
                Console.WriteLine("[OT] EasyModbus TCP Slave started. Press any key to exit.");
                File.AppendAllText(logFile, $"[INFO] Modbus server started on port {port} - {DateTime.Now}\n");

                new Thread(() =>
                {
                    while (true)
                    {
                        lock (_lock)
                        {
                            modbusServer.inputRegisters[3] = isBusy ? (short)1 : (short)0;
                        }
                        Thread.Sleep(500);
                    }
                }).Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OT] Failed to start Modbus server: {ex.Message}");
                File.AppendAllText(logFile, $"[ERROR] Failed to start Modbus server: {ex.Message} - {DateTime.Now}\n");
                throw;
            }
        }
    }
}

