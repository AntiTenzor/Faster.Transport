using Faster.Transport.Contracts;
using Faster.Transport.Primitives;

using System;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Faster.Transport.Ipc
{
    /// <summary>
    /// Represents a single connected client’s communication channel within the reactor.
    /// </summary>
    public sealed class ClientParticle : MappedParticleBase
    {
        private readonly ulong _id;
        private readonly MappedReactor _owner;

        public ClientParticle(MappedChannel rx, MappedChannel tx, ulong id, MappedReactor owner)
            : base(rx, tx)
        {
            _id = id;
            _owner = owner;

            // Forward messages to the reactor’s OnReceived callback.
            OnReceived += (self, data) => _owner.OnReceived?.Invoke(self, data);
        }

        /// <summary>
        /// Cleans up resources and notifies the reactor that the client disconnected.
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            string ids = _id.ToString("X", CultureInfo.InvariantCulture);
            int padLen = 2 * (ids.Length % 2 + 1);
            ids = ids.PadLeft(padLen, '0');

            string res = "[Ipc.ClientParticle|ID:0x" + ids + "]";
            return res;
        }
    }
}
