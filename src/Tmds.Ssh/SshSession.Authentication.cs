﻿// This file is part of Tmds.Ssh which is released under MIT.
// See file LICENSE for full license details.

using System.Diagnostics;
using static Tmds.Ssh.UserAuthentication;

namespace Tmds.Ssh;

sealed partial class SshSession
{
    private async Task AuthenticateAsync(SshConnection connection, CancellationToken ct)
    {
        Debug.Assert(_settings is not null);

        Logger.Authenticating(ConnectionInfo.HostName, _settings.UserName);

        // Request ssh-userauth service
        {
            using var serviceRequestMsg = CreateServiceRequestMessage(connection.SequencePool);
            await connection.SendPacketAsync(serviceRequestMsg.Move(), ct).ConfigureAwait(false);
        }
        {
            using Packet serviceAcceptMsg = await connection.ReceivePacketAsync(ct).ConfigureAwait(false);
            ParseServiceAccept(serviceAcceptMsg);
        }

        UserAuthContext context = new UserAuthContext(
            connection, _settings.UserName,
            _settings.PublicKeyAcceptedAlgorithms, SshClientSettings.SupportedPublicKeyAlgorithms,
            _settings.MinimumRSAKeySize, Logger);

        HashSet<Name>? rejectedMethods = null;
        HashSet<Name>? failedMethods = null;
        HashSet<Name>? skippedMethods = null;

        int partialAuthAttempts = 0;
        // Try credentials.
        List<Credential> credentials = new(_settings.CredentialsOrDefault);
        for (int i = 0; i < credentials.Count; i++)
        {
            Credential credential = credentials[i];

            AuthResult authResult = AuthResult.None;
            Name method;
            if (credential is PasswordCredential passwordCredential)
            {
                if (TryMethod(AlgorithmNames.Password))
                {
                    authResult = await PasswordAuth.TryAuthenticate(passwordCredential, context, ConnectionInfo, Logger, ct).ConfigureAwait(false);
                }
            }
            else if (credential is PrivateKeyCredential keyCredential)
            {
                if (TryMethod(AlgorithmNames.PublicKey))
                {
                    authResult = await PublicKeyAuth.TryAuthenticate(keyCredential, context, ConnectionInfo, Logger, ct).ConfigureAwait(false);
                }
            }
            else if (credential is KerberosCredential kerberosCredential)
            {
                if (TryMethod(AlgorithmNames.GssApiWithMic))
                {
                    authResult = await GssApiAuth.TryAuthenticate(kerberosCredential, context, ConnectionInfo, Logger, ct).ConfigureAwait(false);
                }
            }
            else if (credential is NoCredential)
            {
                if (TryMethod(AlgorithmNames.None))
                {
                    authResult = await NoneAuth.TryAuthenticate(context, ConnectionInfo, Logger, ct).ConfigureAwait(false);
                }
            }
            else if (credential is SshAgentCredentials sshAgentCredentials)
            {
                if (TryMethod(AlgorithmNames.PublicKey))
                {
                    authResult = await SshAgentAuth.TryAuthenticate(sshAgentCredentials, context, ConnectionInfo, Logger, ct).ConfigureAwait(false);
                }
            }
            else
            {
                throw new NotImplementedException("Unsupported credential type: " + credential.GetType().FullName);
            }

            if (authResult == AuthResult.Success)
            {
                return;
            }

            if (authResult is AuthResult.None)
            {
                continue;
            }

            // We didn't try the method, skip to the next credential.
            if (authResult is AuthResult.SkippedMethodNotAllowed)
            {
                rejectedMethods ??= new();
                rejectedMethods.Add(method);
                continue;
            }
            else if (authResult is AuthResult.Failure or AuthResult.FailureMethodNotAllowed)
            {
                failedMethods ??= new();
                failedMethods.Add(method);
            }
            else if (authResult is AuthResult.Skipped)
            {
                skippedMethods ??= new();
                skippedMethods.Add(method);
            }

            // Don't try a failed/skipped credential again if it matched an accepted method.
            if (authResult is AuthResult.Failure or AuthResult.Skipped)
            {
                credentials.RemoveAt(i);
                i--;
            }

            if (authResult == AuthResult.Partial)
            {
                // The same method may be requested multiple times with partial auth.
                // The client should be providing different credentials when the same method is tried again.
                // Move the current credential to the back of the list so we don't try it as the first one once more.
                credentials.RemoveAt(i);
                credentials.Add(credential);

                // Start over (but limit the amount of times we want to start over to avoid an infinite loop).
                if (++partialAuthAttempts == Constants.MaxPartialAuths)
                {
                    break;
                }
                else
                {
                    i = -1;
                }
            }

            bool TryMethod(Name credentialMethod)
            {
                method = credentialMethod;
                bool tryMethod = context.IsMethodAccepted(method) != false;
                if (!tryMethod)
                {
                    authResult = AuthResult.SkippedMethodNotAllowed;
                }
                return tryMethod;
            }
        }

        throw new ConnectFailedException(
                    ConnectFailedReason.AuthenticationFailed,
                    $"Authentication failed. {DescribeMethodListBehavior("failed", failedMethods)} {DescribeMethodListBehavior("were skipped", skippedMethods)} {DescribeMethodListBehavior("were rejected", rejectedMethods)}", ConnectionInfo);

        static string DescribeMethodListBehavior(string state, IEnumerable<Name>? methods)
            => methods is null ? $"No methods {state}."
                              : $"These methods {state}: {string.Join(", ", methods)}.";
    }

    private static Packet CreateServiceRequestMessage(SequencePool sequencePool)
    {
        using var packet = sequencePool.RentPacket();
        var writer = packet.GetWriter();
        writer.WriteMessageId(MessageId.SSH_MSG_SERVICE_REQUEST);
        writer.WriteString("ssh-userauth");
        return packet.Move();
    }

    private static void ParseServiceAccept(ReadOnlyPacket packet)
    {
        var reader = packet.GetReader();
        reader.ReadMessageId(MessageId.SSH_MSG_SERVICE_ACCEPT);
        reader.SkipString();
        reader.ReadEnd();
    }
}
