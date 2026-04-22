using System;
using System.Net;

using Faster.Transport.Ipc;
using Faster.Transport.Inproc;
using Faster.Transport.Contracts;
using Faster.Transport.Features.Tcp;

namespace Faster.Transport
{
    /// <summary>
    /// A fluent builder for creating and configuring a **server-side Reactor**.
    /// </summary>
    /// <remarks>
    /// The <see cref="ReactorBuilder"/> helps you build a high-performance server
    /// that can accept and manage multiple clients simultaneously.
    /// 
    /// <para>Depending on your use case, it can build one of these:</para>
    /// <list type="bullet">
    /// <item><b>TCP Reactor</b> — Listens on a network port (for LAN or Internet connections).</item>
    /// <item><b>IPC Reactor</b> — Allows local processes on the same machine to communicate efficiently.</item>
    /// <item><b>Inproc Reactor</b> — Handles communication entirely inside the same process (fastest option).</item>
    /// </list>
    /// 
    /// Once built, you can call <c>Start()</c> on the returned reactor to begin accepting connections.
    /// </remarks>
    public sealed class ReactorBuilder
    {
        #region === Configuration Fields ===

        /// <summary>
        /// What kind of server we’re building (default: TCP)
        /// </summary>
        private TransportMode _mode = TransportMode.Tcp;

        /// <summary>
        /// The endpoint the TCP server will bind to (e.g., IP and port)
        /// </summary>
        private EndPoint? _bindEndPoint;

        /// <summary>
        /// Used by IPC and Inproc modes to identify shared channels
        /// </summary>
        private string? _channelName;

        /// <summary>
        /// Used by IPC and Inproc modes to identify visibility scope of shared memory areas.
        ///
        /// !WARNING! Global visibility REQUIRES admin privileges!
        /// </summary>
        private bool _isGlobal = false;

        // Tuning options
        private int _backlog = 1024; // How many pending connections the OS can queue
        private int _bufferSize = 8192; // Default per-client buffer
        private int _maxDegreeOfParallelism = 8; // How many threads can handle messages in parallel
        private int _ringCapacity = 128 + (1 << 20); // ≈ 1 MB ring buffer for IPC/Inproc

        // Optional event handlers for client connect/receive events
        private Action<IParticle>? _onConnected;
        private Action<IParticle, ReadOnlyMemory<byte>>? _onReceived;

        #endregion === Configuration Fields ===

        #region === Fluent Configuration ===

        /// <summary>
        /// Sets the communication mode for this reactor (TCP, IPC, or Inproc).
        /// </summary>
        /// <param name="mode">Which type of server to build.</param>
        /// <returns>The same builder for chaining.</returns>
        public ReactorBuilder UseMode(TransportMode mode)
        {
            _mode = mode;
            return this;
        }

        /// <summary>
        /// Specifies the IP endpoint to bind to.  
        /// Only required when using <see cref="TransportMode.Tcp"/>.
        /// </summary>
        /// <param name="endpoint">An <see cref="EndPoint"/> (e.g., 0.0.0.0:5000).</param>
        public ReactorBuilder BindTo(EndPoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            // TODO: throw an exception if transport mode is not suitable for EndPoint?
            // i.e. _mode == Inproc OR Ipc
            _bindEndPoint = endpoint;
            return this;
        }

        /// <summary>
        /// Sets the name of the shared channel.  
        /// This is required for <see cref="TransportMode.Ipc"/> and <see cref="TransportMode.Inproc"/>.
        /// </summary>
        /// <param name="channelName">A unique name that both server and clients must share.</param>
        public ReactorBuilder WithChannel(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                throw new ArgumentException("Channel name cannot be null or empty.", nameof(channelName));

            _channelName = channelName.Trim();
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
        public ReactorBuilder WithGlobal(bool isGlobal)
        {
            // TODO: throw an exception if transport mode is not suitable for isGlobal flag?
            // i.e. _mode == Tcp OR Udp

            _isGlobal = isGlobal;
            return this;
        }

        /// <summary>
        /// Adjusts the backlog — how many pending connections can wait before being accepted.
        /// </summary>
        public ReactorBuilder WithBacklog(int backlog)
        {
            if (backlog <= 0)
                throw new ArgumentOutOfRangeException(nameof(backlog));

            _backlog = backlog;
            return this;
        }

        /// <summary>
        /// Sets how much buffer memory is allocated per client connection.
        /// </summary>
        public ReactorBuilder WithBufferSize(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            _bufferSize = size;
            return this;
        }

        /// <summary>
        /// Controls how many operations can process data simultaneously.
        /// </summary>
        public ReactorBuilder WithParallelism(int degree)
        {
            if (degree <= 0)
                throw new ArgumentOutOfRangeException(nameof(degree));

            _maxDegreeOfParallelism = degree;
            return this;
        }

        /// <summary>
        /// Sets the total capacity (in bytes) of the message ring buffer used by IPC/Inproc.
        /// </summary>
        public ReactorBuilder WithRingCapacity(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            // TODO: throw an exception if transport mode is not suitable for ring capacity?
            // i.e. _mode == Tcp OR Udp

            _ringCapacity = capacity;
            return this;
        }

        /// <summary>
        /// Registers a callback that triggers when a new client connects.
        /// </summary>
        public ReactorBuilder OnConnected(Action<IParticle> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _onConnected = handler;
            return this;
        }

        /// <summary>
        /// Registers a callback that triggers when any connected client sends a message.
        /// </summary>
        public ReactorBuilder OnReceived(Action<IParticle, ReadOnlyMemory<byte>> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _onReceived = handler;
            return this;
        }

        #endregion === Fluent Configuration ===

        #region === Build ===

        /// <summary>
        /// Builds and returns the appropriate reactor instance depending on the selected mode.
        /// </summary>
        /// <returns>
        /// An <see cref="IReactor"/> that is ready to <see cref="IReactor.Start"/> and accept clients.
        /// </returns>
        /// <exception cref="InvalidOperationException">Thrown if required configuration is missing.</exception>
        public IReactor Build()
        {
            // Choose which type of reactor to create based on the configured mode
            return _mode switch
            {
                TransportMode.Tcp => BuildTcpReactor(),
                TransportMode.Ipc => BuildIpcReactor(),
                TransportMode.Inproc => BuildInprocReactor(),
                _ => throw new InvalidOperationException($"Unsupported transport mode: {_mode}")
            };
        }

        /// <summary>
        /// Builds a TCP server that listens for network clients.
        /// </summary>
        /// <remarks>
        /// This server accepts multiple simultaneous TCP connections asynchronously.
        /// </remarks>
        private IReactor BuildTcpReactor()
        {
            if (_bindEndPoint == null)
                throw new InvalidOperationException("TCP mode requires a call to BindTo().");

            var reactor = new Reactor(
                bindEndPoint: _bindEndPoint,
                backlog: _backlog,
                bufferSize: _bufferSize,
                maxDegreeOfParallelism: _maxDegreeOfParallelism);

            // Hook up event handlers if provided
            if (_onConnected != null)
                reactor.OnConnected = _onConnected;

            if (_onReceived != null)
                reactor.OnReceived = _onReceived;

            reactor.Start();

            return reactor;
        }

        /// <summary>
        /// Builds an IPC (inter-process communication) reactor.
        /// </summary>
        /// <remarks>
        /// IPC reactors allow multiple **processes** on the same machine
        /// to communicate efficiently using shared memory or Unix domain sockets.
        /// </remarks>
        private IReactor BuildIpcReactor()
        {
            string? chName = _channelName;
            if ((chName == null) || String.IsNullOrWhiteSpace(chName))
                throw new InvalidOperationException("IPC mode requires call WithChannel(channelName).");

            // Create a shared-memory reactor
            var reactor = new MappedReactor(
                baseName: chName,
                global: _isGlobal,
                ringBytes: _ringCapacity);

            if (_onConnected != null)
                reactor.OnConnected = _onConnected;

            if (_onReceived != null)
                reactor.OnReceived = _onReceived;

            reactor.Start();

            return reactor;
        }

        /// <summary>
        /// Builds an in-process (same-application) message hub reactor.
        /// </summary>
        /// <remarks>
        /// This type of reactor doesn’t use sockets or shared memory — 
        /// it simply connects multiple components inside your program using memory queues.
        /// It’s extremely fast because everything happens in RAM within the same process.
        /// </remarks>
        private IReactor BuildInprocReactor()
        {
            string? chName = _channelName;
            if ((chName == null) || String.IsNullOrWhiteSpace(chName))
                throw new InvalidOperationException("Inproc mode requires call WithChannel(channelName).");

            var reactor = new InprocReactor(
                name: chName,
                bufferSize: _bufferSize,
                ringCapacity: _ringCapacity,
                maxDegreeOfParallelism: _maxDegreeOfParallelism);

            if (_onConnected != null)
                reactor.OnConnected = _onConnected;

            if (_onReceived != null)
                reactor.OnReceived = _onReceived;
                    
            return reactor;
        }

        #endregion === Build ===
    }
}