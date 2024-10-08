// This file is part of Tmds.Ssh which is released under MIT.
// See file LICENSE for full license details.

using Microsoft.Extensions.Logging;

namespace Tmds.Ssh;

partial class UserAuthentication
{
    // https://datatracker.ietf.org/doc/html/rfc4252 - Password Authentication Method: "password"
    public sealed class PasswordAuth
    {
        public static async Task<bool> TryAuthenticate(PasswordCredential passwordCredential, UserAuthContext context, SshConnectionInfo connectionInfo, ILogger<SshClient> logger, CancellationToken ct)
        {
            string? password = passwordCredential.GetPassword();
            if (password is not null)
            {
                if (!context.TryStartAuth(AlgorithmNames.Password))
                {
                    return false;
                }

                logger.PasswordAuth();

                {
                    using var userAuthMsg = CreatePasswordRequestMessage(context.SequencePool, context.UserName, password);
                    await context.SendPacketAsync(userAuthMsg.Move(), ct).ConfigureAwait(false);
                }

                bool success = await context.ReceiveAuthIsSuccesfullAsync(ct).ConfigureAwait(false);
                if (success)
                {
                    return true;
                }
            }

            return false;
        }

        private static Packet CreatePasswordRequestMessage(SequencePool sequencePool, string userName, string password)
        {
            using var packet = sequencePool.RentPacket();
            var writer = packet.GetWriter();
            writer.WriteMessageId(MessageId.SSH_MSG_USERAUTH_REQUEST);
            writer.WriteString(userName);
            writer.WriteString("ssh-connection");
            writer.WriteString("password");
            writer.WriteBoolean(false);
            writer.WriteString(password);
            return packet.Move();
        }
   }
}