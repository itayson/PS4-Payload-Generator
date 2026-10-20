package com.tayson.proxylab;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.util.*;
import java.util.concurrent.*;

/** Servidor HTTP de uso pontual: uma única URL fixa, token aleatório, sem proxy aberto. */
public final class ProxyServer {
    private static final int PORT = 8765;
    private final ExecutorService clients = Executors.newFixedThreadPool(4);
    private final String key;
    private final UrlResolver.Result upstream;
    private volatile boolean running;
    private ServerSocket server;
    private Thread acceptThread;

    public ProxyServer(UrlResolver.Result upstream) {
        this.upstream = upstream;
        byte[] random = new byte[16]; new SecureRandom().nextBytes(random);
        StringBuilder sb = new StringBuilder();
        for (byte x : random) sb.append(String.format(java.util.Locale.ROOT, "%02x", x & 0xff));
        key = sb.toString();
    }

    public synchronized String start(String localIp) throws IOException {
        if (running) return endpoint(localIp);
        if (!upstream.rangeSupported) throw new IOException("Range 206 não confirmado; proxy bloqueado para evitar download incorreto.");
        server = new ServerSocket();
        server.setReuseAddress(true);
        server.bind(new InetSocketAddress(PORT));
        running = true;
        acceptThread = new Thread(this::acceptLoop, "tayson-proxy-accept");
        acceptThread.start();
        return endpoint(localIp);
    }

    private String endpoint(String ip) { return "http://" + ip + ":" + PORT + "/pkg/" + key + "/file.pkg"; }
    public synchronized void stop() {
        running = false;
        if (server != null) try { server.close(); } catch (IOException ignored) { }
        if (acceptThread != null) acceptThread.interrupt();
        clients.shutdownNow();
    }

    private void acceptLoop() {
        while (running) {
            try {
                final Socket s = server.accept();
                s.setSoTimeout(25000);
                clients.execute(() -> handle(s));
            } catch (IOException | RejectedExecutionException e) {
                if (!running) break;
            }
        }
    }

    private static boolean allowedClient(InetAddress ip) {
        return ip.isSiteLocalAddress() || ip.isLoopbackAddress() || ip.isLinkLocalAddress();
    }

    private void handle(Socket socket) {
        try (Socket s = socket; InputStream input = s.getInputStream(); OutputStream output = s.getOutputStream()) {
            if (!allowedClient(s.getInetAddress())) { reply(output, 403, "Forbidden"); return; }
            BufferedReader reader = new BufferedReader(new InputStreamReader(input, StandardCharsets.ISO_8859_1));
            String first = reader.readLine();
            if (first == null || first.length() > 4096) return;
            String[] parts = first.split(" ");
            if (parts.length != 3 || !(parts[0].equals("GET") || parts[0].equals("HEAD"))) { reply(output, 405, "Method Not Allowed"); return; }
            if (!parts[1].equals("/pkg/" + key + "/file.pkg")) { reply(output, 404, "Not Found"); return; }
            String range = null, ifRange = null;
            int bytes = 0;
            for (String header; (header = reader.readLine()) != null && !header.isEmpty();) {
                bytes += header.length(); if (bytes > 8192) { reply(output, 431, "Headers Too Large"); return; }
                int colon = header.indexOf(':'); if (colon < 1) continue;
                String k = header.substring(0, colon).trim().toLowerCase(java.util.Locale.ROOT);
                String v = header.substring(colon+1).trim();
                if (k.equals("range")) range = v;
                if (k.equals("if-range")) ifRange = v;
            }
            if (range != null && !range.matches("bytes=\\d+-\\d*")) { reply(output, 400, "Invalid Range"); return; }
            // A sonda apenas valida 0-3; no proxy cada pedido recebe resposta nativa da origem.
            HttpURLConnection c = UrlResolver.connect(upstream.url, parts[0], range, upstream.referer);
            try {
                if (ifRange != null) {
                    // If-Range não é encaminhado pelo resolvedor; interromper para não juntar versões incompatíveis.
                    reply(output, 412, "If-Range unavailable"); return;
                }
                int status = c.getResponseCode();
                if (status != 200 && status != 206 && status != 416) { reply(output, 502, "Upstream HTTP " + status); return; }
                String type = c.getHeaderField("Content-Type");
                if (type != null && type.toLowerCase(java.util.Locale.ROOT).contains("text/html")) {
                    reply(output, 502, "Upstream HTML"); return;
                }
                String contentRange = c.getHeaderField("Content-Range");
                if (status == 206 && (contentRange == null || !contentRange.startsWith("bytes "))) {
                    reply(output, 502, "Invalid Content-Range"); return;
                }
                if (range != null && status == 200) { reply(output, 502, "Upstream ignored Range"); return; }
                StringBuilder headers = new StringBuilder("HTTP/1.1 " + status + " " + (status == 206 ? "Partial Content" : status == 416 ? "Range Not Satisfiable" : "OK") + "\r\n");
                appendHeader(headers, "Content-Type", type == null ? "application/octet-stream" : type);
                for (String k : new String[]{"Content-Length", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified"}) {
                    String v = c.getHeaderField(k);
                    if (v != null) appendHeader(headers, k, v);
                }
                headers.append("Cache-Control: no-store\r\nConnection: close\r\n\r\n");
                output.write(headers.toString().getBytes(StandardCharsets.ISO_8859_1));
                if (parts[0].equals("HEAD") || status == 416) return;
                try (InputStream src = c.getInputStream()) {
                    byte[] buf = new byte[64 * 1024]; int n;
                    while (running && (n = src.read(buf)) != -1) output.write(buf, 0, n);
                    output.flush();
                }
            } finally { c.disconnect(); }
        } catch (IOException ignored) { /* Desconexão do console encerra a transferência. */ }
    }

    private static void appendHeader(StringBuilder s, String name, String value) {
        if (!value.contains("\r") && !value.contains("\n")) s.append(name).append(": ").append(value).append("\r\n");
    }
    private static void reply(OutputStream out, int status, String body) throws IOException {
        byte[] b = body.getBytes(StandardCharsets.UTF_8);
        out.write(("HTTP/1.1 " + status + " Error\r\nContent-Type: text/plain\r\nContent-Length: " + b.length + "\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.ISO_8859_1));
        out.write(b); out.flush();
    }
}
