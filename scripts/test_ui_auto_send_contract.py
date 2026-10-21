#!/usr/bin/env python3
"""Static safety regression checks. Not a substitute for MAUI or PS4 tests."""
from pathlib import Path
import unittest

SOURCE = (Path(__file__).resolve().parents[1] /
          "payload-generator/3.2.0/UPDATE/src/UiAutoPipeline.cs").read_text(encoding="utf-8")

class AutoSendContractTests(unittest.TestCase):
    def test_no_fake_bgft_confirmation(self):
        self.assertNotIn("Registro BGFT iniciado.", SOURCE)
        self.assertIn("Registro BGFT não confirmado", SOURCE)

    def test_bounded_write_and_flush(self):
        self.assertIn("new CancellationTokenSource(TimeSpan.FromSeconds(15))", SOURCE)
        self.assertIn("stream.WriteAsync(payload, 0, payload.Length, writeTimeout.Token)", SOURCE)
        self.assertIn("stream.FlushAsync(writeTimeout.Token)", SOURCE)

    def test_ambiguous_write_failure_is_explicit(self):
        self.assertIn("catch (OperationCanceledException) when (writeTimeout.IsCancellationRequested)", SOURCE)
        self.assertIn("catch (BinLoaderWriteInterruptedException)", SOURCE)
        self.assertIn("Resultado BGFT desconhecido", SOURCE)

    def test_no_retry_after_tcp_connection(self):
        write_section = SOURCE.split("using (client)", 1)[1].split("static bool IsRetryableConnectError", 1)[0]
        self.assertNotIn("for (int attempt", write_section)
        self.assertIn("catch (IOException ex)", write_section)

    def test_no_rearm_during_active_send(self):
        arm = SOURCE.split("static void Arm(Entry url)", 1)[1].split("static async Task ContinueAsync", 1)[0]
        self.assertIn("if (Busy || SendStarted) return;", arm)

    def test_failed_connection_socket_disposed(self):
        self.assertIn("catch (SocketException ex) when (IsRetryableConnectError(ex.SocketErrorCode))", SOURCE)
        self.assertIn("if (attempt < 4) await Task.Delay(350 * attempt);", SOURCE)
        self.assertIn("client.Dispose();", SOURCE)

if __name__ == "__main__":
    unittest.main()
