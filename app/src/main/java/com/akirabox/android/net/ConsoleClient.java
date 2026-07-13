package com.akirabox.android.net;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Locale;

public final class ConsoleClient {
    private static final int CONNECT_TIMEOUT_MS = 12_000;
    private static final int READ_TIMEOUT_MS = 45_000;
    private static final int BUFFER_SIZE = 32 * 1024;
    private static final int HEAD_SIZE = 4096;

    private ConsoleClient() {}

    public static TransferResult sendPayload(
            File cacheDirectory,
            String ip,
            int port,
            String payloadUrl,
            ProgressListener progress
    ) throws IOException {
        requirePrivateConsole(ip, port);
        URL sourceUrl = UrlUtils.requireHttpUrl(payloadUrl);
        OpenedDownload opened = openDownload(sourceUrl);
        HttpURLConnection connection = opened.connection;
        long announced = connection.getContentLengthLong();
        MessageDigest digest = sha256();

        try (InputStream input = connection.getInputStream(); Socket socket = new Socket()) {
            byte[] head = readHead(input);
            long validationSize = announced > 0 ? announced : Math.max(256, head.length);
            PayloadFormatValidator.validate(head, validationSize, opened.finalUrl);

            socket.setSendBufferSize(BUFFER_SIZE);
            socket.setTcpNoDelay(true);
            socket.connect(new InetSocketAddress(ip, port), CONNECT_TIMEOUT_MS);
            socket.setSoTimeout(CONNECT_TIMEOUT_MS);

            long sent = 0;
            try (OutputStream output = socket.getOutputStream()) {
                if (head.length > 0) {
                    output.write(head);
                    digest.update(head);
                    sent += head.length;
                    notifyProgress(progress, "Transmitindo diretamente ao PS4", percentage(sent, announced), sent, announced);
                }

                byte[] buffer = new byte[BUFFER_SIZE];
                int read;
                while ((read = input.read(buffer)) != -1) {
                    output.write(buffer, 0, read);
                    digest.update(buffer, 0, read);
                    sent += read;
                    notifyProgress(progress, "Transmitindo diretamente ao PS4", percentage(sent, announced), sent, announced);
                }
                output.flush();
                socket.shutdownOutput();
            }
            return new TransferResult(sent, hex(digest.digest()), opened.finalUrl.toString());
        } finally {
            connection.disconnect();
        }
    }

    public static TransferResult validatePayload(
            File cacheDirectory,
            String payloadUrl,
            ProgressListener progress
    ) throws IOException {
        URL sourceUrl = UrlUtils.requireHttpUrl(payloadUrl);
        File temporary = File.createTempFile("orbisdrop-validation-", ".tmp", cacheDirectory);
        try {
            DownloadResult download = download(sourceUrl, temporary, progress);
            return new TransferResult(download.size, download.sha256, download.finalUrl.toString());
        } finally {
            if (temporary.exists() && !temporary.delete()) temporary.deleteOnExit();
        }
    }

    public static TransferResult validatePayloadFile(
            File payload,
            String displayName,
            ProgressListener progress
    ) throws IOException {
        return inspectPayloadFile(payload, displayName, progress);
    }

    public static TransferResult sendPayloadFile(
            File payload,
            String displayName,
            String ip,
            int port,
            ProgressListener progress
    ) throws IOException {
        requirePrivateConsole(ip, port);
        TransferResult validated = inspectPayloadFile(payload, displayName, progress);
        sendFile(payload, ip, port, validated.getBytesSent(), progress);
        return validated;
    }

    private static TransferResult inspectPayloadFile(
            File payload,
            String displayName,
            ProgressListener progress
    ) throws IOException {
        if (payload == null || !payload.isFile()) {
            throw new IOException("O arquivo local não está mais disponível.");
        }
        long announced = payload.length();
        MessageDigest digest = sha256();
        long total = 0;
        ByteArrayOutputStream head = new ByteArrayOutputStream(HEAD_SIZE);

        try (InputStream input = new FileInputStream(payload)) {
            byte[] buffer = new byte[BUFFER_SIZE];
            int read;
            while ((read = input.read(buffer)) != -1) {
                total += read;
                digest.update(buffer, 0, read);
                if (head.size() < HEAD_SIZE) {
                    head.write(buffer, 0, Math.min(read, HEAD_SIZE - head.size()));
                }
                notifyProgress(progress, "Validando arquivo local", percentage(total, announced), total, announced);
            }
        }

        String safeName = displayName == null || displayName.trim().isEmpty()
                ? "payload.bin" : displayName.trim();
        URL localUrl = new URL("https://local.orbisdrop.invalid/"
                + URLEncoder.encode(safeName, StandardCharsets.UTF_8.name()).replace("+", "%20"));
        PayloadFormatValidator.validate(head.toByteArray(), total, localUrl);
        notifyProgress(progress, "Payload validado", 100, total, total);
        return new TransferResult(total, hex(digest.digest()), "arquivo://" + safeName);
    }

    private static DownloadResult download(URL initialUrl, File destination, ProgressListener progress)
            throws IOException {
        OpenedDownload opened = openDownload(initialUrl);
        HttpURLConnection connection = opened.connection;
        long announced = connection.getContentLengthLong();
        MessageDigest digest = sha256();
        long total = 0;
        ByteArrayOutputStream head = new ByteArrayOutputStream(HEAD_SIZE);

        try (InputStream input = connection.getInputStream();
             OutputStream output = new FileOutputStream(destination)) {
            byte[] buffer = new byte[BUFFER_SIZE];
            int read;
            while ((read = input.read(buffer)) != -1) {
                total += read;
                output.write(buffer, 0, read);
                digest.update(buffer, 0, read);
                if (head.size() < HEAD_SIZE) {
                    head.write(buffer, 0, Math.min(read, HEAD_SIZE - head.size()));
                }
                notifyProgress(progress, "Baixando para validação", percentage(total, announced), total, announced);
            }
        } finally {
            connection.disconnect();
        }

        PayloadFormatValidator.validate(head.toByteArray(), total, opened.finalUrl);
        notifyProgress(progress, "Payload validado", 100, total, total);
        return new DownloadResult(total, hex(digest.digest()), opened.finalUrl);
    }

    private static OpenedDownload openDownload(URL initialUrl) throws IOException {
        URL current = initialUrl;
        for (int redirect = 0; redirect <= 5; redirect++) {
            HttpURLConnection connection = (HttpURLConnection) current.openConnection();
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(CONNECT_TIMEOUT_MS);
            connection.setReadTimeout(READ_TIMEOUT_MS);
            connection.setRequestProperty("User-Agent", "OrbisDrop-Android/1.0");
            connection.setRequestProperty("Accept", "application/octet-stream,*/*;q=0.8");
            int status = connection.getResponseCode();

            if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308) {
                String location = connection.getHeaderField("Location");
                connection.disconnect();
                if (location == null) throw new IOException("Redirecionamento sem destino.");
                current = new URL(current, location);
                if (!"http".equalsIgnoreCase(current.getProtocol())
                        && !"https".equalsIgnoreCase(current.getProtocol())) {
                    throw new IOException("O redirecionamento saiu de HTTP/HTTPS.");
                }
                continue;
            }

            if (status < 200 || status >= 300) {
                String error = readSmall(connection.getErrorStream());
                connection.disconnect();
                throw new IOException("Servidor do payload respondeu HTTP " + status
                        + (error.isEmpty() ? "." : ": " + error));
            }
            return new OpenedDownload(connection, current);
        }
        throw new IOException("O link excedeu o limite de redirecionamentos.");
    }

    private static byte[] readHead(InputStream input) throws IOException {
        ByteArrayOutputStream head = new ByteArrayOutputStream(HEAD_SIZE);
        byte[] buffer = new byte[1024];
        while (head.size() < HEAD_SIZE) {
            int read = input.read(buffer, 0, Math.min(buffer.length, HEAD_SIZE - head.size()));
            if (read == -1) break;
            head.write(buffer, 0, read);
        }
        return head.toByteArray();
    }

    private static void sendFile(
            File payload,
            String ip,
            int port,
            long total,
            ProgressListener progress
    ) throws IOException {
        try (Socket socket = new Socket()) {
            socket.setSendBufferSize(BUFFER_SIZE);
            socket.setTcpNoDelay(true);
            socket.connect(new InetSocketAddress(ip, port), CONNECT_TIMEOUT_MS);
            socket.setSoTimeout(CONNECT_TIMEOUT_MS);
            try (InputStream input = new FileInputStream(payload);
                 OutputStream output = socket.getOutputStream()) {
                byte[] buffer = new byte[BUFFER_SIZE];
                long sent = 0;
                int read;
                while ((read = input.read(buffer)) != -1) {
                    output.write(buffer, 0, read);
                    sent += read;
                    notifyProgress(progress, "Transmitindo ao PS4", percentage(sent, total), sent, total);
                }
                output.flush();
                socket.shutdownOutput();
            }
        }
    }

    private static void requirePrivateConsole(String ip, int port) throws IOException {
        if (!UrlUtils.isPrivateIpv4(ip)) {
            throw new IOException("Informe um IPv4 privado válido do PS4.");
        }
        if (port < 1 || port > 65535) {
            throw new IOException("A porta precisa ficar entre 1 e 65535.");
        }
    }

    private static MessageDigest sha256() throws IOException {
        try {
            return MessageDigest.getInstance("SHA-256");
        } catch (NoSuchAlgorithmException impossible) {
            throw new IOException("SHA-256 indisponível.", impossible);
        }
    }

    private static String readSmall(InputStream input) throws IOException {
        if (input == null) return "";
        try (InputStream stream = input; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[1024];
            int remaining = 16 * 1024;
            int read;
            while (remaining > 0 && (read = stream.read(buffer, 0, Math.min(buffer.length, remaining))) != -1) {
                output.write(buffer, 0, read);
                remaining -= read;
            }
            return output.toString(StandardCharsets.UTF_8.name()).trim();
        }
    }

    private static int percentage(long value, long total) {
        if (total <= 0) return -1;
        return (int) Math.max(0, Math.min(100, (value * 100L) / total));
    }

    private static void notifyProgress(
            ProgressListener listener,
            String stage,
            int percent,
            long completed,
            long total
    ) {
        if (listener != null) listener.onProgress(stage, percent, completed, total);
    }

    private static String hex(byte[] value) {
        StringBuilder result = new StringBuilder(value.length * 2);
        for (byte current : value) result.append(String.format(Locale.ROOT, "%02x", current & 0xff));
        return result.toString();
    }

    private static final class OpenedDownload {
        private final HttpURLConnection connection;
        private final URL finalUrl;

        private OpenedDownload(HttpURLConnection connection, URL finalUrl) {
            this.connection = connection;
            this.finalUrl = finalUrl;
        }
    }

    private static final class DownloadResult {
        private final long size;
        private final String sha256;
        private final URL finalUrl;

        private DownloadResult(long size, String sha256, URL finalUrl) {
            this.size = size;
            this.sha256 = sha256;
            this.finalUrl = finalUrl;
        }
    }
}
