using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace Tayson.Providers;

/// <summary>
/// Transport for the Tayson 4.0 receiver architecture.
///
/// Phase 1 deliberately does not install packages. It only:
/// 1) checks whether the receiver is already alive on TCP 9091;
/// 2) if needed, sends the bundled receiver ELF once to GoldHEN BinLoader 9090;
/// 3) waits for a positive PING/PONG handshake.
///
/// The PUT protocol is implemented now so the next phase can stream a validated
/// PKG without changing the receiver contract.
/// </summary>
public static class ReceiverClient
{
    public const int BinLoaderPort = 9090;
    public const int ReceiverPort = 9091;
    const string ReceiverAsset = "tayson_receiver.elf";

    public static async Task<string> EnsureReadyAsync(
        string host,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidDataException("Informe o IP do PS4.");

        try
        {
            return await PingAsync(host, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is SocketException ||
            ex is TimeoutException ||
            ex is IOException)
        {
            // Receiver is not running yet. Bootstrap it through BinLoader.
        }

        byte[] receiver = await ReadReceiverAssetAsync(token).ConfigureAwait(false);
        if (receiver.Length == 0)
            throw new InvalidDataException("O Receiver ELF incorporado está vazio.");

        await SendElfOnceAsync(host, receiver, token).ConfigureAwait(false);

        Exception last = null;
        for (int attempt = 1; attempt <= 20; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return await PingAsync(host, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is SocketException ||
                ex is TimeoutException ||
                ex is IOException)
            {
                last = ex;
                await Task.Delay(350, token).ConfigureAwait(false);
            }
        }

        throw new IOException(
            "O Receiver foi enviado ao BinLoader, mas não respondeu na porta 9091.",
            last);
    }

    public static async Task<string> PingAsync(
        string host,
        CancellationToken token = default)
    {
        using TcpClient client = await ConnectAsync(
            host,
            ReceiverPort,
            1800,
            token).ConfigureAwait(false);

        using NetworkStream stream = client.GetStream();
        byte[] ping = Encoding.ASCII.GetBytes("PING\n");
        await stream.WriteAsync(ping, 0, ping.Length, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);

        string line = await ReadLineAsync(stream, 256, 1800, token).ConfigureAwait(false);
        if (!line.StartsWith("PONG TAYSON-RX ", StringComparison.Ordinal))
            throw new IOException("Resposta inesperada do Receiver.");

        return line.Substring("PONG ".Length).Trim();
    }

    public static async Task UploadAsync(
        string host,
        string fileName,
        Stream input,
        long length,
        Action<long> progress = null,
        CancellationToken token = default)
    {
        if (input == null || !input.CanRead)
            throw new ArgumentException("Stream de entrada inválido.", nameof(input));
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        string safe = SafeFileName(fileName);

        using TcpClient client = await ConnectAsync(
            host,
            ReceiverPort,
            3000,
            token).ConfigureAwait(false);
        using NetworkStream stream = client.GetStream();

        byte[] header = Encoding.ASCII.GetBytes(
            "PUT " + length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " " + safe + "\n");
        await stream.WriteAsync(header, 0, header.Length, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);

        string ready = await ReadLineAsync(stream, 256, 3000, token).ConfigureAwait(false);
        if (!string.Equals(ready, "READY", StringComparison.Ordinal))
            throw new IOException("Receiver recusou a transferência: " + ready);

        byte[] buffer = new byte[256 * 1024];
        long sent = 0;
        while (sent < length)
        {
            int want = (int)Math.Min(buffer.Length, length - sent);
            int n = await input.ReadAsync(buffer, 0, want, token).ConfigureAwait(false);
            if (n <= 0)
                throw new EndOfStreamException("A origem terminou antes do tamanho esperado.");

            await stream.WriteAsync(buffer, 0, n, token).ConfigureAwait(false);
            sent += n;
            progress?.Invoke(sent);
        }

        await stream.FlushAsync(token).ConfigureAwait(false);

        string result = await ReadLineAsync(stream, 256, 10000, token).ConfigureAwait(false);
        if (!string.Equals(result, "STORED", StringComparison.Ordinal))
            throw new IOException("Receiver não confirmou o arquivo: " + result);
    }

    public static string SafeFileName(string value)
    {
        string name = Path.GetFileName(value ?? "").Trim();
        if (name.Length == 0) name = "package.pkg";

        var sb = new StringBuilder(Math.Min(name.Length, 96));
        foreach (char c in name)
        {
            bool ok =
                (c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '.' || c == '_' || c == '-';
            sb.Append(ok ? c : '_');
            if (sb.Length == 96) break;
        }

        string safe = sb.ToString().TrimStart('.');
        return safe.Length == 0 ? "package.pkg" : safe;
    }

    static async Task<byte[]> ReadReceiverAssetAsync(CancellationToken token)
    {
        using Stream input = await FileSystem.OpenAppPackageFileAsync(ReceiverAsset);
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, 81920, token).ConfigureAwait(false);
        return buffer.ToArray();
    }

    static async Task SendElfOnceAsync(
        string host,
        byte[] payload,
        CancellationToken token)
    {
        TcpClient client = null;
        SocketException last = null;

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                client = await ConnectAsync(
                    host,
                    BinLoaderPort,
                    2500,
                    token).ConfigureAwait(false);
                break;
            }
            catch (SocketException ex) when (attempt < 4)
            {
                last = ex;
                await Task.Delay(300 * attempt, token).ConfigureAwait(false);
            }
            catch (TimeoutException) when (attempt < 4)
            {
                await Task.Delay(300 * attempt, token).ConfigureAwait(false);
            }
        }

        if (client == null)
        {
            if (last != null) throw last;
            throw new TimeoutException("GoldHEN BinLoader 9090 não respondeu.");
        }

        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            await stream.WriteAsync(payload, 0, payload.Length, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
    }

    static async Task<TcpClient> ConnectAsync(
        string host,
        int port,
        int timeoutMs,
        CancellationToken token)
    {
        var client = new TcpClient();
        try
        {
            Task connect = client.ConnectAsync(host, port);
            Task delay = Task.Delay(timeoutMs, token);
            Task completed = await Task.WhenAny(connect, delay).ConfigureAwait(false);

            if (!ReferenceEquals(completed, connect))
            {
                client.Dispose();
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("Tempo limite de conexão excedido.");
            }

            await connect.ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    static async Task<string> ReadLineAsync(
        Stream stream,
        int maxBytes,
        int timeoutMs,
        CancellationToken token)
    {
        var buffer = new byte[maxBytes];
        int count = 0;

        while (count < buffer.Length)
        {
            Task<int> read = stream.ReadAsync(
                buffer,
                count,
                1,
                token);
            Task completed = await Task.WhenAny(
                read,
                Task.Delay(timeoutMs, token)).ConfigureAwait(false);

            if (!ReferenceEquals(completed, read))
            {
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("Tempo limite aguardando resposta do Receiver.");
            }

            int n = await read.ConfigureAwait(false);
            if (n <= 0)
                throw new IOException("Conexão encerrada antes da resposta.");
            if (buffer[count] == (byte)'\n')
                return Encoding.ASCII.GetString(buffer, 0, count).TrimEnd('\r');
            count += n;
        }

        throw new InvalidDataException("Resposta do Receiver excedeu o limite.");
    }
}
