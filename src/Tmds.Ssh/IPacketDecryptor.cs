// This file is part of Tmds.Ssh which is released under MIT.
// See file LICENSE for full license details.

namespace Tmds.Ssh;

interface IPacketDecryptor : IDisposable
{
    bool TryDecrypt(Sequence receiveBuffer, uint sequenceNumber, int maxLength, out Packet packet);
}
