package com.akirabox.android;

import android.content.SharedPreferences;
import android.os.Bundle;
import android.text.SpannableString;
import android.text.SpannableStringBuilder;
import android.text.Spanned;
import android.text.style.ForegroundColorSpan;
import android.view.View;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.ColorInt;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.content.ContextCompat;

import com.akirabox.android.net.ConsoleClient;
import com.akirabox.android.net.TransferResult;
import com.akirabox.android.net.UrlUtils;
import com.google.android.material.button.MaterialButton;
import com.google.android.material.button.MaterialButtonToggleGroup;
import com.google.android.material.checkbox.MaterialCheckBox;
import com.google.android.material.textfield.TextInputEditText;
import com.google.android.material.textfield.TextInputLayout;

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
    private ProgressBar progress;
    private TextView progressLabel;
    private TextView networkStatus;
    private TextView logView;
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
        progress = findViewById(R.id.progress);
        progressLabel = findViewById(R.id.progressLabel);
        networkStatus = findViewById(R.id.networkStatus);
        logView = findViewById(R.id.logView);
        findViewById(R.id.clearLog).setOnClickListener(view -> clearLog());
    }

    private void configureControls() {
        modeGroup.addOnButtonCheckedListener((group, checkedId, isChecked) -> {
            if (!isChecked) return;
            mode = checkedId == R.id.modePayload ? Mode.PAYLOAD : Mode.PACKAGE;
            updateModeUi();
            saveSettings();
        });

        consoleGroup.addOnButtonCheckedListener((group, checkedId, isChecked) -> {
            if (!isChecked) return;
            consoleType = checkedId == R.id.consolePs5 ? ConsoleType.PS5 : ConsoleType.PS4;
            updateDefaultPort();
            saveSettings();
        });

        testButton.setOnClickListener(view -> testConnection());
        sendButton.setOnClickListener(view -> sendContent());
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
    }

    private void saveSettings() {
        preferences.edit()
                .putString("console_ip", text(consoleIp))
                .putString("mode", mode.name())
                .putString("console", consoleType.name())
                .apply();
    }

    private void updateModeUi() {
        boolean payload = mode == Mode.PAYLOAD;
        trustedPayload.setVisibility(payload ? View.VISIBLE : View.GONE);
        urlLayout.setHint(payload ? "Link direto do payload BIN/ELF" : "Link direto do pacote PKG");
        sendButton.setText(payload ? "Enviar payload" : "Instalar pacote");
        testButton.setText(payload ? "Validar configuração" : "Testar instalador");
        updateDefaultPort();
        resetErrors();
    }

    private void updateDefaultPort() {
        String next;
        if (mode == Mode.PACKAGE) {
            next = "12800";
        } else {
            next = consoleType == ConsoleType.PS4 ? "9090" : "9021";
        }
        port.setText(next);
    }

    private void testConnection() {
        Input input = validateInput(false);
        if (input == null) return;
        saveSettings();

        if (mode == Mode.PAYLOAD) {
            appendLog(
                    "OK",
                    "Configuração válida. Para evitar 'payload format error', a porta será aberta somente durante o envio real.",
                    color(R.color.akira_green)
            );
            networkStatus.setText("● Configuração válida");
            networkStatus.setTextColor(color(R.color.akira_green));
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
                    setBusy(false, null);
                });
            } catch (Exception error) {
                runOnUiThread(() -> finishWithError("Não foi possível localizar o instalador", error));
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
            return null;
        }

        int selectedPort;
        try {
            selectedPort = Integer.parseInt(text(port));
            if (selectedPort < 1 || selectedPort > 65535) throw new NumberFormatException();
        } catch (NumberFormatException error) {
            portLayout.setError("Porta inválida.");
            return null;
        }

        URL url = null;
        if (requireUrl) {
            try {
                url = UrlUtils.requireHttpUrl(text(downloadUrl));
            } catch (Exception error) {
                urlLayout.setError(error.getMessage());
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
        String detail = error.getMessage() == null ? error.getClass().getSimpleName() : error.getMessage();
        appendLog("ERR", context + ": " + detail, color(R.color.akira_error));
        networkStatus.setText("● Erro");
        networkStatus.setTextColor(color(R.color.akira_error));
        setBusy(false, null);
    }

    private void clearLog() {
        logBuffer.clear();
        logView.setText("");
        appendLog("INF", "Atividade limpa.", color(R.color.akira_accent));
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

    private static String formatBytes(long value) {
        if (value < 1024) return value + " B";
        if (value < 1024L * 1024L) return String.format(Locale.getDefault(), "%.1f KB", value / 1024d);
        return String.format(Locale.getDefault(), "%.2f MB", value / 1024d / 1024d);
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
