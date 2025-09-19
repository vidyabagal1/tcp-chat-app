using System.Net.Sockets;
using System.Text;
using System.Text.Json;

class Program
{
    static async Task Main()
    {
        TcpClient client = new TcpClient();
        Console.WriteLine("Connecting to server...");
        await client.ConnectAsync("127.0.0.1", 5000);
        Console.WriteLine("Connected to server!");

        using NetworkStream stream = client.GetStream();

        // Start background reader
        _ = Task.Run(async () =>
        {
            byte[] buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                try
                {
                    var doc = JsonDocument.Parse(response);
                    var msg = doc.RootElement;

                    string type = msg.GetProperty("type").GetString() ?? "";

                    if (type == "LOGIN_RESP")
                    {
                        bool ok = msg.GetProperty("ok").GetBoolean();
                        if (ok)
                        {
                            string welcome = msg.GetProperty("msg").GetString() ?? "Login successful.";
                            Console.WriteLine($"[Server] {welcome}");
                            Console.WriteLine("[Server] Type HELP to see available commands.");
                        }
                        else
                        {
                            string reason = msg.GetProperty("reason").GetString() ?? "Login failed.";
                            Console.WriteLine($"[Server] Login failed: {reason}");
                        }
                    }
                    else if (type == "DM")
                    {
                        string from = msg.GetProperty("from").GetString()!;
                        string message = msg.GetProperty("msg").GetString()!;
                        Console.WriteLine($"[DM from {from}] {message}");
                    }
                    else if (type == "MULTI")
                    {
                        string from = msg.GetProperty("from").GetString()!;
                        string message = msg.GetProperty("msg").GetString()!;
                        Console.WriteLine($"[MULTI from {from}] {message}");
                    }
                    else if (type == "BROADCAST")
                    {
                        string from = msg.GetProperty("from").GetString()!;
                        string message = msg.GetProperty("msg").GetString()!;
                        Console.WriteLine($"[Broadcast from {from}] {message}");
                    }
                    else if (type == "USERS_RESP")
                    {
                        var users = msg.GetProperty("users").EnumerateArray().Select(u => u.GetString()).ToList();
                        Console.WriteLine("[Server] Online users: " + string.Join(", ", users));
                    }
                    else if (type == "ERROR")
                    {
                        Console.WriteLine("[Server Error] " + msg.GetProperty("msg").GetString());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Client Error] Invalid message from server: " + ex.Message);
                }
            }
        });

        // Login first
        Console.Write("Username: ");
        string username = Console.ReadLine()!;
        Console.Write("Password: ");
        string password = Console.ReadLine()!;
        await Send(stream, new { type = "LOGIN_REQ", username, password });

        // Chat loop
        while (true)
        {
            Console.Write("> ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input.StartsWith("DM "))
            {
                var parts = input.Split(" ", 3);
                if (parts.Length < 3)
                {
                    Console.WriteLine("[Client] Usage: DM {user} {message}");
                    continue;
                }
                await Send(stream, new { type = "DM", to = parts[1], msg = parts[2] });
            }
            else if (input.StartsWith("MULTI "))
            {
                var parts = input.Split(" ", 3);
                if (parts.Length < 3)
                {
                    Console.WriteLine("[Client] Usage: MULTI {user1,user2} {message}");
                    continue;
                }
                var toList = parts[1].Split(",");
                await Send(stream, new { type = "MULTI", to = toList, msg = parts[2] });
            }
            else if (input.StartsWith("BROADCAST "))
            {
                string msg = input.Substring(10);
                await Send(stream, new { type = "BROADCAST", msg });
            }
            else if (input == "USERS")
            {
                await Send(stream, new { type = "USERS_REQ" });
            }
            else if (input == "LOGOUT")
            {
                await Send(stream, new { type = "LOGOUT" });
                Console.WriteLine("[Client] You have logged out.");
                break;
            }
            else if (input == "HELP")
            {
                Console.WriteLine("Available commands:");
                Console.WriteLine("  DM {user} {message}         - Send private message");
                Console.WriteLine("  MULTI {u1,u2} {message}    - Send to multiple users");
                Console.WriteLine("  BROADCAST {message}        - Send to everyone");
                Console.WriteLine("  USERS                      - Show online users");
                Console.WriteLine("  LOGOUT                     - Logout");
                Console.WriteLine("  HELP                       - Show this help");
            }
            else
            {
                Console.WriteLine("[Client] Unknown command. Type HELP for options.");
            }
        }
    }

    static async Task Send(NetworkStream stream, object obj)
    {
        string json = JsonSerializer.Serialize(obj);
        byte[] data = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(data);
    }
}
