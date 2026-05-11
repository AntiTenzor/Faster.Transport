using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Faster.Transport.Contracts;

namespace Faster.Transport.Features.Tcp;

/// <summary>
/// High-performance, event-driven TCP reactor server using <see cref="SocketAsyncEventArgs"/>.
/// Accepts multiple clients asynchronously and wraps each connection in an <see cref="IParticle"/>.
/// </summary>
/// <remarks>
/// - Zero-alloc accept loop with reusable event args  
/// - Thread-safe concurrent client map  
/// - Compatible with .NET Framework 4.8+ and .NET 8  
/// - Ideal for real-time, low-latency systems
/// </remarks>
public sealed class Reactor : IDisposable, IReactor
{
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, IParticle> _clients = new();
    private readonly int _bufferSize;
    private readonly int _maxDegreeOfParallelism;
    private SocketAsyncEventArgs? _acceptArgs;
    private int _clientCounter;
    private volatile bool _isRunning;

    public Action<IParticle, ReadOnlyMemory<byte>>? OnReceived { get; set; }

    public Action<IParticle>? OnConnected { get; set; }

    public Reactor(EndPoint bindEndPoint, int backlog = 1024, int bufferSize = 8192, int maxDegreeOfParallelism = 8)
    {
        _listener = new Socket(bindEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        _listener.Bind(bindEndPoint);
        _listener.Listen(backlog);

        _bufferSize = bufferSize;
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    /// <summary>
    /// Starts the reactor and begins accepting incoming TCP connections asynchronously.
    /// </summary>
    public void Start()
    {
        if (_isRunning)
        {
            return;
        }
    
        _isRunning = true;
        _acceptArgs = new SocketAsyncEventArgs();
        _acceptArgs.Completed += AcceptCompleted;

        AcceptLoop(_acceptArgs);
    }

    /// <summary>
    /// Main accept loop. Submits accepts continuously.
    /// </summary>
    private void AcceptLoop(SocketAsyncEventArgs e)
    {
        while (_isRunning && !_cts.IsCancellationRequested)
        {
            e.AcceptSocket = null;

            try
            {
                bool pending = _listener.AcceptAsync(e);
                if (pending)
                    break; // Async operation will invoke AcceptCompleted

                // If completed synchronously, process immediately
                ProcessAccept(e);
            }
            catch (ObjectDisposedException objDispEx)
            {
                Console.Error.WriteLine("[Tcp.Reactor] AcceptLoop catches ObjectDisposedException: {0}\r\n{1}\r\n",
                    objDispEx.Message, objDispEx);

                e.Dispose();
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[Tcp.Reactor] AcceptLoop catches some exception: {0}\r\n{1}\r\n",
                    ex.Message, ex);

                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Accept completion callback.
    /// </summary>
    private void AcceptCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (!_isRunning || _cts.IsCancellationRequested)
        {
            e.Dispose();
            return;
        }

        ProcessAccept(e);
    }

    /// <summary>
    /// Handles a completed accept and sets up the new client.
    /// </summary>
    private void ProcessAccept(SocketAsyncEventArgs e)
    {
        try
        {
            if (e.SocketError != SocketError.Success || e.AcceptSocket == null)
            {
                e.AcceptSocket?.Dispose();
                AcceptLoop(e);
                return;
            }

            var socket = e.AcceptSocket;
            int clientId = Interlocked.Increment(ref _clientCounter);

            // Build client handler from accepted socket
            // Let's do step-by-step:
            ParticleBuilder clientBuilder = new ParticleBuilder();
            clientBuilder = clientBuilder.UseMode(TransportMode.Tcp);
            clientBuilder = clientBuilder.FromAcceptedSocket(socket);
            clientBuilder = clientBuilder.WithBufferSize(_bufferSize);
            clientBuilder = clientBuilder.WithParallelism(_maxDegreeOfParallelism);
            if (OnReceived != null)
                clientBuilder = clientBuilder.OnReceived(OnReceived);
            clientBuilder = clientBuilder.OnDisconnected(_ => _clients.TryRemove(clientId, out _));

            IParticle client = clientBuilder.Build();

            _clients[clientId] = client;

            if (OnConnected != null)
                OnConnected(client);

            // Continue accepting
            AcceptLoop(e);
        }
        catch (ObjectDisposedException objDispEx)
        {
            Console.Error.WriteLine("[Tcp.Reactor] ProcessAccept catches ObjectDisposedException: {0}\r\n{1}\r\n",
                objDispEx.Message, objDispEx);

            e.Dispose();
            return;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[Tcp.Reactor] ProcessAccept catches some exception: {0}\r\n{1}\r\n",
                ex.Message, ex);

            AcceptLoop(e);
        }
    }

    /// <summary>
    /// Stops the reactor gracefully and closes all connections.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cts.Cancel();

        try { _listener.Close(); } catch { }
        try { _acceptArgs?.Dispose(); } catch { }

        foreach (var kv in _clients)
        {
            try
            {
                kv.Value.Dispose();
            }
            catch { }
        }

        _clients.Clear();
    }

    public void Dispose()
    {
        Stop();
        try { _listener.Dispose(); } catch { }
        try { _acceptArgs?.Dispose(); } catch { }
        _cts.Dispose();
    }
}
