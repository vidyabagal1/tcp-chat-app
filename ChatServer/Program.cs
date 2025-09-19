using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;

class ClientSession
{
    public string Username { get; }
    public TcpClient TcpClient { get; }
    public NetworkStream Stream => TcpClient.GetStream();
    public BlockingCollection<object> SendQueue { get; } = new();
    public Stopwatch Stopwatch = Stopwatch.StartNew();

    public ClientSession(string username, TcpClient client)
    {
        Username = username;
        TcpClient = client;
        Task.Run(SendLoop);
    }

    private async Task SendLoop()
    {
        try
        {
            foreach (var obj in SendQueue.GetConsumingEnumerable())
            {
                string json = JsonSerializer.Serialize(obj);
                byte[] data = Encoding.UTF8.GetBytes(json);
                await Stream.WriteAsync(data);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SEND_ERROR] {Username}: {ex.Message}");
        }
    }

    public void Enqueue(object obj) => SendQueue.Add(obj);
    public void Close() => SendQueue.CompleteAdding();
}

class Program
{
    static Dictionary<string, string> users = new()
    {
        { "user1", "pass1" },
        { "user2", "pass2" },
        { "user3", "pass3" }
    };

    static ConcurrentDictionary<string, ClientSession> sessions = new();
    static string logFile = "chat_audit.log";
    static int messageCount = 0;

    static async Task Main()
    {
        TcpListener listener = new(IPAddress.Any, 5000);
        listener.Start();
        Console.WriteLine("Server started on port 5000...");

        _ = Task.Run(MetricsLoop);

        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            Log($"[CONNECT] {client.Client.RemoteEndPoint}");
            _ = HandleClient(client);
        }
    }

    static async Task HandleClient(TcpClient client)
    {
        string? username = null;
        int passwordAttempts = 0;

        try
        {
            using NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4096];

            // ---------- LOGIN ----------
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer);
                if (bytesRead == 0) return;

                string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                JsonDocument doc;
                try { doc = JsonDocument.Parse(json); }
                catch (Exception ex)
                {
                    Log($"[MALFORMED_JSON] {ex.Message}");
                    client.Close();
                    return;
                }

                var root = doc.RootElement;
                string type = root.GetProperty("type").GetString() ?? "";

                if (type == "LOGIN_REQ")
                {
                    string user = root.GetProperty("username").GetString() ?? "";
                    string pass = root.GetProperty("password").GetString() ?? "";

                    Log($"[LOGIN_ATTEMPT] {user}");

                    if (sessions.ContainsKey(user))
                    {
                        await Send(stream, new { type = "LOGIN_RESP", ok = false, reason = "User already logged in" });
                        client.Close();
                        return;
                    }

                    if (!users.ContainsKey(user))
                    {
                        await Send(stream, new { type = "LOGIN_RESP", ok = false, reason = "Invalid username" });
                        client.Close();
                        return;
                    }

                    if (users[user] != pass)
                    {
                        passwordAttempts++;
                        if (passwordAttempts >= 2)
                        {
                            await Send(stream, new { type = "LOGIN_RESP", ok = false, reason = "Too many wrong attempts" });
                            client.Close();
                            return;
                        }
                        else
                        {
                            await Send(stream, new { type = "LOGIN_RESP", ok = false, reason = $"Invalid password ({passwordAttempts}/2)" });
                            continue;
                        }
                    }

                    username = user;
                    var session = new ClientSession(username, client);
                    sessions[username] = session;
                    await Send(stream, new { type = "LOGIN_RESP", ok = true, msg = $"Welcome {username}!" });
                    break;
                }
            }

            // ---------- CHAT LOOP ----------
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer);
                if (bytesRead == 0) break;

                string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                JsonDocument doc;
                try { doc = JsonDocument.Parse(json); }
                catch (Exception ex)
                {
                    Log($"[MALFORMED_JSON] {username}: {ex.Message}");
                    break;
                }

                var root = doc.RootElement;
                string type = root.GetProperty("type").GetString() ?? "";

                switch (type)
                {
                    case "DM":
                        string toUser = root.GetProperty("to").GetString() ?? "";
                        string msg = root.GetProperty("msg").GetString() ?? "";
                        LogMessage(username!, toUser, "DM", json.Length);
                        SendToUser(username!, toUser, new { type = "DM", from = username, msg });
                        break;

                    case "MULTI":
                        var recipients = root.GetProperty("to").EnumerateArray().Select(u => u.GetString()).Where(u => u != null).ToList();
                        string mmsg = root.GetProperty("msg").GetString() ?? "";
                        foreach (var u in recipients)
                            SendToUser(username!, u!, new { type = "MULTI", from = username, msg = mmsg });
                        LogMessage(username!, string.Join(",", recipients), "MULTI", json.Length);
                        break;

                    case "BROADCAST":
                        string bmsg = root.GetProperty("msg").GetString() ?? "";
                        foreach (var kvp in sessions)
                            if (kvp.Key != username)
                                kvp.Value.Enqueue(new { type = "BROADCAST", from = username, msg = bmsg });
                        LogMessage(username!, "ALL", "BROADCAST", json.Length);
                        break;

                    case "USERS_REQ":
                        var online = sessions.Keys.ToList();
                        sessions[username!].Enqueue(new { type = "USERS_RESP", users = online });
                        break;

                    case "LOGOUT":
                        Log($"[LOGOUT] {username}");
                        return;
                }

                Interlocked.Increment(ref messageCount);
            }
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.Message}");
        }
        finally
        {
            if (username != null && sessions.TryRemove(username, out var session))
            {
                session.Close();
                Log($"[DISCONNECT] {username}");
            }
            else
            {
                Log("[DISCONNECT] Unknown client");
            }
            client.Close();
        }
    }

    static void SendToUser(string from, string to, object obj)
    {
        if (sessions.TryGetValue(to, out var session))
        {
            session.Enqueue(obj);
        }
    }

    static async Task Send(NetworkStream stream, object obj)
    {
        string json = JsonSerializer.Serialize(obj);
        byte[] data = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(data);
    }

    static void Log(string line)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string logLine = $"{timestamp} {line}";
        Console.WriteLine(logLine);
        File.AppendAllText(logFile, logLine + Environment.NewLine);
    }

    static void LogMessage(string from, string to, string type, int bytes)
    {
        string line = $"[MSG] {from} -> {to} | {type} | {bytes} bytes";
        Log(line);
    }

    static async Task MetricsLoop()
    {
        while (true)
        {
            await Task.Delay(5000);
            int count = Interlocked.Exchange(ref messageCount, 0);
            Console.WriteLine($"[METRICS] msgs/sec: {count / 5.0:F2}, online: {sessions.Count}");
        }
    }
}
