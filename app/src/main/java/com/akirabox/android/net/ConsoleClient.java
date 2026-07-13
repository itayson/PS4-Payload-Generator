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
    private static final int BUFFER_SIZE = 8 * 1024;

    private ConsoleClient() {}

    public static void testPackageInstaller(String ip, int port) throws IOException {
        requirePrivateConsole(ip, port);
        postJson(new URL("http://" + ip + ":" + port + "/api/is_exists"), "{\"title_id\":\"CUSA00000\"}");
    }

    public static void installPackage(String ip, int port, String packageUrl) throws IOException {
        requirePrivateConsole(ip, port);
        URL directUrl = UrlUtils.requireHttpUrl(packageUrl);
        String body = "{\"type\":\"direct\",\"packages\":[\"" + jsonEscape(directUrl.toString()) + "\"]}";
        postJson(new URL("http://" + ip + ":" + port + "/api/install"), body);
    }

    public static TransferResult sendPayload(File cacheDirectory, String ip, int port, String payloadUrl, ProgressListener progress) throws IOException {
        requirePrivateConsole(ip, port);
        URL sourceUrl = UrlUtils.requireHttpUrl(payloadUrl);
        File temporary = File.createTempFile("orbisdrop-payload-", ".tmp", cacheDirectory);
        try {
            DownloadResult download = download(sourceUrl, temporary, progress);
            sendFile(temporary, ip, port, download.size, progress);
            return new TransferResult(download.size, download.sha256, download.finalUrl.toString());
        } finally {
            if (temporary.exists() && !temporary.delete()) temporary.deleteOnExit();
        }
    }

    public static TransferResult validatePayload(File cacheDirectory, String payloadUrl, ProgressListener progress) throws IOException {
        URL sourceUrl = UrlUtils.requireHttpUrl(payloadUrl);
        File temporary = File.createTempFile("orbisdrop-validation-", ".tmp", cacheDirectory);
        try {
            DownloadResult download = download(sourceUrl, temporary, progress);
            return new TransferResult(download.size, download.sha256, download.finalUrl.toString());
        } finally {
            if (temporary.exists() && !temporary.delete()) temporary.deleteOnExit();
        }
    }

    public static TransferResult validatePayloadFile(File payload, String displayName, ProgressListener progress) throws IOException {
        return inspectPayloadFile(payload, displayName, progress);
    }

    public static TransferResult sendPayloadFile(File payload, String displayName, String ip, int port, ProgressListener progress) throws IOException {
        requirePrivateConsole(ip, port);
        TransferResult validated = inspectPayloadFile(payload, displayName, progress);
        sendFile(payload, ip, port, validated.getBytesSent(), progress);
        return validated;
    }

    private static TransferResult inspectPayloadFile(File payload, String displayName, ProgressListener progress) throws IOException {
        if (payload == null || !payload.isFile()) throw new IOException("O arquivo local não está mais disponível.");
        long announced = payload.length();
        MessageDigest digest = sha256();
        long total = 0;
        ByteArrayOutputStream head = new ByteArrayOutputStream(4096);
        try (InputStream input = new FileInputStream(payload)) {
            byte[] buffer = new byte[BUFFER_SIZE];
            int read;
            while ((read = input.read(buffer)) != -1) {
                total += read;
                digest.update(buffer, 0, read);
                if (head.size() < 4096) head.write(buffer, 0, Math.min(read, 4096 - head.size()));
                notifyProgress(progress, "Validando arquivo local", percentage(total, announced), total, announced);
            }
        }
        String safeName = displayName == null || displayName.trim().isEmpty() ? "payload.bin" : displayName.trim();
        URL localUrl = new URL("https://local.orbisdrop.invalid/" + URLEncoder.encode(safeName, StandardCharsets.UTF_8.name()).replace("+", "%20"));
        PayloadFormatValidator.validate(head.toByteArray(), total, localUrl);
        notifyProgress(progress, "Payload validado", 100, total, total);
        return new TransferResult(total, hex(digest.digest()), "arquivo://" + safeName);
    }

    private static DownloadResult download(URL initialUrl, File destination, ProgressListener progress) throws IOException {
        URL current = initialUrl;
        HttpURLConnection connection = null;
        boolean responseReady = false;
        for (int redirect = 0; redirect <= 5; redirect++) {
            connection = (HttpURLConnection) current.openConnection();
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
                if (!"http".equalsIgnoreCase(current.getProtocol()) && !"https".equalsIgnoreCase(current.getProtocol())) {
                    throw new IOException("O redirecionamento saiu de HTTP/HTTPS.");
                }
                continue;
            }
            if (status < 200 || status >= 300) {
                String error = readSmall(connection.getErrorStream());
                connection.disconnect();
                throw new IOException("Servidor do payload respondeu HTTP " + status + (error.isEmpty() ? "." : ": " + error));
            }
            responseReady = true;
            break;
        }
        if (connection == null || !responseReady) {
            if (connection != null) connection.disconnect();
            throw new IOException("O link excedeu o limite de redirecionamentos.");
        }

        long announced = connection.getContentLengthLong();
        MessageDigest digest = sha256();
        long total = 0;
        ByteArrayOutputStream head = new ByteArrayOutputStream(4096);
        try (InputStream input = connection.getInputStream(); OutputStream output = new FileOutputStream(destination)) {
            byte[] buffer = new byte[BUFFER_SIZE];
            int read;
            while ((read = input.read(buffer)) != -1) {
                total += read;
                output.write(buffer, 0, read);
                digest.update(buffer, 0, read);
                if (head.size() < 4096) head.write(buffer, 0, Math.min(read, 4096 - head.size()));
                notifyProgress(progress, "Baixando payload", announced > 0 ? percentage(total, announced) : -1, total, announced);
            }
        } finally {
            connection.disconnect();
        }
        PayloadFormatValidator.validate(head.toByteArray(), total, current);
        notifyProgress(progress, "Payload validado", 100, total, total);
        return new DownloadResult(total, hex(digest.digest()), current);
    }

    private static void sendFile(File payload, String ip, int port, long total, ProgressListener progress) throws IOException {
        try (Socket socket = new Socket()) {
            socket.setSendBufferSize(BUFFER_SIZE);
            socket.setTcpNoDelay(true);
            socket.connect(new InetSocketAddress(ip, port), CONNECT_TIMEOUT_MS);
            socket.setSoTimeout(CONNECT_TIMEOUT_MS);
            try (InputStream input = new FileInputStream(payload); OutputStream output = socket.getOutputStream()) {
                byte[] buffer = new byte[BUFFER_SIZE];
                long sent = 0;
                int read;
                while ((read = input.read(buffer)) != -1) {
                    output.write(buffer, 0, read);
                    sent += read;
                    notifyProgress(progress, "Transmitindo ao console", percentage(sent, total), sent, total);
                }
                output.flush();
                socket.shutdownOutput();
            }
        }
    }

    private static MessageDigest sha256() throws IOException {
        try { return MessageDigest.getInstance("SHA-256"); }
        catch (NoSuchAlgorithmException impossible) { throw new IOException("SHA-256 indisponível.", impossible); }
    }

    private static String postJson(URL endpoint, String json) throws IOException {
        HttpURLConnection connection = (HttpURLConnection) endpoint.openConnection();
        connection.setConnectTimeout(CONNECT_TIMEOUT_MS);
        connection.setReadTimeout(20_000);
        connection.setRequestMethod("POST");
        connection.setDoOutput(true);
        connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
        byte[] body = json.getBytes(StandardCharsets.UTF_8);
        connection.setFixedLengthStreamingMode(body.length);
        try (OutputStream output = connection.getOutputStream()) { output.write(body); }
        int status = connection.getResponseCode();
        InputStream stream = status >= 400 ? connection.getErrorStream() : connection.getInputStream();
        String response = readSmall(stream);
        connection.disconnect();
        if (status < 200 || status >= 300) throw new IOException("Console respondeu HTTP " + status + (response.isEmpty() ? "." : ": " + response));
        return response;
    }

    private static void requirePrivateConsole(String ip, int port) throws IOException {
        if (!UrlUtils.isPrivateIpv4(ip)) throw new IOException("Informe um IPv4 privado válido do console.");
        if (port < 1 || port > 65535) throw new IOException("A porta precisa ficar entre 1 e 65535.");
    }

    private static String jsonEscape(String value) {
        return value.replace("\\", "\\\\").replace("\"", "\\\"").replace("\b", "\\b").replace("\f", "\\f")
                .replace("\n", "\\n").replace("\r", "\\r").replace("\t", "\\t");
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

    private static void notifyProgress(ProgressListener listener, String stage, int percent, long completed, long total) {
        if (listener != null) listener.onProgress(stage, percent, completed, total);
    }

    private static String hex(byte[] value) {
        StringBuilder result = new StringBuilder(value.length * 2);
        for (byte current : value) result.append(String.format(Locale.ROOT, "%02x", current & 0xff));
        return result.toString();
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
