import com.akirabox.android.net.ConsoleClient;
import com.akirabox.android.net.PayloadFormatValidator;
import com.akirabox.android.net.TransferResult;
import com.akirabox.android.net.UrlUtils;

import com.sun.net.httpserver.HttpServer;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.InputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.Inet4Address;
import java.net.NetworkInterface;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.URL;
import java.nio.file.Files;
import java.util.Arrays;
import java.util.Enumeration;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

public final class CoreProtocolTest {
    public static void main(String[] args) throws Exception {
        testPrivateIps();
        testGitHubNormalization();
        testInvalidPayloads();
        testExactTcpTransfer();
        System.out.println("Akirabox core protocol tests: OK");
    }

    private static void testPrivateIps() {
        check(UrlUtils.isPrivateIpv4("192.168.1.108"), "192.168 deve ser privado");
        check(UrlUtils.isPrivateIpv4("172.16.0.2"), "172.16 deve ser privado");
        check(UrlUtils.isPrivateIpv4("10.0.0.3"), "10/8 deve ser privado");
        check(!UrlUtils.isPrivateIpv4("8.8.8.8"), "IP público deve ser rejeitado");
        check(!UrlUtils.isPrivateIpv4("192.168.999.1"), "Octeto inválido deve ser rejeitado");
    }

    private static void testGitHubNormalization() {
        String value = UrlUtils.normalizeGitHubBlob(
                "https://github.com/example/repo/blob/main/payloads/goldhen.bin"
        );
        check(
                value.equals("https://raw.githubusercontent.com/example/repo/main/payloads/goldhen.bin"),
                "Link blob do GitHub não foi convertido"
        );
    }

    private static void testInvalidPayloads() throws Exception {
        byte[] html = ("<!doctype html><html>download</html>" + " ".repeat(300)).getBytes();
        expectFailure(() -> PayloadFormatValidator.validate(
                html, html.length, new URL("https://example.com/payload.bin")
        ), "HTML deveria ser rejeitado");

        byte[] badElf = new byte[512];
        Arrays.fill(badElf, (byte) 0x41);
        expectFailure(() -> PayloadFormatValidator.validate(
                badElf, badElf.length, new URL("https://example.com/payload.elf")
        ), "ELF inválido deveria ser rejeitado");
    }

    private static void testExactTcpTransfer() throws Exception {
        byte[] expected = new byte[32 * 1024 + 17];
        expected[0] = 0x7f;
        expected[1] = 'E';
        expected[2] = 'L';
        expected[3] = 'F';
        for (int i = 4; i < expected.length; i++) expected[i] = (byte) (i & 0xff);

        HttpServer http = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        http.createContext("/payload.elf", exchange -> {
            exchange.getResponseHeaders().add("Content-Type", "application/octet-stream");
            exchange.sendResponseHeaders(200, expected.length);
            exchange.getResponseBody().write(expected);
            exchange.close();
        });
        http.start();

        String localIp = findPrivateIpv4();
        check(UrlUtils.isPrivateIpv4(localIp), "Ambiente de teste precisa de IPv4 privado: " + localIp);
        ServerSocket receiver = new ServerSocket(0, 1, InetAddress.getByName(localIp));
        CompletableFuture<byte[]> receivedFuture = CompletableFuture.supplyAsync(() -> {
            try (Socket socket = receiver.accept();
                 InputStream input = socket.getInputStream();
                 ByteArrayOutputStream received = new ByteArrayOutputStream()) {
                byte[] buffer = new byte[4096];
                int read;
                while ((read = input.read(buffer)) != -1) received.write(buffer, 0, read);
                return received.toByteArray();
            } catch (Exception error) {
                throw new RuntimeException(error);
            }
        });

        File cache = Files.createTempDirectory("akirabox-core-test").toFile();
        URL payloadUrl = new URL("http://127.0.0.1:" + http.getAddress().getPort() + "/payload.elf");
        TransferResult result;
        try {
            result = ConsoleClient.sendPayload(
                    cache,
                    localIp,
                    receiver.getLocalPort(),
                    payloadUrl.toString(),
                    null
            );
        } finally {
            http.stop(0);
            receiver.close();
        }

        byte[] received = receivedFuture.get(3, TimeUnit.SECONDS);
        check(Arrays.equals(expected, received), "Bytes recebidos diferem do payload original");
        check(result.getBytesSent() == expected.length, "Tamanho reportado incorreto");
        check(result.getSha256().length() == 64, "SHA-256 inválido");
    }

    private static String findPrivateIpv4() throws Exception {
        Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
        while (interfaces.hasMoreElements()) {
            Enumeration<InetAddress> addresses = interfaces.nextElement().getInetAddresses();
            while (addresses.hasMoreElements()) {
                InetAddress address = addresses.nextElement();
                String candidate = address.getHostAddress();
                if (address instanceof Inet4Address && UrlUtils.isPrivateIpv4(candidate)) {
                    return candidate;
                }
            }
        }
        throw new IllegalStateException("Nenhum IPv4 privado disponível para o teste TCP.");
    }

    private static void expectFailure(ThrowingRunnable action, String message) throws Exception {
        try {
            action.run();
        } catch (Exception expected) {
            return;
        }
        throw new AssertionError(message);
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

    private interface ThrowingRunnable {
        void run() throws Exception;
    }
}
