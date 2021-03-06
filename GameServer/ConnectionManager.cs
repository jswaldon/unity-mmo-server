using System;
using System.Collections.Concurrent;
using System.Linq;
using GameServer.GameServer;
using GameServer.Models;
using GameServer.Utils;
using NetcodeIO.NET;
using ReliableNetcode;

namespace GameServer
{
    public delegate void PlayerConnected();
    public delegate void ChatMessageReceived(ChatMessage msg);
    public delegate void Movement();

    public delegate void DataReceived<in T>(T data) where T : struct;
    
    public class ConnectionManager
    {
        private const int HEADER_OFFSET = 2;
        
        static readonly byte[] _privateKey = new byte[]
        {
            0x60, 0x6a, 0xbe, 0x6e, 0xc9, 0x19, 0x10, 0xea,
            0x9a, 0x65, 0x62, 0xf6, 0x6f, 0x2b, 0x30, 0xe4,
            0x43, 0x71, 0xd6, 0x2c, 0xd1, 0x99, 0x27, 0x26,
            0x6b, 0x3c, 0x60, 0xf4, 0xb7, 0x15, 0xab, 0xa1
        };

        public ChatMessageReceived OnChatMessage;
        
        private static Server _server;

        private static ConcurrentDictionary<RemoteClient, ReliableEndpoint> _clients;
        
        public ConnectionManager()
        {
            _clients = new ConcurrentDictionary<RemoteClient, ReliableEndpoint>();    
        }

        public RemoteClient GetClient(ulong id)
        {
            var client = _clients.FirstOrDefault(pair => pair.Key.ClientID == id);
            return client.Key;
        }
        
        public void StartServer()
        {
            //var privateKeyBytes = Encoding.ASCII.GetBytes("thisismysupersecretkeyimusing");
            
            _server = new Server(
                5,		// int maximum number of clients which can connect to this server at one time
                "127.0.0.1", 8559,	// string public address and int port clients will connect to
                1UL,		// ulong protocol ID shared between clients and server
                _privateKey		// byte[32] private crypto key shared between backend servers
            );
            
            // Called when a client has connected
            _server.OnClientConnected += ClientConnectedHandler;		// void( RemoteClient client )

            // Called when a client disconnects
            _server.OnClientDisconnected += ClientDisconnectedHandler;	// void( RemoteClient client )

            // Called when a payload has been received from a client
            // Note that you should not keep a reference to the payload, as it will be returned to a pool after this call completes.
            _server.OnClientMessageReceived += ClientMessageReceivedHandler;	// void( RemoteClient client, byte[] payload, int payloadSize )
            
            _server.Start(); 
            Console.WriteLine("Waiting for connections...");
        }

        public void StopServer(int secondsToStop = 0)
        {
            if (secondsToStop == 0)
            {
                foreach (var (remoteClient, reliableEndpoint) in _clients)
                {
                    _server.Disconnect(remoteClient);
                }

                _server.Stop();
            }
        }

        public void SendAll(byte[] payload, int payloadSize, QosType type = QosType.Unreliable)
        {        
            //Console.WriteLine($"Sending Payload of {payloadSize} bytes.");
            foreach (var (remoteClient, reliableEndpoint) in _clients)
            {
                reliableEndpoint.TransmitCallback = ( buffer, size ) =>
                {                 
                    remoteClient.SendPayload(buffer, size);                  
                };
                
                reliableEndpoint.SendMessage(payload, payloadSize, type);
            }
        }
        
        public void SendAll<T>(T data, MessageType type, QosType qosType = QosType.Unreliable)
        {        
            var objData = StructTools.RawSerialize(data);
            var objType = BitConverter.GetBytes((short)type);
            var payload = objType.Concat(objData).ToArray();
            
            foreach (var (rc, re) in _clients)
            {
                re.TransmitCallback = ( buffer, size ) => {  rc.SendPayload(buffer, size); };               
                re.SendMessage(payload, payload.Length, qosType);
            }
        }
        
        public void Send(RemoteClient client, byte[] payload, int payloadSize, QosType type = QosType.Unreliable)
        {
            _clients.TryGetValue(client, out var reliableEndpoint);
            reliableEndpoint?.SendMessage(payload, payloadSize, type);
        }
        
        public void Send<T>(RemoteClient client, T data, MessageType type, QosType qosType = QosType.Unreliable)
        {        
            var objData = StructTools.RawSerialize(data);
            var objType = BitConverter.GetBytes((short)type);
            var payload = objType.Concat(objData).ToArray();
            _clients.TryGetValue(client, out var reliableEndpoint);
            reliableEndpoint?.SendMessage(payload, payload.Length, qosType);
        }
        
        private void ClientConnectedHandler(RemoteClient client)
        {           
            Console.WriteLine($"clientConnectedHandler: {client}");
            ReliableEndpoint _reliableEndpoint = new ReliableEndpoint();
            _reliableEndpoint.ReceiveCallback += ReliableClientMessageReceived;
            _clients.TryAdd(client, _reliableEndpoint);
        }
        
        private void ClientDisconnectedHandler(RemoteClient client)
        {
            Console.WriteLine($"clientDisconnectedHandler: {client}");
            _clients.TryRemove(client, out _);
        }
        
        private void ClientMessageReceivedHandler(RemoteClient client, byte[] payload, int payloadSize)
        {
            _clients.TryGetValue(client, out var _reliableEndpoint);
            _reliableEndpoint.ReceivePacket(payload, payloadSize);
        }
            
        private void ReliableClientMessageReceived(byte[] payload, int payloadSize)
        {
            
            Console.WriteLine($"Received Payload of {payloadSize} bytes.");
            MessageType type = (MessageType)BitConverter.ToInt16(payload, 0);
            
            if (type == MessageType.Chat)
            {
                //var data = StructTools.RawDeserialize<ChatMessage>(payload, HEADER_OFFSET);
                //OnChatMessage?.Invoke(data);
                var chatMessage = StructTools.RawDeserialize<ChatMessage>(payload, HEADER_OFFSET);
                //OnChatMessageReceived?.Invoke(chatMessage);
                SendAll(chatMessage, MessageType.Chat);
            }
            else
            {
                Console.WriteLine($"Type: {(MessageType) Enum.Parse(typeof(MessageType), type.ToString())}");

                var pos = StructTools.RawDeserialize<Position>(payload, HEADER_OFFSET); // 0 is offset in byte[]
                //Console.WriteLine($"messageReceivedHandler: {client} sent {payloadSize} bytes of data.");
                Console.WriteLine(pos.ToString());
                SendAll(payload, payloadSize);
            }

        }       
    }
}