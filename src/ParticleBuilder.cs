using System;
using System.Net;
using System.Net.Sockets;

using Faster.Transport.Ipc;
using Faster.Transport.Inproc;
using Faster.Transport.Contracts;
using Faster.Transport.Features.Tcp;
using Faster.Transport.Features.Udp;

namespace Faster.Transport
{
    /// <summary>
    /// Specifies the available communication modes.
    /// </summary>
    public enum TransportMode
    {
        /// <summary>
        /// Communication within the same process (no network, fastest).
        /// </summary>
        Inproc,

        /// <summary>
        /// Communication between processes on the same machine using shared memory.
        /// </summary>
        Ipc,

        /// <summary>
        /// Standard TCP/IP network communication (reliable, ordered).
        /// </summary>
        Tcp,

        /// <summary>
        /// UDP communication (fast but unreliable).
        /// </summary>
        Udp
    }

    /// <summary>
    /// A fluent builder for creating <see cref="IParticle"/> clients.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ParticleBuilder"/> helps you create high-performance network clients
    /// using a simple and consistent syntax.
    /// You can choose between:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="TransportMode.Inproc"/> → same-process communication</item>
    ///   <item><see cref="TransportMode.Ipc"/> → inter-process communication (shared memory)</item>
    ///   <item><see cref="TransportMode.Tcp"/> → reliable network communication</item>
    ///   <item><see cref="TransportMode.Udp"/> → low-latency datagrams</item>
    /// </list>
    ///
    /// This builder is **client-only** — server creation is handled separately (for example, by <c>ReactorBuilder</c>).
    /// </remarks>
    public sealed class ParticleBuilder
    {
        #region === Configuration Fields ===

        /// <summary>
        /// Selected transport type (default = TCP)
        /// </summary>
        private TransportMode _mode = TransportMode.Tcp;

        /// <summary>
        /// Network endpoints for TCP/UDP
        /// </summary>
        private IPEndPoint? _remoteEndPoint;
        private IPEndPoint? _localEndPoint;

        /// <summary>
        /// Socket provided by a TCP acceptor (used for accepted connections)
        /// </summary>
        private Socket? _acceptedSocket;

        /// <summary>
        /// Shared channel name (used by IPC and Inproc modes)
        /// </summary>
        private string? _channelName;

        /// <summary>
        /// Used by IPC and Inproc modes to identify visibility scope of shared memory areas.
        ///
        /// !WARNING! Global visibility REQUIRES admin privileges!
        /// </summary>
        private bool _isGlobal = false;

        // Performance and tuning options
        private int _bufferSize = 8192;
        private int _parallelism = 8;
        private int _ringCapacity = 128 + (1 << 20); // Default ≈ 1 MB

        // UDP-specific options
        private bool _allowBroadcast;
        private bool _disableLoopback = true;
        private IPAddress? _multicastGroup;
        private int _multicastPort;

        // Auto-reconnect settings
        private bool _autoReconnect;
        private TimeSpan _baseDelay = TimeSpan.FromSeconds(1);
        private TimeSpan _maxDelay = TimeSpan.FromSeconds(30);

        // Event handlers (optional callbacks)
        private Action<IParticle, ReadOnlyMemory<byte>>? _onReceived;
        private Action<IParticle>? _onDisconnected;
        private Action<IParticle>? _onConnected;

        #endregion === Configuration Fields ===

        #region === Fluent Configuration ===

        /// <summary>
        /// Sets the communication mode (e.g., TCP, UDP, IPC, Inproc).
        /// </summary>
        public ParticleBuilder UseMode(TransportMode mode)
        {
            _mode = mode;
            return this;
        }

        /// <summary>
        /// Sets the remote endpoint for TCP or UDP clients.
        /// </summary>
        public ParticleBuilder WithRemote(IPEndPoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            _remoteEndPoint = endpoint;
            _acceptedSocket = null;
            return this;
        }

        /// <summary>
        /// Sets the local endpoint (used for UDP binding or custom TCP ports).
        /// </summary>
        public ParticleBuilder WithLocal(IPEndPoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            _localEndPoint = endpoint;
            return this;
        }

        /// <summary>
        /// Creates a client from an already-accepted socket.
        /// </summary>
        /// <remarks>
        /// Used internally by TCP servers to wrap accepted connections.
        /// </remarks>
        public ParticleBuilder FromAcceptedSocket(Socket socket)
        {
            _mode = TransportMode.Tcp;
            _acceptedSocket = socket;
            return this;
        }

        /// <summary>
        /// Sets the shared channel name used for IPC or Inproc communication.
        /// </summary>
        /// <param name="name">The unique name of the shared channel.</param>
        public ParticleBuilder WithChannel(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Channel name cannot be null or empty.", nameof(name));

            _channelName = name;
            return this;
        }

        /// <summary>
        /// Specify scope of memory mapped files.
        /// !WARNING! Global visibility REQUIRES admin privileges!
        /// </summary>
        /// <param name="global">
        /// If true, shared memory objects are created under the "Global\\" namespace, 
        /// making them visible across Windows sessions. Otherwise, "Local\\" is used.
        /// </param>
        public ParticleBuilder WithGlobal(bool isGlobal)
        {
            // TODO: throw an exception if transport mode is not suitable for isGlobal flag?
            // i.e. _mode == Tcp OR Udp

            _isGlobal = isGlobal;
            return this;
        }

        /// <summary>
        /// Adjusts the internal buffer size used for send and receive operations.
        /// </summary>
        public ParticleBuilder WithBufferSize(int bytes)
        {
            if (bytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(bytes));

            _bufferSize = bytes;
            return this;
        }

        /// <summary>
        /// Sets how many threads can send data concurrently.
        /// </summary>
        public ParticleBuilder WithParallelism(int degree)
        {
            if (degree <= 0)
                throw new ArgumentOutOfRangeException(nameof(degree));

            _parallelism = degree;
            return this;
        }

        /// <summary>
        /// Sets the maximum number of messages that can be queued in a ring buffer.
        /// </summary>
        public ParticleBuilder WithRingCapacity(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            // TODO: throw an exception if transport mode is not suitable for ring capacity?
            // i.e. _mode == Tcp OR Udp

            _ringCapacity = capacity;
            return this;
        }

        /// <summary>
        /// Enables UDP broadcast packets (use with caution).
        /// </summary>
        public ParticleBuilder AllowBroadcast(bool allow = true)
        {
            _allowBroadcast = allow;
            return this;
        }

        /// <summary>
        /// Configures a UDP multicast group.
        /// </summary>
        public ParticleBuilder WithMulticast(IPAddress group, int port, bool disableLoopback = true)
        {
            _mode = TransportMode.Udp;
            _multicastGroup = group;
            _multicastPort = port;
            _disableLoopback = disableLoopback;
            return this;
        }

        /// <summary>
        /// Sets a handler that triggers whenever a message is received.
        /// </summary>
        public ParticleBuilder OnReceived(Action<IParticle, ReadOnlyMemory<byte>> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _onReceived = handler;
            return this;
        }

        /// <summary>
        /// Sets a handler that triggers when the connection is established.
        /// </summary>
        public ParticleBuilder OnConnected(Action<IParticle> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _onConnected = handler;
            return this;
        }

        /// <summary>
        /// Sets a handler that triggers when the connection is lost or closed.
        /// </summary>
        public ParticleBuilder OnDisconnected(Action<IParticle> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _onDisconnected = handler;
            return this;
        }

        /// <summary>
        /// Enables automatic reconnection if the connection drops.
        /// </summary>
        /// <param name="baseSeconds">The initial retry delay in seconds.</param>
        /// <param name="maxSeconds">The maximum retry delay in seconds.</param>
        public ParticleBuilder WithAutoReconnect(double baseSeconds = 1, double maxSeconds = 30)
        {
            if (baseSeconds <= 0.1)
                throw new ArgumentOutOfRangeException(nameof(baseSeconds),
                    "Initial reconnect interval should be at least 0.1 sec. But it is " + baseSeconds);

            if (maxSeconds < 1.001 * baseSeconds)
                throw new ArgumentOutOfRangeException(nameof(baseSeconds),
                    "Max reconnect interval should be at least " + (1.001 * baseSeconds) + " sec. But it is " + maxSeconds);
            
            _autoReconnect = true;
            _baseDelay = TimeSpan.FromSeconds(baseSeconds);
            _maxDelay = TimeSpan.FromSeconds(maxSeconds);
            return this;
        }

        #endregion === Fluent Configuration ===

        #region === Build ===

        /// <summary>
        /// Creates the <see cref="IParticle"/> instance using the current configuration.
        /// </summary>
        /// <remarks>
        /// If auto-reconnect is enabled, the client will automatically reconnect using an internal wrapper.
        /// </remarks>
        public IParticle Build()
        {
            // 🔄 If auto-reconnect is enabled, wrap the connection in a retry layer
            if (_autoReconnect)
            {
                return new AutoReconnectWrapper(
                    factory: () => BuildCore(),
                    baseDelay: _baseDelay,
                    maxDelay: _maxDelay,
                    onConnected: _onConnected,
                    onDisconnected: _onDisconnected,
                    onReceived: _onReceived
                );
            }

            // Otherwise, build the transport directly
            return BuildCore();
        }

        /// <summary>
        /// Internal core builder that creates the correct transport type.
        /// </summary>
        private IParticle BuildCore()
        {
            return _mode switch
            {
                TransportMode.Tcp => BuildTcp(),
                TransportMode.Ipc => BuildIpc(),
                TransportMode.Inproc => BuildInproc(),
                TransportMode.Udp => BuildUdp(),
                _ => throw new InvalidOperationException($"Unsupported transport mode: {_mode}")
            };
        }

        /// <summary>
        /// Builds a TCP client particle.
        /// </summary>
        private IParticle BuildTcp()
        {
            if (_acceptedSocket != null)
            {
                // Wrap an existing accepted socket (for server use)
                var tcp = new Particle(_acceptedSocket, _bufferSize, _parallelism);
                ApplyHandlers(tcp);
                _onConnected?.Invoke(tcp);
                return tcp;
            }

            if (_remoteEndPoint == null)
                throw new InvalidOperationException("TCP requires call WithRemote(endpoint).");

            var client = new Particle(_remoteEndPoint, _bufferSize, _parallelism);
            ApplyHandlers(client);

            return client;
        }

        /// <summary>
        /// Builds an IPC (shared-memory) client particle.
        /// 
        /// !WARNING! Particle is already started!
        /// </summary>
        private IParticle BuildIpc()
        {
            string? chName = _channelName;
            if ((chName == null) || String.IsNullOrWhiteSpace(chName))
                throw new InvalidOperationException("IPC mode requires call WithChannel(name).");

            int ringBytes = _ringCapacity <= 0 ? (128 + (1 << 20)) : _ringCapacity;

            // Each IPC client gets a unique 64-bit ID so the server can identify it
            Random rnd = new Random();
            byte[] buf = new byte[8];
            rnd.NextBytes(buf);
            ulong id = BitConverter.ToUInt64(buf, 0);

            MappedParticle client = new MappedParticle(chName, id, global: false, ringBytes: ringBytes);            
            ApplyHandlers(client);

            client.Start();

            return client;
        }

        /// <summary>
        /// Builds an Inproc (same-process) client particle.
        /// 
        /// !WARNING! Particle is already started!
        /// </summary>
        private IParticle BuildInproc()
        {
            string? chName = _channelName;
            if ((chName == null) || String.IsNullOrWhiteSpace(chName))
                throw new InvalidOperationException("Inproc mode requires WithChannel(name).");

            var client = new InprocParticle(chName, isServer: false, _ringCapacity);
            ApplyHandlers(client);

            client.Start();

            return client;
        }

        /// <summary>
        /// Builds a UDP particle for unicast or multicast communication.
        /// </summary>
        private IParticle BuildUdp()
        {
            // Multicast mode
            if (_multicastGroup != null)
            {
                var port = _multicastPort > 0 ? _multicastPort : 0;
                var local = new IPEndPoint(IPAddress.Any, port);
                var remote = new IPEndPoint(_multicastGroup, port);

                var udp = new UdpParticle(local, remote, _multicastGroup, _disableLoopback, _allowBroadcast);
                ApplyHandlers(udp);
                return udp;
            }

            // Standard unicast UDP
            var localEp = _localEndPoint ?? new IPEndPoint(IPAddress.Any, 0);
            var remoteEp = _remoteEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0);

            var p = new UdpParticle(localEp, remoteEp, allowBroadcast: _allowBroadcast);
            ApplyHandlers(p);

            return p;
        }

        /// <summary>
        /// Applies event handlers to a particle instance (if provided).
        /// </summary>
        private void ApplyHandlers(IParticle p)
        {
            if (_onReceived != null) p.OnReceived = _onReceived;
            if (_onDisconnected != null) p.OnDisconnected = _onDisconnected;
            if (_onConnected != null) p.OnConnected = _onConnected;
        }

        #endregion === Build ===
    }
}
