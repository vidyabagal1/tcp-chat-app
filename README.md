# TCP Chat App in C# (.NET 6)

A multi-client TCP chat application built using C# and .NET 6. It supports login, direct messaging, multi-user messaging, broadcast, and audit logging. Designed for concurrency, protocol clarity, and backend robustness.

## 🚀 Features

- **Login System**: Validates users with predefined credentials
- **Direct Messaging (DM)**: Send private messages to specific users
- **Multi-User Messaging**: Target multiple users in one message
- **Broadcast Messaging**: Send messages to all connected clients
- **Audit Logging**: Tracks login, logout, and message events
- **Concurrency Handling**: Each client runs on a separate thread
- **Metrics**: Tracks messages per second and latency

## 🧪 Protocol Commands

- `LOGIN_REQ username password`
- `LOGIN_RESP success/failure`
- `DM recipient message`
- `MULTI recipient1,recipient2 message`
- `BROADCAST message`
- `USERS` – List active users
- `LOGOUT` – Disconnect from server

## 🛠️ Technologies Used

- C# (.NET 6)
- TCP Sockets (`System.Net.Sockets`)
- Threading (`System.Threading`)
- WinForms (optional UI layer)
- Logging via file streams

## 📂 Folder Structure

