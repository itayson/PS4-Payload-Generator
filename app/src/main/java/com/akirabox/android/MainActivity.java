package com.akirabox.android;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.text.SpannableString;
import android.text.SpannableStringBuilder;
import android.text.Spanned;
import android.text.style.ForegroundColorSpan;
import android.view.View;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import androidx.annotation.ColorInt;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.content.ContextCompat;

import com.akirabox.android.net.ConsoleClient;
import com.akirabox.android.net.TransferResult;
import com.akirabox.android.net.UrlUtils;
import com.google.android.material.button.MaterialButton;
import com.google.android.material.button.MaterialButtonToggleGroup;
import com.google.android.material.checkbox.MaterialCheckBox;
import com.google.android.material.dialog.MaterialAlertDialogBuilder;
import com.google.android.material.textfield.TextInputEditText;
import com.google.android.material.textfield.TextInputLayout;

import java.net.ConnectException;
import java.net.SocketTimeoutException;
import java.net.UnknownHostException;
import java.net.URL;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;

public final class MainActivity extends AppCompatActivity {
    private enum Mode { PACKAGE, PAYLOAD }
    private enum ConsoleType { PS4, PS5 }

    private static final String PREFS = "akirabox_settings";

    private final ExecutorService networkExecutor = Executors.newSingleThreadExecutor();
    private final SimpleDateFormat clock = new SimpleDateFormat("HH:mm:ss", Locale.getDefault());
    private final SpannableStringBuilder logBuffer = new SpannableStringBuilder();

    private Mode mode = Mode.PACKAGE;
    private ConsoleType consoleType = ConsoleType.PS4;

    private MaterialButtonToggleGroup modeGroup;
    private MaterialButtonToggleGroup consoleGroup;
    private TextInputLayout ipLayout;
    private TextInputLayout urlLayout;
    private TextInputLayout portLayout;
    private TextInputEditText consoleIp;
    private TextInputEditText downloadUrl;
    private TextInputEditText port;
    private MaterialCheckBox trustedPayload;
    private MaterialButton testButton;
    private MaterialButton sendButton;
    private MaterialButton pasteUrlButton;
    private MaterialButton helpButton;
    private ProgressBar progress;
    private TextView progressLabel;
    private TextView networkStatus;
    private TextView modeHint;
    private TextView logView;
    private TextView stepConsole;
    private TextView stepLink;
    private TextView stepValidate;
    private TextView stepSend;
    private SharedPreferences preferences;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);
        preferences = getSharedPreferences(PREFS, MODE_PRIVATE);
        bindViews();
        restoreSettings();
        configureControls();
        updateModeUi();
        appendLog("INF", "Aplicativo pronto. Nenhum Bridge ou Termux é necessário.", color(R.color.akira_accent));
        handleIncomingShare(getIntent());
    }

    private void bindViews() {
        modeGroup = findViewById(R.id.modeGroup);
        consoleGroup = findViewById(R.id.consoleGroup);
        ipLayout = findViewById(R.id.ipLayout);
        urlLayout = findViewById(R.id.urlLayout);
        portLayout = findViewById(R.id.portLayout);
        consoleIp = findViewById(R.id.consoleIp);
        downloadUrl = findViewById(R.id.downloadUrl);
        port = findViewById(R.id.port);
        trustedPayload = findViewById(R.id.trustedPayload);
        testButton = findViewById(R.id.testButton);
        sendButton = findViewById(R.id.sendButton);
        pasteUrlButton = findViewById(R.id.pasteUrlButton);
        helpButton = findViewById(R.id.helpButton);
        progress = findViewById(R.id.progress);
        progressLabel = findViewById(R.id.progressLabel);
        networkStatus = findViewById(R.id.networkStatus);
        modeHint = findViewById(R.id.modeHint);
        logView = findViewById(R.id.logView);
        stepConsole = findViewById(R.id.stepConsole);
        stepLink = findViewById(R.id.stepLink);
        stepValidate = findViewById(R.id.stepValidate);
        stepSend = findViewById(R.id.stepSend);
        findViewById(R.id.clearLog).setOnClickListener(view -> clearLog());
        findViewById(R.id.copyLog).setOnClickListener(view -> copyLog());
    }

    private void configureControls() {
        modeGroup.addOnButtonCheckedListener((group, checkedId, isChecked) -> {
            if (!isChecked) return;
            Mode next = checkedId == R.id.modePayload ? Mode.PAYLOAD : Mode.PACKAGE;
            if (next == mode) return;
            saveSettings();
            mode = next;
            loadModeFields();
            updateModeUi();
            saveSettings();
        });

        consoleGroup.addOnButtonCheckedListener((group, checkedId, isChecked) -> {
            if (!isChecked) return;
            ConsoleType next = checkedId == R.id.consolePs5 ? ConsoleType.PS5 : ConsoleType.PS4;
            if (next == consoleType) return;
            saveSettings();
            consoleType = next;
            loadModeFields();
            updateModeUi();
            saveSettings();
        });

        testButton.setOnClickListener(view -> testConnection());
        sendButton.setOnClickListener(view -> sendContent());
        pasteUrlButton.setOnClickListener(view -> pasteUrl());
        helpButton.setOnClickListener(view -> showHelp());
    }

    private void restoreSettings() {
        String savedIp = preferences.getString("console_ip", "");
        String savedMode = preferences.getString("mode", Mode.PACKAGE.name());
        String savedConsole = preferences.getString("console", ConsoleType.PS4.name());
        try {
            mode = Mode.valueOf(savedMode);
            consoleType = ConsoleType.valueOf(savedConsole);
        } catch (IllegalArgumentException ignored) {
            mode = Mode.PACKAGE;
            consoleType = ConsoleType.PS4;
        }
        consoleIp.setText(savedIp);
        modeGroup.check(mode == Mode.PAYLOAD ? R.id.modePayload : R.id.modePkg);
        consoleGroup.check(consoleType == ConsoleType.PS5 ? R.id.consolePs5 : R.id.consolePs4);
        loadModeFields();
    }

    private void saveSettings() {
        preferences.edit()
                .putString("console_ip", text(consoleIp))
                .putString("mode", mode.name())
                .putString("console", consoleType.name())
                .putString(urlPreferenceKey(), text(downloadUrl))
                .putString(portPreferenceKey(), text(port))
                .apply();
    }

    private void loadModeFields() {
        downloadUrl.setText(preferences.getString(urlPreferenceKey(), ""));
        port.setText(preferences.getString(portPreferenceKey(), defaultPort()));
    }

    private String urlPreferenceKey() {
        return mode == Mode.PAYLOAD ? "url_payload" : "url_package";
    }

    private String portPreferenceKey() {
        return "port_" + mode.name() + '_' + consoleType.name();
    }

    private String defaultPort() {
        if (mode == Mode.PACKAGE) return "12800";
        return consoleType == ConsoleType.PS4 ? "9090" : "9021";
    }

    private void updateModeUi() {
        boolean payload = mode == Mode.PAYLOAD;
        trustedPayload.setVisibility(payload ? View.VISIBLE : View.GONE);
        urlLayout.setHint(payload ? "Link direto do payload BIN/ELF" : "Link direto do pacote PKG");
        sendButton.setText(payload ? "Enviar" : "Instalar");
        testButton.setText(payload ? "Validar payload" : "Testar conexão");
        modeHint.setText(payload
                ? (consoleType == ConsoleType.PS4
                    ? "PS4 • Ative o GoldHEN BinLoader • porta padrão 9090"
                    : "PS5 • Ative o etaHEN ELF Loader • porta padrão 9021")
                : "Abra o Remote Package Installer/DPI no console • porta padrão 12800");
        trustedPayload.setChecked(false);
        networkStatus.setText("● Pronto");
        networkStatus.setTextColor(color(R.color.akira_green));
        showStep(0);
        resetErrors();
    }

    private void testConnection() {
        Input input = validateInput(mode == Mode.PAYLOAD);
        if (input == null) return;
        saveSettings();

        if (mode == Mode.PAYLOAD) {
            validatePayload(input);
            return;
        }

        setBusy(true, "Procurando Remote Package Installer…");
        appendLog("INF", "Procurando instalador em " + input.ip + ':' + input.port + '.', color(R.color.akira_accent));
        networkExecutor.execute(() -> {
            try {
                ConsoleClient.testPackageInstaller(input.ip, input.port);
                runOnUiThread(() -> {
                    appendLog("OK", "Instalador localizado. Pronto para receber PKG.", color(R.color.akira_green));
                    networkStatus.setText("● Conectado");
                    networkStatus.setTextColor(color(R.color.akira_green));
                    showStep(1);
                    setBusy(false, null);
                });
            } catch (Exception error) {
                runOnUiThread(() -> finishWithError("Não foi possível localizar o instalador", error));
            }
        });
    }

    private void validatePayload(Input input) {
        showStep(2);
        setBusy(true, "Baixando o payload para validar…");
        progress.setIndeterminate(true);
        appendLog("INF", "Validando link e formato sem abrir a porta do BinLoader.", color(R.color.akira_accent));
        AtomicInteger lastPercent = new AtomicInteger(-2);

        networkExecutor.execute(() -> {
            try {
                TransferResult result = ConsoleClient.validatePayload(
                        getCacheDir(),
                        input.url.toString(),
                        (stage, percent, completed, total) -> {
                            if (percent >= 0 && lastPercent.getAndSet(percent) == percent) return;
                            runOnUiThread(() -> updateProgress(stage, percent, completed, total));
                        }
                );
                runOnUiThread(() -> {
                    appendLog(
                            "OK",
                            "Payload válido: " + formatBytes(result.getBytesSent())
                                    + " · SHA-256 " + result.getSha256().substring(0, 12) + "…",
                            color(R.color.akira_green)
                    );
                    networkStatus.setText("● Payload válido");
                    networkStatus.setTextColor(color(R.color.akira_green));
                    showStep(3);
                    Toast.makeText(this, "Payload validado. Nenhuma conexão foi aberta no console.", Toast.LENGTH_LONG).show();
                    setBusy(false, null);
                });
            } catch (Exception error) {
                runOnUiThread(() -> finishWithError("Payload inválido ou inacessível", error));
            }
        });
    }

    private void sendContent() {
        Input input = validateInput(true);
        if (input == null) return;
        if (mode == Mode.PAYLOAD && !trustedPayload.isChecked()) {
            trustedPayload.setError("Confirme antes de enviar.");
            appendLog("ATN", "Confirme a origem e o firmware do payload.", color(R.color.akira_warning));
            return;
        }

        saveSettings();
        showStep(3);
        if (mode == Mode.PACKAGE) {
            installPackage(input);
        } else {
            transmitPayload(input);
        }
    }

    private void installPackage(Input input) {
        setBusy(true, "Enviando pedido de instalação…");
        appendLog("INF", "Enviando PKG para " + input.ip + ':' + input.port + '.', color(R.color.akira_accent));
        networkExecutor.execute(() -> {
            try {
                ConsoleClient.installPackage(input.ip, input.port, input.url.toString());
                runOnUiThread(() -> {
                    appendLog("OK", "Pedido entregue. Acompanhe o download nas notificações do console.", color(R.color.akira_green));
                    networkStatus.setText("● Pedido entregue");
                    networkStatus.setTextColor(color(R.color.akira_green));
                    setBusy(false, null);
                });
            } catch (Exception error) {
                runOnUiThread(() -> finishWithError("Falha ao instalar o pacote", error));
            }
        });
    }

    private void transmitPayload(Input input) {
        setBusy(true, "Preparando payload…");
        progress.setIndeterminate(true);
        appendLog("INF", "Baixando e validando o payload antes de acessar o BinLoader.", color(R.color.akira_accent));
        AtomicInteger lastPercent = new AtomicInteger(-2);

        networkExecutor.execute(() -> {
            try {
                TransferResult result = ConsoleClient.sendPayload(
                        getCacheDir(),
                        input.ip,
                        input.port,
                        input.url.toString(),
                        (stage, percent, completed, total) -> {
                            if (percent >= 0 && lastPercent.getAndSet(percent) == percent) return;
                            runOnUiThread(() -> updateProgress(stage, percent, completed, total));
                        }
                );
                runOnUiThread(() -> {
                    appendLog(
                            "OK",
                            "Payload transmitido: " + formatBytes(result.getBytesSent())
                                    + " · SHA-256 " + result.getSha256().substring(0, 12) + "…",
                            color(R.color.akira_green)
                    );
                    networkStatus.setText("● Payload enviado");
                    networkStatus.setTextColor(color(R.color.akira_green));
                    setBusy(false, null);
                });
            } catch (Exception error) {
                runOnUiThread(() -> finishWithError("Falha ao enviar o payload", error));
            }
        });
    }

    private Input validateInput(boolean requireUrl) {
        resetErrors();
        String ip = text(consoleIp);
        if (!UrlUtils.isPrivateIpv4(ip)) {
            ipLayout.setError("Use um IPv4 privado, como 192.168.1.108.");
            consoleIp.requestFocus();
            return null;
        }

        int selectedPort;
        try {
            selectedPort = Integer.parseInt(text(port));
            if (selectedPort < 1 || selectedPort > 65535) throw new NumberFormatException();
        } catch (NumberFormatException error) {
            portLayout.setError("Porta inválida.");
            port.requestFocus();
            return null;
        }

        URL url = null;
        if (requireUrl) {
            try {
                url = UrlUtils.requireHttpUrl(text(downloadUrl));
                showStep(2);
                if (!url.toString().equals(text(downloadUrl))) {
                    downloadUrl.setText(url.toString());
                    downloadUrl.setSelection(url.toString().length());
                    appendLog("INF", "Link do GitHub convertido automaticamente para download direto.", color(R.color.akira_accent));
                }
            } catch (Exception error) {
                urlLayout.setError(error.getMessage());
                downloadUrl.requestFocus();
                return null;
            }
        }
        return new Input(ip, selectedPort, url);
    }

    private void resetErrors() {
        ipLayout.setError(null);
        urlLayout.setError(null);
        portLayout.setError(null);
        trustedPayload.setError(null);
    }

    private void setBusy(boolean busy, String message) {
        modeGroup.setEnabled(!busy);
        consoleGroup.setEnabled(!busy);
        testButton.setEnabled(!busy);
        sendButton.setEnabled(!busy);
        consoleIp.setEnabled(!busy);
        downloadUrl.setEnabled(!busy);
        port.setEnabled(!busy);
        trustedPayload.setEnabled(!busy);
        progress.setVisibility(busy ? View.VISIBLE : View.GONE);
        progressLabel.setVisibility(busy ? View.VISIBLE : View.GONE);
        if (busy) {
            networkStatus.setText("● Trabalhando");
            networkStatus.setTextColor(color(R.color.akira_accent));
            progressLabel.setText(message == null ? "Aguarde…" : message);
        } else {
            progress.setProgress(0);
            progress.setIndeterminate(false);
            progressLabel.setText("");
        }
    }

    private void updateProgress(String stage, int percent, long completed, long total) {
        progress.setIndeterminate(percent < 0);
        if (percent >= 0) progress.setProgress(percent);
        String detail = total > 0
                ? " · " + formatBytes(completed) + " / " + formatBytes(total)
                : " · " + formatBytes(completed);
        progressLabel.setText(stage + (percent >= 0 ? " " + percent + "%" : "") + detail);
    }

    private void finishWithError(String context, Exception error) {
        String detail = friendlyError(error);
        appendLog("ERR", context + ": " + detail, color(R.color.akira_error));
        networkStatus.setText("● Erro");
        networkStatus.setTextColor(color(R.color.akira_error));
        Toast.makeText(this, context + ". Veja a atividade para corrigir.", Toast.LENGTH_LONG).show();
        setBusy(false, null);
    }

    private String friendlyError(Exception error) {
        if (error instanceof UnknownHostException) {
            return "o servidor do link não foi encontrado. Confira a internet e o endereço.";
        }
        if (error instanceof ConnectException) {
            return mode == Mode.PAYLOAD
                    ? "conexão recusada. Ative o BinLoader/ELF Loader e confira IP e porta."
                    : "conexão recusada. Abra o Remote Package Installer/DPI no console.";
        }
        if (error instanceof SocketTimeoutException) {
            return "tempo esgotado. Confirme que celular e console estão na mesma rede e sem isolamento de Wi-Fi.";
        }
        return error.getMessage() == null ? error.getClass().getSimpleName() : error.getMessage();
    }

    private void clearLog() {
        logBuffer.clear();
        logView.setText("");
        appendLog("INF", "Atividade limpa.", color(R.color.akira_accent));
    }

    private void copyLog() {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        String diagnostic = "Akirabox 1.2.0\nModo: " + mode.name()
                + "\nConsole: " + consoleType.name()
                + "\nIP/porta: " + text(consoleIp) + ':' + text(port)
                + "\n\n" + logView.getText();
        clipboard.setPrimaryClip(ClipData.newPlainText("Diagnóstico Akirabox", diagnostic));
        Toast.makeText(this, "Diagnóstico copiado.", Toast.LENGTH_SHORT).show();
    }

    private void pasteUrl() {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        if (!clipboard.hasPrimaryClip() || clipboard.getPrimaryClip() == null
                || clipboard.getPrimaryClip().getItemCount() == 0) {
            Toast.makeText(this, "A área de transferência está vazia.", Toast.LENGTH_SHORT).show();
            return;
        }
        CharSequence value = clipboard.getPrimaryClip().getItemAt(0).coerceToText(this);
        applySharedUrl(value == null ? "" : value.toString(), "Link colado");
    }

    private void handleIncomingShare(Intent intent) {
        if (intent == null || !Intent.ACTION_SEND.equals(intent.getAction())
                || !"text/plain".equals(intent.getType())) return;
        applySharedUrl(intent.getStringExtra(Intent.EXTRA_TEXT), "Link recebido de outro aplicativo");
    }

    private void applySharedUrl(String sharedText, String source) {
        String extracted = UrlUtils.extractFirstHttpUrl(sharedText);
        if (extracted == null) {
            Toast.makeText(this, "Nenhum link HTTP/HTTPS válido foi encontrado.", Toast.LENGTH_LONG).show();
            return;
        }
        String lower = extracted.toLowerCase(Locale.ROOT);
        if (lower.matches(".*\\.pkg(?:[?#].*)?$")) {
            modeGroup.check(R.id.modePkg);
        } else if (lower.matches(".*\\.(?:bin|elf)(?:[?#].*)?$")) {
            modeGroup.check(R.id.modePayload);
        }
        String normalized = UrlUtils.normalizeGitHubBlob(extracted);
        downloadUrl.setText(normalized);
        downloadUrl.setSelection(normalized.length());
        showStep(1);
        saveSettings();
        appendLog("INF", source + " e pronto para validar.", color(R.color.akira_accent));
        Toast.makeText(this, source + ".", Toast.LENGTH_SHORT).show();
    }

    private void showHelp() {
        new MaterialAlertDialogBuilder(this)
                .setTitle("Ajuda rápida")
                .setMessage(
                        "INSTALAR PKG\n"
                                + "Abra o Remote Package Installer ou DPI no console. Use normalmente a porta 12800.\n\n"
                                + "ENVIAR PAYLOAD\n"
                                + "No PS4, ative o GoldHEN BinLoader na porta 9090. No PS5, use o etaHEN ELF Loader na porta 9021.\n\n"
                                + "PAYLOAD FORMAT ERROR\n"
                                + "Use o arquivo BIN/ELF direto, compatível com seu firmware. Não envie páginas HTML ou arquivos ZIP. O botão “Validar payload” verifica o download sem abrir a porta do console.\n\n"
                                + "SUPORTE\n"
                                + "Use “Copiar” na seção Atividade para compartilhar um diagnóstico sem incluir o link de download."
                )
                .setNeutralButton("Copiar diagnóstico", (dialog, which) -> copyLog())
                .setPositiveButton("Entendi", null)
                .show();
    }

    private void appendLog(String code, String message, @ColorInt int accentColor) {
        if (logBuffer.length() > 12_000) {
            logBuffer.delete(0, logBuffer.length() - 8_000);
        }
        if (logBuffer.length() > 0) logBuffer.append('\n');
        logBuffer.append(clock.format(new Date())).append("  ");
        SpannableString coloredCode = new SpannableString(code);
        coloredCode.setSpan(new ForegroundColorSpan(accentColor), 0, code.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        logBuffer.append(coloredCode).append("  ").append(message);
        logView.setText(logBuffer);
    }

    private String text(TextInputEditText field) {
        return field.getText() == null ? "" : field.getText().toString().trim();
    }

    private int color(int resource) {
        return ContextCompat.getColor(this, resource);
    }

    private void showStep(int activeIndex) {
        TextView[] steps = {stepConsole, stepLink, stepValidate, stepSend};
        for (int index = 0; index < steps.length; index++) {
            boolean reached = index <= activeIndex;
            steps[index].setBackgroundResource(reached ? R.drawable.bg_step_active : R.drawable.bg_step_idle);
            steps[index].setTextColor(color(reached ? R.color.akira_accent_bright : R.color.akira_muted));
        }
    }

    private static String formatBytes(long value) {
        if (value < 1024) return value + " B";
        if (value < 1024L * 1024L) return String.format(Locale.getDefault(), "%.1f KB", value / 1024d);
        return String.format(Locale.getDefault(), "%.2f MB", value / 1024d / 1024d);
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handleIncomingShare(intent);
    }

    @Override
    protected void onPause() {
        saveSettings();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        networkExecutor.shutdownNow();
        super.onDestroy();
    }

    private static final class Input {
        private final String ip;
        private final int port;
        private final URL url;

        private Input(String ip, int port, URL url) {
            this.ip = ip;
            this.port = port;
            this.url = url;
        }
    }
}
