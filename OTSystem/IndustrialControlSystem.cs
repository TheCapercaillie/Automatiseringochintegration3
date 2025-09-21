using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EasyModbus;

namespace OTSystem
{
    internal class IndustrialControlSystem
    {
        private static readonly int AuthKey = 0xBEEF;
        private static int lastNonce = -1;
        private static bool isBusy = false;
        private static readonly object _lock = new();
        private static ModbusServer? modbusServer;
        private int _port;
        private int _authKey;

        public IndustrialControlSystem(int port, int authKey)
        {
            _port = port;
            _authKey = authKey;
        }

        public void Run()
        {

            Console.WriteLine($"Simulated OT system with Modbus support on port {_port} with AuthKey {_authKey}");

            Thread modbusThread = new Thread(StartEasyModbusTcpSlave);
            modbusThread.IsBackground = true;
            modbusThread.Start();

            while (true) Thread.Sleep(1000);
        }
        public static void StartEasyModbusTcpSlave()
        {
            int port = 502;
            ModbusServer modbusServer = new ModbusServer();
            modbusServer.Port = port;

            // --- Event Handlers for EasyModbus ---

            modbusServer.CoilsChanged += (int startAddress, int numberOfCoils) =>
            {
                Console.WriteLine($"CoilsChanged event fired at {DateTime.Now}");
                Console.WriteLine($"  Start Address: {startAddress}");
                Console.WriteLine($"  Number of Coils: {numberOfCoils}");

                const int maxCoilAddress = 1999;

                for (int i = 0; i < numberOfCoils; i++)
                {
                    int address = startAddress + i;

                    if (address >= 0 && address <= maxCoilAddress)
                    {
                        Console.WriteLine($"    Coil[{address}] changed to: {modbusServer.coils[address]}");
                    }
                    else
                    {
                        Console.WriteLine($"    Warning: Attempted to access Coil[{address}] which is out of bounds.");
                    }
                    if (!(modbusServer.coils[0] || modbusServer.coils[1]))
                        return;

                    lock (_lock)
                    {
                        if (isBusy)
                        {
                            Console.WriteLine("[OT] Start ignored – machine already active.");
                            return;
                        }
                        isBusy = true;
                    }

                    short keyCandidate = (short)(modbusServer.holdingRegisters[10] != 0
                        ? modbusServer.holdingRegisters[10]
                        : modbusServer.holdingRegisters[11]);

                    short nonceCandidate = (short)(modbusServer.holdingRegisters[11] != 0
                        ? modbusServer.holdingRegisters[11]
                        : modbusServer.holdingRegisters[12]);

                    bool invalid = keyCandidate != AuthKey || nonceCandidate == lastNonce;
                    if (invalid)
                    {
                        Console.WriteLine("[OT] Unauthorized start attempt detected.");
                        ResetCoils();
                        MarkIdle();
                        return;
                    }
                    lastNonce = nonceCandidate;

                    int orderId = modbusServer.holdingRegisters[0];
                    if (orderId == 0) orderId = modbusServer.holdingRegisters[1];

                    int qty = modbusServer.holdingRegisters[2];
                    if (qty == 0) qty = modbusServer.holdingRegisters[1];
                    if (qty < 0) qty = 0;

                    Console.WriteLine($"[OT] ---> Starting order {orderId}, target {qty} units");

                    ClearStatus();

                    new Thread(() =>
                    {
                        try
                        {
                            int produced = 0;
                            while (produced < qty)
                            {
                                Thread.Sleep(1000);
                                produced++;
                                short report = (short)Math.Min(produced, short.MaxValue);

                                modbusServer.inputRegisters[0] = report;
                                modbusServer.inputRegisters[1] = report;
                            }

                            modbusServer.discreteInputs[0] = true;
                            modbusServer.discreteInputs[1] = true;
                            Console.WriteLine($"[OT] <--- Finished order {orderId}, produced {qty}");
                        }
                        finally
                        {
                            ResetCoils();
                            MarkIdle();
                        }
                    }).Start();

                    void ResetCoils()
                    {
                        modbusServer.coils[0] = false;
                        modbusServer.coils[1] = false;
                    }

                    void ClearStatus()
                    {
                        modbusServer.inputRegisters[0] = 0;
                        modbusServer.inputRegisters[1] = 0;
                        modbusServer.discreteInputs[0] = false;
                        modbusServer.discreteInputs[1] = false;
                    }

                    void MarkIdle()
                    {
                        lock (_lock) isBusy = false;
                    }
                }
            };

            modbusServer.HoldingRegistersChanged += (int startAddress, int numberOfRegisters) =>
            {
                Console.WriteLine($"HoldingRegistersChanged event fired at {DateTime.Now}");
                Console.WriteLine($"  Start Address: {startAddress}");
                Console.WriteLine($"  Number of Registers: {numberOfRegisters}");

                const int maxRegisterAddress = 1999;

                for (int i = 0; i < numberOfRegisters; i++)
                {
                    int address = startAddress + i;
                    if (address >= 0 && address <= maxRegisterAddress)
                    {
                        Console.WriteLine($"    HoldingRegister[{address}] changed to: {modbusServer.holdingRegisters[address]}");
                    }
                    else
                    {
                        Console.WriteLine($"    Warning: Attempted to access HoldingRegister[{address}] which is out of bounds.");
                    }
                }
            };

            modbusServer.inputRegisters[0] = 0;
            modbusServer.inputRegisters[1] = 0;
            modbusServer.discreteInputs[0] = false;
            modbusServer.discreteInputs[1] = false;

            try
            {
                Console.WriteLine($"Starting EasyModbus TCP Slave on port {port}...");
                modbusServer.Listen();
                Console.WriteLine("EasyModbus TCP Slave started. Press any key to exit.");
                Console.ReadKey();
                Console.WriteLine("Stopping EasyModbus TCP Slave...");
                Console.WriteLine("EasyModbus TCP Slave stopped.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}

