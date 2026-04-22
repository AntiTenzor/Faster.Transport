using Faster.Transport.Contracts;
using Faster.Transport.Primitives;

using System;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Faster.Transport.Ipc
{
    /// <summary>
    /// Base class for memory-mapped IPC transport participants (<see cref="MappedParticle"/> or server-side handlers).
    /// Provides common send/receive logic over two <see cref="MappedChannel"/> instances (RX/TX).
    /// </summary>
    /// <remarks>
    /// The base class handles:
    /// <list type="bullet">
    ///   <item>Thread-safe sending through the TX channel.</item>
    ///   <item>Frame forwarding via the RX channel using <see cref="OnReceived"/>.</item>
    ///   <item>Connection lifecycle events (<see cref="OnConnected"/> / <see cref="OnDisconnected"/>).</item>
    /// </list>
    /// </remarks>
    public abstract class MappedParticleBase : IParticle
    {
        /// <summary>
        /// The receive channel (server → client direction).
        /// </summary>
        protected readonly MappedChannel _rx;

        /// <summary>
        /// The transmit channel (client → server direction).
        /// </summary>
        protected readonly MappedChannel _tx;

        /// <inheritdoc/>
        public Action<IParticle, ReadOnlyMemory<byte>>? OnReceived { get; set; }

        /// <inheritdoc/>
        public Action<IParticle>? OnDisconnected { get; set; }

        /// <inheritdoc/>
        public Action<IParticle>? OnConnected { get; set; }

        /// <summary>
        /// Initializes a new base IPC participant using the provided RX/TX channels.
        /// </summary>
        /// <param name="rx">The inbound (read) channel.</param>
        /// <param name="tx">The outbound (write) channel.</param>
        protected MappedParticleBase(MappedChannel rx, MappedChannel tx)
        {
            _rx = rx;
            _tx = tx;
        }

        private void OnRxFrame(ReadOnlyMemory<byte> mem)
            => OnReceived?.Invoke(this, mem);

        /// <summary>
        /// Starts the receive loop and triggers the <see cref="OnConnected"/> event.
        /// </summary>
        public virtual void Start()
        {
            _rx.Start();
            // Wire RX frames directly to OnReceived to minimize allocations
            _rx.OnFrame += payload => OnReceived?.Invoke(this, payload);
            OnConnected?.Invoke(this);
        }

        /// <inheritdoc/>
        public void Send(ReadOnlySpan<byte> payload)
        {
            if (payload.Length == 0)
            {
                return;
            }

            _tx.Send(payload);
        }

        /// <inheritdoc/>
        public ValueTask SendAsync(ReadOnlyMemory<byte> payload)
        {
            if (payload.Length == 0)
            {
                return TaskCompat.CompletedValueTask;
            }

            _tx.Send(payload.Span);
            return TaskCompat.CompletedValueTask;
        }

        /// <inheritdoc/>
        public virtual void Dispose()
        {
            _rx.Dispose();
            _tx.Dispose();
            OnDisconnected?.Invoke(this);
        }
    }
}
