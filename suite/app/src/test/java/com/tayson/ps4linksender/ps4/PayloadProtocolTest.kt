package com.tayson.ps4linksender.ps4

import com.tayson.ps4linksender.model.PkgInfo
import com.tayson.ps4linksender.model.RequestContext
import java.io.ByteArrayInputStream
import java.io.DataInputStream
import java.net.ServerSocket
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import org.junit.Assert.*
import org.junit.Test

class PayloadProtocolTest {
    private fun pkg(ctx: RequestContext? = null) = PkgInfo(
        "https://example.test/page", "https://example.test/a.pkg?token=fixture", "teste-ç.pkg",
        10_000_000_000L, "UP0000-CUSA00001_00-TEST", "CUSA00001", "PS4GD", 26, 0,
        true, 206, "application/octet-stream", requestContext = ctx
    )

    @Test fun preservesReceiverWireLayoutAndLargeSize() {
        val ctx = RequestContext("Agent", "https://example.test/", "session=fixture", mapOf("Authorization" to "Bearer fixture"))
        val p = pkg(ctx)
        val input = DataInputStream(ByteArrayInputStream(JobPacketCodec.encode(p)))
        assertEquals(0x54594A31, input.readInt()); assertEquals(1, input.readInt())
        assertEquals(10_000_000_000L, input.readLong())
        val fields = List(8) {
            val n = input.readInt(); val bytes = ByteArray(n); input.readFully(bytes); String(bytes, Charsets.UTF_8)
        }
        assertEquals(listOf(p.finalUrl,"Agent",ctx.referer,ctx.cookie,"Authorization: Bearer fixture\n",p.contentId,p.fileName,p.packageType),fields)
        assertEquals(-1,input.read())
    }

    @Test fun filtersInjectedAndTransportHeaders() {
        val ctx = RequestContext(extraHeaders = mapOf("X-Good" to "yes", "Range" to "bytes=9-99", "X-Bad" to "a\r\nInjected: yes", "Host" to "evil.test"))
        val bytes = String(JobPacketCodec.encode(pkg(ctx)), Charsets.ISO_8859_1)
        assertTrue(bytes.contains("X-Good: yes")); assertFalse(bytes.contains("Injected")); assertFalse(bytes.contains("evil.test")); assertFalse(bytes.contains("bytes=9-99"))
    }

    @Test fun rejectsOversizedUrlAndInvalidSize() {
        assertThrows(IllegalArgumentException::class.java) { JobPacketCodec.encode(pkg().copy(finalUrl="https://example.test/"+"a".repeat(8200))) }
        assertThrows(IllegalArgumentException::class.java) { JobPacketCodec.encode(pkg().copy(size=0)) }
    }

    @Test fun refusesReceiverAsPayload() {
        ServerSocket(0).use { server ->
            val exec = Executors.newSingleThreadExecutor()
            val future = exec.submit {
                server.accept().use { s -> s.getInputStream().bufferedReader().readLine(); s.getOutputStream().write("TAYSON/1 OK receiver=1.4.2\n".toByteArray()) }
            }
            try { assertThrows(IllegalArgumentException::class.java) { PayloadProbeClient.ping("127.0.0.1",server.localPort) };future.get(3,TimeUnit.SECONDS) }
            finally { exec.shutdownNow() }
        }
    }

    @Test fun readsStagesAndChecksProbeResult() {
        ServerSocket(0).use { server ->
            val exec = Executors.newSingleThreadExecutor()
            val future = exec.submit {
                server.accept().use { s -> s.getInputStream().bufferedReader().readLine();s.getOutputStream().write("TAYSON/1 OK payload=1.5.0 caps=probe pid=42 busy=0 stage=ready\n".toByteArray()) }
                server.accept().use { s ->
                    val input = s.getInputStream()
                    val command = buildString { while (true) { val b=input.read();check(b>=0);if(b==10)break;append(b.toChar()) } }
                    val size=command.substringAfterLast(' ').toInt()
                    val bytes=ByteArray(size);DataInputStream(input).readFully(bytes)
                    assertArrayEquals(JobPacketCodec.encode(pkg()),bytes)
                    s.getOutputStream().write(("TAYSON/1 STEP stage=ssl2-init\n"+"TAYSON/1 STEP stage=read-16-bytes\n"+"TAYSON/1 PROBE payload=1.5.0 http=206 bytes=16 pkg=1 scheme=https\n").toByteArray())
                }
            }
            try {
                val stages=mutableListOf<String>()
                val result=PayloadProbeClient.probe("127.0.0.1",pkg(),server.localPort) { stages.add(it) }
                assertEquals(listOf("ssl2-init","read-16-bytes"),stages)
                assertTrue(result.pkg);assertEquals(206,result.http);assertEquals(16,result.bytes)
                future.get(3,TimeUnit.SECONDS)
            } finally {exec.shutdownNow()}
        }
    }

    @Test fun validatesLocalIpWithoutDns() {
        PayloadProbeClient.requireHost("192.168.0.10");PayloadProbeClient.requireHost("172.31.0.2")
        for (host in listOf("evil.test","https://192.168.0.1","192.168.0.1:9090","8.8.8.8","192.168.0.999","172.32.0.1"))
            assertThrows(IllegalArgumentException::class.java) {PayloadProbeClient.requireHost(host)}
    }
}
