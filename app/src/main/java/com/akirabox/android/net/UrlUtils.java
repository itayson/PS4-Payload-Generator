package com.akirabox.android.net;

import java.net.MalformedURLException;
import java.net.URL;
import java.nio.charset.StandardCharsets;

public final class UrlUtils {
    private UrlUtils() {}

    public static URL requireHttpUrl(String raw) throws MalformedURLException {
        if (raw == null || raw.trim().isEmpty() || raw.length() > 4096) {
            throw new MalformedURLException("Link ausente ou muito longo.");
        }
        URL url = new URL(normalizeGitHubBlob(raw.trim()));
        if (!"http".equalsIgnoreCase(url.getProtocol())
                && !"https".equalsIgnoreCase(url.getProtocol())) {
            throw new MalformedURLException("O link precisa começar com http:// ou https://.");
        }
        if (url.getUserInfo() != null) {
            throw new MalformedURLException("Links com usuário e senha não são aceitos.");
        }
        return url;
    }

    public static String normalizeGitHubBlob(String raw) {
        try {
            URL url = new URL(raw);
            if (!"github.com".equalsIgnoreCase(url.getHost())) {
                return raw;
            }
            String[] pieces = url.getPath().split("/");
            // /owner/repository/blob/branch/path/to/file
            if (pieces.length < 6 || !"blob".equals(pieces[3])) {
                return raw;
            }
            StringBuilder result = new StringBuilder("https://raw.githubusercontent.com/")
                    .append(encodePathSegment(pieces[1])).append('/')
                    .append(encodePathSegment(pieces[2])).append('/')
                    .append(encodePathSegment(pieces[4]));
            for (int i = 5; i < pieces.length; i++) {
                result.append('/').append(encodePathSegment(pieces[i]));
            }
            return result.toString();
        } catch (MalformedURLException ignored) {
            return raw;
        }
    }

    private static String encodePathSegment(String value) {
        StringBuilder encoded = new StringBuilder();
        for (byte current : value.getBytes(StandardCharsets.UTF_8)) {
            int unsigned = current & 0xff;
            if ((unsigned >= 'a' && unsigned <= 'z')
                    || (unsigned >= 'A' && unsigned <= 'Z')
                    || (unsigned >= '0' && unsigned <= '9')
                    || unsigned == '-' || unsigned == '_' || unsigned == '.' || unsigned == '~') {
                encoded.append((char) unsigned);
            } else {
                encoded.append('%');
                encoded.append(Character.toUpperCase(Character.forDigit((unsigned >> 4) & 0xf, 16)));
                encoded.append(Character.toUpperCase(Character.forDigit(unsigned & 0xf, 16)));
            }
        }
        return encoded.toString();
    }

    public static boolean isPrivateIpv4(String value) {
        if (value == null) return false;
        String[] parts = value.trim().split("\\.", -1);
        if (parts.length != 4) return false;
        int[] octets = new int[4];
        try {
            for (int i = 0; i < 4; i++) {
                if (parts[i].isEmpty() || parts[i].length() > 3) return false;
                octets[i] = Integer.parseInt(parts[i]);
                if (octets[i] < 0 || octets[i] > 255) return false;
            }
        } catch (NumberFormatException error) {
            return false;
        }
        return octets[0] == 10
                || (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
                || (octets[0] == 192 && octets[1] == 168);
    }
}
