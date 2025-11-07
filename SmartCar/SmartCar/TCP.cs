using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SmartCar
{
    public static class SmartCarInterface
    {
        public static Socket OpenSocket(string ipAddress, int port, int timeout)
        {
            Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.ReceiveTimeout = timeout;

            try
            {
                clientSocket.Connect(ipAddress, port);
            }
            catch (SocketException se)
            {
                Console.WriteLine($"Connection timed out connecting to {ipAddress}:{port}");
                CloseSocket(clientSocket);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to {ipAddress}:{port}: {ex.Message}");
                CloseSocket(clientSocket);
                Environment.Exit(0);
            }

            return clientSocket;
        }

        public static void CloseSocket(Socket clientSocket)
        {
            clientSocket.Shutdown(SocketShutdown.Both);
            clientSocket.Close();
        }

        public static void Interact(Socket clientSocket, string callback = null)  // Action<Socket> callback = null
        {
            // Specify how many requests a Socket can listen before it gives Server busy response.
         
            Console.WriteLine("Waiting for a connection...");
            Socket handler = clientSocket.Accept();

            // Incoming data from the client.
            string data = null;
            byte[] bytes = null;


            while (true)
            {
                bytes = new byte[1024];
                int bytesRec = handler.Receive(bytes);
                data += Encoding.ASCII.GetString(bytes, 0, bytesRec);
                //if (data.IndexOf("<EOF>") > -1)
                //{
                //    break;
                //}

                Console.WriteLine("Text received : {0}", data);

                try
                {
                    if (bytesRec == 0)
                    {
                        Console.WriteLine("No data received. Exiting.");
                        CloseSocket(clientSocket);
                        Environment.Exit(0);
                    }
                    else if (data == "{Heartbeat}{Heartbeat}{Heartbeat}{Heartbeat}")
                    {
                        //byte[] heartbeatResponse = Encoding.ASCII.GetBytes(callback);
                        //clientSocket.Send(heartbeatResponse);

                        //Console.WriteLine("Responding to heartbeat.");

                        ////byte[] smartdata = Encoding.ASCII.GetBytes(callback);
                        ////int bytesSent = clientSocket.Send(smartdata);
                        //Console.WriteLine("Sent {0} bytes to server.", heartbeatResponse);

                        ////callback?.Invoke(clientSocket);
                    }
                    else
                    {
                        string message = JsonConvert.SerializeObject(data);
                        string diagnostic = "Decoded message: " + message;
                    }
                    Console.WriteLine("Received from server: " + data);
                }
                catch (SocketException se)
                {
                    Console.WriteLine("Socket exception: " + se.Message);
                    CloseSocket(clientSocket);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception: " + ex.Message);
                    CloseSocket(clientSocket);
                    Environment.Exit(0);
                }

            }          
           
        }
    }
}
