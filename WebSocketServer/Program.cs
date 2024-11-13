using System;

namespace WebSocketServer
{
    using System;
    using System.Collections.Concurrent;
    using System.Net;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    class WebSocketServer
    {
        private readonly HttpListener _httpListener;
        private readonly ConcurrentDictionary<WebSocket, int> _connectedClients;
        private int _currentTurn = 1;

        public WebSocketServer(string uri)
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(uri);
            _connectedClients = new ConcurrentDictionary<WebSocket, int>();
        }

        public async Task Start()
        {
            _httpListener.Start();
            Console.WriteLine("Server started...");

            while (true)
            {
                var listenerContext = await _httpListener.GetContextAsync();
                if (listenerContext.Request.IsWebSocketRequest)
                {
                    var webSocketContext = await listenerContext.AcceptWebSocketAsync(null);
                    var clientSocket = webSocketContext.WebSocket;

                    if (_connectedClients.Count < 2) // Allow only 2 players
                    {
                        int playerNumber = _connectedClients.Count + 1;
                        _connectedClients[clientSocket] = playerNumber;

                        Console.WriteLine($"Player {playerNumber} connected.");
                        _ = HandleClient(clientSocket, playerNumber); // Run client handling in the background
                    }
                    else
                    {
                        Console.WriteLine("Server full. New connection denied.");
                        listenerContext.Response.StatusCode = 403;
                        listenerContext.Response.Close();
                    }
                }
            }
        }

        private async Task HandleClient(WebSocket clientSocket, int playerNumber)
        {
            var buffer = new byte[1024];

            while (clientSocket.State == WebSocketState.Open)
            {
                var result = await clientSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    _connectedClients.TryRemove(clientSocket, out _);
                    Console.WriteLine($"Player {playerNumber} disconnected.");
                    return;
                }

                // Parse the received move
                var move = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine($"Player {playerNumber} made a move: {move}");

                // Enforce turn-taking logic
                if (_currentTurn != playerNumber)
                {
                    Console.WriteLine($"Invalid turn for Player {playerNumber}. Ignoring move.");
                    continue;
                }

                // Forward the move to the other player
                var otherPlayerSocket = GetOtherPlayerSocket(playerNumber);
                if (otherPlayerSocket != null && otherPlayerSocket.State == WebSocketState.Open)
                {
                    await otherPlayerSocket.SendAsync(
                        new ArraySegment<byte>(Encoding.UTF8.GetBytes(move)),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None
                    );
                    Console.WriteLine($"Move forwarded to Player {_connectedClients[otherPlayerSocket]}");
                }

                // Switch turns
                _currentTurn = _currentTurn == 1 ? 2 : 1;
            }
        }

        private WebSocket GetOtherPlayerSocket(int playerNumber)
        {
            foreach (var kvp in _connectedClients)
            {
                if (kvp.Value != playerNumber) return kvp.Key;
            }
            return null;
        }

        public async Task Stop()
        {
            foreach (var client in _connectedClients.Keys)
            {
                await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
            }
            _httpListener.Stop();
            Console.WriteLine("Server stopped.");
        }
    }

    internal class Program
    {
        static async Task Main()
        {
            var server = new WebSocketServer("http://localhost:5000/");
            await server.Start();
        }
    }
}