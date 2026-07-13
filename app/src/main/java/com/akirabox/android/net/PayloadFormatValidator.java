package com.akirabox.android.net;

import java.io.IOException;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.Locale;

public final class PayloadFormatValidator {
    private PayloadFormatValidator() {}

    public static void validate(byte[] head, long totalBytes, URL finalUrl) throws IOException {
        if (totalBytes < 256) {
            throw new IOException("O arquivo é pequeno demais para ser um payload válido.");
        }
        byte[] safeHead = head == null ? new byte[0] : head;
        String prefix = new String(safeHead, StandardCharsets.ISO_8859_1)
                .replace("\u0000", "")
                .trim()
                .toLowerCase(Locale.ROOT);
        if (prefix.startsWith("<!doctype html")
                || prefix.startsWith("<html")
                || prefix.startsWith("<head")
                || prefix.startsWith("<body")) {
            throw new IOException("O link abriu uma página HTML. Use o link de download direto.");
        }
        if (prefix.startsWith("version https://git-lfs.github.com/spec/v1")) {
            throw new IOException("O link retornou um ponteiro Git LFS, não o payload real.");
        }
        if (startsWith(safeHead, new byte[] {'P', 'K', 3, 4})
                || startsWith(safeHead, new byte[] {'R', 'a', 'r', '!', 0x1a, 7})
                || startsWith(safeHead, new byte[] {'7', 'z', (byte) 0xbc, (byte) 0xaf, 0x27, 0x1c})) {
            throw new IOException("O link retornou um arquivo compactado. Extraia o BIN/ELF primeiro.");
        }
        if (startsWith(safeHead, new byte[] {'M', 'Z'})) {
            throw new IOException("O link retornou um executável de Windows, não um payload.");
        }
        String path = finalUrl.getPath().toLowerCase(Locale.ROOT);
        if (path.endsWith(".elf")
                && !startsWith(safeHead, new byte[] {0x7f, 'E', 'L', 'F'})) {
            throw new IOException("O arquivo .elf não possui um cabeçalho ELF válido.");
        }
    }

    private static boolean startsWith(byte[] value, byte[] expected) {
        return value.length >= expected.length
                && Arrays.equals(Arrays.copyOf(value, expected.length), expected);
    }
}
