package com.tayson.proxylab;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.regex.*;

/** Resolução somente de links públicos e sondagem limitada; nenhum PKG inteiro é baixado. */
public final class UrlResolver {
    private UrlResolver() { }
    static final int MAX_REDIRECTS = 5;
    static final String UA = "Mozilla/5.0 (Linux; Android) TaySonProxyLab/0.1";
    private static final Pattern MF1 = Pattern.compile("id\\s*=\\s*['\"]downloadButton['\"][^>]*href\\s*=\\s*['\"]([^'\"]+)", Pattern.CASE_INSENSITIVE);
    private static final Pattern MF2 = Pattern.compile("href\\s*=\\s*['\"]([^'\"]+)['\"][^>]*id\\s*=\\s*['\"]downloadButton['\"]", Pattern.CASE_INSENSITIVE);

    public static final class Result {
        public final String url;
        public final String referer;
        public final int status;
        public final long totalBytes;
        public final boolean rangeSupported;
        public final String contentType;
        Result(String url, String referer, int status, long totalBytes, boolean rangeSupported, String contentType) {
            this.url = url; this.referer = referer; this.status = status; this.totalBytes = totalBytes;
            this.rangeSupported = rangeSupported; this.contentType = contentType;
        }
    }

    public static Result resolve(String input) throws IOException {
        if (input == null || input.trim().isEmpty()) throw new IOException("Cole um link primeiro.");
        String source = input.trim();
        validateUrl(source);
        String host = URI.create(source).getHost();
        boolean mfShare = host != null && (host.equalsIgnoreCase("mediafire.com") || host.equalsIgnoreCase("www.mediafire.com"))
                && !URI.create(source).getPath().toLowerCase().endsWith(".pkg");
        String referer = null;
        if (mfShare) {
            HttpURLConnection page = connect(source, "GET", null, null);
            try {
                if (page.getResponseCode() != 200) throw new IOException("Página MediaFire HTTP " + page.getResponseCode());
                String type = page.getContentType();
                if (type != null && !type.toLowerCase().contains("html")) throw new IOException("A página retornou conteúdo inesperado.");
                String html = readLimited(page.getInputStream(), 256 * 1024);
                Matcher a = MF1.matcher(html);
                Matcher b = MF2.matcher(html);
                String direct = a.find() ? a.group(1) : (b.find() ? b.group(1) : null);
                if (direct == null) throw new IOException("MediaFire: botão de download não identificado nesta página.");
                direct = direct.replace("&amp;", "&").replace("&#38;", "&").replace("&quot;", "\"");
                validateUrl(direct);
                source = direct;
                referer = "https://www.mediafire.com/";
            } finally { page.disconnect(); }
        }
        // A prova é GET Range 0-3 (não HEAD, que muitos provedores rejeitam).
        HttpURLConnection test = connect(source, "GET", "bytes=0-3", referer);
        try {
            int code = test.getResponseCode();
            if (code != 200 && code != 206) throw new IOException("Provedor HTTP " + code);
            String type = test.getContentType();
            if (type != null && type.toLowerCase().contains("text/html")) throw new IOException("O provedor devolveu HTML, não os bytes do PKG.");
            byte[] magic = new byte[4];
            try (InputStream in = test.getInputStream()) {
                int i = 0, n;
                while (i < 4 && (n = in.read(magic, i, 4 - i)) > 0) i += n;
                if (i < 4 || (magic[0] & 255) != 0x7f || magic[1] != 'C' || magic[2] != 'N' || magic[3] != 'T')
                    throw new IOException("Assinatura de PKG PS4 não encontrada no início do arquivo.");
            }
            String range = test.getHeaderField("Content-Range");
            boolean rangeOk = code == 206 && range != null && range.startsWith("bytes 0-3/");
            long total = -1;
            if (rangeOk) {
                try { total = Long.parseLong(range.substring(range.indexOf('/') + 1)); } catch (NumberFormatException ignored) { }
            } else if (code == 200) total = test.getContentLengthLong();
            return new Result(test.getURL().toString(), referer, code, total, rangeOk, type == null ? "desconhecido" : type);
        } finally { test.disconnect(); }
    }

    /** Valida o destino de cada salto, inclusive para evitar exposição de serviços locais. */
    static void validateUrl(String raw) throws IOException {
        try {
            URI u = new URI(raw);
            String scheme = u.getScheme(), host = u.getHost();
            if (scheme == null || !(scheme.equalsIgnoreCase("http") || scheme.equalsIgnoreCase("https")) || host == null || u.getUserInfo() != null)
                throw new IOException("Use um endereço HTTP(S) público válido.");
            int port = u.getPort();
            if (port != -1 && port != 80 && port != 443) throw new IOException("Porta remota não autorizada.");
            String h = host.toLowerCase(java.util.Locale.ROOT);
            if (h.equals("localhost") || h.endsWith(".local") || h.endsWith(".localhost")) throw new IOException("Destino local não autorizado.");
            for (InetAddress ip : InetAddress.getAllByName(host)) {
                if (ip.isAnyLocalAddress() || ip.isLoopbackAddress() || ip.isLinkLocalAddress() || ip.isSiteLocalAddress() || ip.isMulticastAddress())
                    throw new IOException("Destino com IP privado/local não autorizado.");
                if (ip instanceof Inet6Address) {
                    byte[] b = ip.getAddress();
                    if ((b[0] & 0xfe) == 0xfc) throw new IOException("Destino IPv6 ULA não autorizado.");
                }
            }
        } catch (URISyntaxException | IllegalArgumentException e) { throw new IOException("URL inválida.", e); }
    }

    static HttpURLConnection connect(String raw, String method, String range, String referer) throws IOException {
        String url = raw;
        for (int redirects = 0; redirects <= MAX_REDIRECTS; redirects++) {
            validateUrl(url);
            HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
            c.setInstanceFollowRedirects(false);
            c.setConnectTimeout(12000);
            c.setReadTimeout(20000);
            c.setRequestMethod(method);
            c.setRequestProperty("User-Agent", UA);
            c.setRequestProperty("Accept-Encoding", "identity");
            if (range != null) c.setRequestProperty("Range", range);
            if (referer != null && new URL(url).getHost().toLowerCase().endsWith("mediafire.com")) c.setRequestProperty("Referer", referer);
            int code;
            try { code = c.getResponseCode(); }
            catch (IOException e) { c.disconnect(); throw e; }
            if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308) {
                String dest = c.getHeaderField("Location");
                if (dest == null) { c.disconnect(); throw new IOException("Redirecionamento sem Location."); }
                url = new URL(new URL(url), dest).toString();
                c.disconnect();
                if (redirects == MAX_REDIRECTS) throw new IOException("Redirecionamentos em excesso.");
                continue;
            }
            return c;
        }
        throw new IOException("Redirecionamentos em excesso.");
    }

    private static String readLimited(InputStream in, int limit) throws IOException {
        try (InputStream src = in; ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            byte[] b = new byte[4096]; int n;
            while (out.size() < limit && (n = src.read(b, 0, Math.min(b.length, limit - out.size()))) > 0) out.write(b, 0, n);
            return out.toString(StandardCharsets.UTF_8.name());
        }
    }
}
