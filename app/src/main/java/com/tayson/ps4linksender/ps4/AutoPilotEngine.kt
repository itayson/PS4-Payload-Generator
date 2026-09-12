package com.tayson.ps4linksender.ps4

import android.content.Context
import com.tayson.ps4linksender.model.PkgInfo

/**
 * Zero-touch transport chooser used by Link Sender 1.3.0.
 *
 * Preferred path: Payload 0.2.x DIRECT -> the PS4 downloads HTTP(S) itself,
 * resumes current.pkg locally and submits it to AppInstUtil. Android is only
 * needed for the initial handoff.
 *
 * Fallback path: Tayson Receiver/BGFT. This keeps the proven receiver backend
 * available when BinLoader or the direct payload path is unavailable.
 */
object AutoPilotEngine {
    enum class Backend { PAYLOAD_DIRECT, PAYLOAD_ALREADY_RUNNING, BGFT_RECEIVER }

    data class Result(
        val host: String,
        val backend: Backend,
        val message: String,
        val payloadVersion: String? = null,
        val receiverJobId: Int? = null,
        val bgftTaskId: Int? = null
    )

    fun run(
        context: Context,
        hostHint: String,
        pkg: PkgInfo,
        progress: (String) -> Unit = {}
    ): Result {
        val host = resolveHost(context, hostHint, progress)
        progress("PS4 localizado em $host. Preparando backend automático…")

        var probe = runCatching { PayloadClient.probe(host) }.getOrNull()
        var status = probe?.let { runCatching { PayloadClient.status(host) }.getOrNull() }

        if (status != null && status.state in setOf("downloading", "installing")) {
            if (samePackage(status, pkg)) {
                return Result(
                    host = host,
                    backend = Backend.PAYLOAD_ALREADY_RUNNING,
                    message = "O mesmo PKG já está em andamento no Payload; nenhum trabalho duplicado foi criado.",
                    payloadVersion = probe?.version
                )
            }
            throw IllegalStateException("O Payload está ocupado com outro PKG. Aguarde a conclusão ou interrompa o trabalho atual.")
        }

        if (probe == null || !PayloadClient.supportsDirect(probe.version)) {
            progress("Payload direto não está ativo. Tentando carregar automaticamente pelo BinLoader :${PayloadLoaderClient.PORT}…")
            if (probe != null) {
                // Only replace an idle/terminal old payload. EXIT preserves current.pkg.
                runCatching { PayloadClient.exit(host) }
                Thread.sleep(350)
            }
            probe = runCatching {
                PayloadLoaderClient.sendEmbedded(context, host)
                waitForPayload(host)
            }.getOrNull()
            status = probe?.let { runCatching { PayloadClient.status(host) }.getOrNull() }
        }

        if (probe != null && PayloadClient.supportsDirect(probe.version)) {
            progress("Payload ${probe.version} ativo. Selecionando iniciar/retomar automaticamente…")
            val resume = status != null &&
                status.state in setOf("cached", "error", "cancelled", "idle") &&
                status.bytes > 0L &&
                samePackage(status, pkg, allowUnknownIdentity = true)

            val accepted = try {
                PayloadClient.direct(host, pkg, resume = resume)
            } catch (first: Exception) {
                // A legacy cache with stale metadata may reject resume. Never erase it
                // implicitly: fall through to the proven BGFT backend instead.
                progress("Modo direto não pôde iniciar (${first.message}). Tentando BGFT automaticamente…")
                null
            }

            if (accepted != null) {
                progress(if (resume) "Cache local retomado; PS4 baixará apenas o restante." else "Modo direto iniciado; PS4 baixará o PKG sem relay do celular.")

                // Catch immediate SceHttp/runtime failures. Network interruptions after
                // data starts are handled inside Payload 0.2.x with local Range resume.
                repeat(4) {
                    Thread.sleep(700)
                    val st = runCatching { PayloadClient.status(host) }.getOrNull() ?: return@repeat
                    if (st.state == "error" && st.bytes == 0L) {
                        progress("Payload direto falhou antes de transferir dados. Ativando fallback BGFT…")
                        return fallbackReceiver(host, pkg, progress)
                    }
                    if (st.state in setOf("downloading", "installing", "submitted")) {
                        return Result(
                            host = host,
                            backend = Backend.PAYLOAD_DIRECT,
                            message = if (resume)
                                "Autopilot retomou o cache existente. O PS4 continua sozinho até instalar."
                            else
                                "Autopilot iniciou download direto no PS4. O celular pode sair do app após o handoff.",
                            payloadVersion = probe.version
                        )
                    }
                }
                return Result(
                    host = host,
                    backend = Backend.PAYLOAD_DIRECT,
                    message = "Trabalho direto aceito pelo Payload. O PS4 continuará o processo autonomamente.",
                    payloadVersion = probe.version
                )
            }
        }

        progress("BinLoader/Payload direto indisponível. Usando Tayson Receiver/BGFT automaticamente…")
        return fallbackReceiver(host, pkg, progress)
    }

    private fun fallbackReceiver(host: String, pkg: PkgInfo, progress: (String) -> Unit): Result {
        val receiver = ReceiverClient.probe(host)
        progress("Receiver ${receiver.version} ativo. Registrando o PKG no BGFT…")
        val sent = ReceiverClient.sendJob(host, pkg)
        return Result(
            host = host,
            backend = Backend.BGFT_RECEIVER,
            message = "Autopilot usou o fallback BGFT. O download foi registrado no PS4.",
            receiverJobId = sent.jobId,
            bgftTaskId = sent.taskId
        )
    }

    private fun resolveHost(context: Context, hostHint: String, progress: (String) -> Unit): String {
        val hint = hostHint.trim()
        if (hint.isNotBlank()) return hint
        progress("IP do PS4 não definido. Procurando automaticamente na rede local…")
        val scan = ReceiverDiscovery.scan(context)
        val match = scan.matches.firstOrNull()
            ?: throw IllegalStateException("Nenhum Tayson Receiver foi encontrado. Informe o IP do PS4 uma vez; ele ficará salvo para os próximos usos.")
        return match.ip
    }

    private fun waitForPayload(host: String): PayloadClient.ProbeResult {
        var last: Throwable? = null
        repeat(16) {
            Thread.sleep(500)
            try {
                return PayloadClient.probe(host)
            } catch (e: Throwable) {
                last = e
            }
        }
        throw IllegalStateException("BinLoader recebeu o payload, mas :${PayloadClient.PORT} não respondeu: ${last?.message.orEmpty()}")
    }

    private fun samePackage(
        status: PayloadClient.StatusResult,
        pkg: PkgInfo,
        allowUnknownIdentity: Boolean = false
    ): Boolean {
        if (status.total > 0L && status.total != pkg.size) return false
        val expected = pkg.titleId
        val remote = status.titleId
        if (!expected.isNullOrBlank() && !remote.isNullOrBlank() && !expected.equals(remote, true)) return false
        if (!allowUnknownIdentity && !expected.isNullOrBlank() && remote.isNullOrBlank()) return false
        return true
    }
}
