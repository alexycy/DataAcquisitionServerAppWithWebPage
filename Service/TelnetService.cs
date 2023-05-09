using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DataAcquisitionServerApp
{
    public static class TelnetService
    {
        public static async Task StartTelnetServerAsync(TcpListenerWrapper listenerWrapper,  string ipAddress, int port)
        {
            try
            {
                IPAddress localAddress = IPAddress.Parse(ipAddress);
                listenerWrapper.Listener = new TcpListener(localAddress, port);
                listenerWrapper.Listener.Start();

                while (true)
                {
                    var client = await listenerWrapper.Listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
            }
            catch (Exception e)
            {

                throw;
            }

        }

        public static async Task HandleClientAsync(TcpClient client)
        {
            Console.WriteLine("Client connected.");

            using var reader = new StreamReader(client.GetStream(), Encoding.ASCII);
            using var writer = new StreamWriter(client.GetStream(), Encoding.ASCII) { AutoFlush = true };

            // Authenticate user with username and password.
            writer.WriteLine("Username:");
            string username = (await reader.ReadLineAsync()) ?? string.Empty;


            writer.WriteLine("Password:");
            string password = (await reader.ReadLineAsync()) ?? string.Empty;

            if (ValidateCredentials(username, password))
            {
                writer.WriteLine("Login successful. Enter 'quit' to exit.");
                string command;
                do
                {
                    command = (await reader.ReadLineAsync()) ?? string.Empty;

                    if (command != null && command.ToLower() != "quit")
                    {
                        writer.WriteLine($"You entered: {command}");
                    }
                } while (command != null && command.ToLower() != "quit");
            }
            else
            {
                writer.WriteLine("Invalid credentials.");
            }

            Console.WriteLine("Client disconnected.");
        }

        public static bool ValidateCredentials(string username, string password)
        {
            // Replace with your own validation logic.
            return username == "user" && password == "pass";
        }
    }
}
