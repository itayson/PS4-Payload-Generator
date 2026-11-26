using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Tayson.Providers;

// A local write is not a remote execution acknowledgement.
public sealed class PayloadDeliveryUncertainException : IOException
{
    public PayloadDeliveryUncertainException(Exception inner)
        : base("Delivery after connection is uncertain; do not replay automatically.", inner) { }
}

public static class PayloadTransport
{
    public const int DefaultPort = 9090;

    public static async Task SendAsync(string host, byte[] payload, int port = DefaultPort,
        int connectTimeoutMs = 2500, int writeTimeoutMs = 15000, int attempts = 4)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Missing host.", nameof(host));
        if (payload == null || payload.Length == 0) throw new ArgumentException("Empty payload.", nameof(payload));
        if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (connectTimeoutMs < 1 || writeTimeoutMs < 1 || attempts < 1 || attempts > 4)
            throw new ArgumentOutOfRangeException("Timeout/attempt parameters");

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    await WithDeadline(client.ConnectAsync(host, port), connectTimeoutMs);
                }
                catch (TimeoutException) when (attempt < attempts)
                {
                    client.Dispose();
                    await Task.Delay(350 * attempt);
                    continue;
                }
                catch (SocketException ex) when (attempt < attempts && IsRetryable(ex.SocketErrorCode))
                {
                    client.Dispose();
                    await Task.Delay(350 * attempt);
                    continue;
                }

                // From here on, a failure may mean partial delivery. No retries.
                try
                {
                    using (NetworkStream stream = client.GetStream())
                    {
                        await WithDeadline(stream.WriteAsync(payload, 0, payload.Length), writeTimeoutMs);
                        await WithDeadline(stream.FlushAsync(), writeTimeoutMs);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException ||
                    ex is TimeoutException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    throw new PayloadDeliveryUncertainException(ex);
                }
                return;
            }
        }
        throw new InvalidOperationException("Connection attempts exhausted.");
    }

    static async Task WithDeadline(Task operation, int milliseconds)
    {
        if (await Task.WhenAny(operation, Task.Delay(milliseconds)) != operation)
        {
            // Caller disposes the socket. Observe a later fault from the pending operation.
            _ = operation.ContinueWith(t => { _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            throw new TimeoutException("TCP operation timed out.");
        }
        await operation;
    }

    static bool IsRetryable(SocketError error)
        => error == SocketError.ConnectionRefused || error == SocketError.TimedOut ||
           error == SocketError.HostUnreachable || error == SocketError.NetworkUnreachable;
}
