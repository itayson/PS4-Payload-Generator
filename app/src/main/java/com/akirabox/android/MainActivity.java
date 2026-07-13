package com.akirabox.android;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.text.SpannableString;
import android.text.SpannableStringBuilder;
import android.text.Spanned;
import android.text.style.ForegroundColorSpan;
import android.view.View;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import androidx.annotation.ColorInt;
import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.content.ContextCompat;

import com.akirabox.android.net.ConsoleClient;
import com.akirabox.android.net.ProgressListener;
import com.akirabox.android.net.TransferResult;
import com.akirabox.android.net.UrlUtils;
import com.google.android.material.button.MaterialButton;
import com.google.android.material.checkbox.MaterialCheckBox;
import com.google.android.material.dialog.MaterialAlertDialogBuilder;
import com.google.android.material.textfield.TextInputEditText;
import com.google.android.material.textfield.TextInputLayout;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
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
    private static final String PREFS = "orbisdrop_settings";
    private static final int BINLOADER_PORT = 9090;
    private static final long MAX_LOCAL_PAYLOAD_BYTES = 64L * 1024L * 1024L;

    private final ExecutorService networkExecutor = Executors.newSingleThreadExecutor();
    private final SimpleDateFormat clock = new SimpleDateFormat("HH:mm:ss", Locale.getDefault());
    private final SpannableStringBuilder logBuffer = new SpannableStringBuilder();

    private TextInputLayout ipLayout;
    private TextInputLayout urlLayout;
    private TextInputEditText consoleIp;
    private TextInputEditText downloadUrl;
    private MaterialCheckBox trustedPayload;
    private MaterialButton validateButton;
    private MaterialButton sendButton;
    private MaterialButton pasteUrlButton;
    private MaterialButton choosePayloadFileButton;
    private View selectedFileCard;
    private TextView selectedFileLabel;
    private ProgressBar progress;
    private TextView progressLabel;
    private TextView networkStatus;
    private TextView logView;
    private SharedPreferences preferences;
    private ActivityResultLauncher<String[]> payloadFilePicker;
    private Uri selectedPayloadUri;
    private String selectedPayloadName;
    private long selectedPayloadSize = -1;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        payloadFilePicker = registerForActivityResult(
                new ActivityResultContracts.OpenDocument(), this::selectPayloadFile);
        setContentView(R.layout.activity_main);
        preferences = getSharedPreferences(PREFS, MODE_PRIVATE);
        bindViews();
        restoreSettings();
        configureControls();
        updateSelectedFileUi();
        appendLog("INF", "OrbisDrop pronto. Destino fixo: PS4 GoldHEN BinLoader na porta 9090.", color(R.color.akira_accent));
        handleIncomingShare(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handleIncomingShare(intent);
    }

    private void bindViews() {
        ipLayout = findViewById(R.id.ipLayout);
        urlLayout = findViewById(R.id.urlLayout);
        consoleIp = findViewById(R.id.consoleIp);
        downloadUrl = findViewById(R.id.downloadUrl);
        trustedPayload = findViewById(R.id.trustedPayload);
        validateButton = findViewById(R.id.validateButton);
        sendButton = findViewById(R.id.sendButton);
        pasteUrlButton = findViewById(R.id.pasteUrlButton);
        choosePayloadFileButton = findViewById(R.id.choosePayloadFileButton);
        selectedFileCard = findViewById(R.id.selectedFileCard);
        selectedFileLabel = findViewById(R.id.selectedFileLabel);
        progress = findViewById(R.id.progress);
        progressLabel = findViewById(R.id.progressLabel);
        networkStatus = findViewById(R.id.networkStatus);
        logView = findViewById(R.id.logView);
        findViewById(R.id.clearLog).setOnClickListener(view -> clearLog());
        findViewById(R.id.copyLog).setOnClickListener(view -> copyLog());
        findViewById(R.id.clearSelectedFile).setOnClickListener(view -> clearSelectedPayload(true));
    }

    private void configureControls() {
        validateButton.setOnClickListener(view -> validatePayload());
        sendButton.setOnClickListener(view -> sendPayload());
        pasteUrlButton.setOnClickListener(view -> pasteUrl());
        choosePayloadFileButton.setOnClickListener(view -> payloadFilePicker.launch(new String[] {"*/*"}));
    }

    private void restoreSettings() {
        consoleIp.setText(preferences.getString("console_ip", ""));
        downloadUrl.setText(preferences.getString("payload_url", ""));
        String storedUri = preferences.getString("payload_uri", "");
        if (storedUri != null && !storedUri.trim().isEmpty()) {
            selectedPayloadUri = Uri.parse(storedUri);
            selectedPayloadName = preferences.getString("payload_name", "payload.bin");
            selectedPayloadSize = preferences.getLong("payload_size", -1);
        }
    }

    private void saveSettings() {
        SharedPreferences.Editor editor = preferences.edit()
                .putString("console_ip", text(consoleIp))
                .putString("payload_url", text(downloadUrl));
        if (selectedPayloadUri == null) {
            editor.remove("payload_uri").remove("payload_name").remove("payload_size");
        } else {
            editor.putString("payload_uri", selectedPayloadUri.toString())
                    .putString("payload_name", selectedPayloadName)
                    .putLong("payload_size", selectedPayloadSize);
        }
        editor.apply();
    }

    private void validatePayload() {
        Input input = validateInput(false);
        if (input == null) return;
        saveSettings();
        executeTransfer(input, false);
    }

    private void sendPayload() {
        Input input = validateInput(true);
        if (input == null) return;
        if (!trustedPayload.isChecked()) {
            trustedPayload.setError("Confirme antes de enviar.");
            appendLog("ATN", "Confirme a origem e a compatibilidade do payload com seu firmware.", color(R.color.akira_warning));
            return;
        }
        saveSettings();
        executeTransfer(input, true);
    }

    private void executeTransfer(Input input, boolean send) {
        setBusy(true, input.hasLocalPayload() ? "Lendo arquivo local…" : "Baixando payload…");
        progress.setIndeterminate(true);
        appendLog("INF", send
                ? "Preparando payload para " + input.ip + ":9090."
                : "Validando payload sem abrir conexão com o PS4.", color(R.color.akira_accent));
        AtomicInteger lastPercent = new AtomicInteger(-2);
        networkExecutor.execute(() -> {
            File temporary = null;
            try {
                ProgressListener listener = (stage, percent, completed, total) -> {
                    if (percent >= 0 && lastPercent.getAndSet(percent) == percent) return;
                    runOnUiThread(() -> updateProgress(stage, percent, completed, total));
                };
                TransferResult result;
                if (input.hasLocalPayload()) {
                    temporary = copyPayloadToCache(input, listener);
                    result = send
                            ? ConsoleClient.sendPayloadFile(temporary, input.localPayloadName, input.ip, BINLOADER_PORT, listener)
                            : ConsoleClient.validatePayloadFile(temporary, input.localPayloadName, listener);
                } else {
                    result = send
                            ? ConsoleClient.sendPayload(getCacheDir(), input.ip, BINLOADER_PORT, input.url.toString(), listener)
                            : ConsoleClient.validatePayload(getCacheDir(), input.url.toString(), listener);
                }
                TransferResult completed = result;
                runOnUiThread(() -> finishSuccess(completed, send));
            } catch (Exception error) {
                runOnUiThread(() -> finishWithError(send ? "Falha ao enviar o payload" : "Payload inválido ou inacessível", error));
            } finally {
                deleteTemporary(temporary);
            }
        });
    }

    private Input validateInput(boolean requireIp) {
        resetErrors();
        String ip = text(consoleIp);
        if (requireIp && !UrlUtils.isPrivateIpv4(ip)) {
            ipLayout.setError("Use o IPv4 local do PS4, como 192.168.1.108.");
            consoleIp.requestFocus();
            return null;
        }
        if (!requireIp && !ip.isEmpty() && !UrlUtils.isPrivateIpv4(ip)) {
            ipLayout.setError("IPv4 local inválido.");
            return null;
        }
        if (selectedPayloadUri != null) {
            return new Input(ip, null, selectedPayloadUri, selectedPayloadName, selectedPayloadSize);
        }
        try {
            URL url = UrlUtils.requireHttpUrl(text(downloadUrl));
            String normalized = url.toString();
            if (!normalized.equals(text(downloadUrl))) {
                downloadUrl.setText(normalized);
                downloadUrl.setSelection(normalized.length());
                appendLog("INF", "Link normalizado para download direto.", color(R.color.akira_accent));
            }
            return new Input(ip, url, null, null, -1);
        } catch (Exception error) {
            urlLayout.setError(error.getMessage());
            downloadUrl.requestFocus();
            return null;
        }
    }

    private void finishSuccess(TransferResult result, boolean sent) {
        String hash = result.getSha256();
        String shortHash = hash.length() > 12 ? hash.substring(0, 12) + "…" : hash;
        appendLog("OK", (sent ? "Payload enviado" : "Payload válido") + ": "
                + formatBytes(result.getBytesSent()) + " · SHA-256 " + shortHash, color(R.color.akira_green));
        networkStatus.setText(sent ? "● Enviado" : "● Válido");
        networkStatus.setTextColor(color(R.color.akira_green));
        Toast.makeText(this, sent ? "Payload transmitido ao PS4 pela porta 9090." : "Payload validado.", Toast.LENGTH_LONG).show();
        setBusy(false, null);
    }

    private void setBusy(boolean busy, String message) {
        validateButton.setEnabled(!busy);
        sendButton.setEnabled(!busy);
        pasteUrlButton.setEnabled(!busy);
        choosePayloadFileButton.setEnabled(!busy);
        consoleIp.setEnabled(!busy);
        downloadUrl.setEnabled(!busy && selectedPayloadUri == null);
        trustedPayload.setEnabled(!busy);
        progress.setVisibility(busy ? View.VISIBLE : View.GONE);
        progressLabel.setVisibility(busy ? View.VISIBLE : View.GONE);
        if (busy) {
            networkStatus.setText("● Trabalhando");
            networkStatus.setTextColor(color(R.color.akira_accent));
            progressLabel.setText(message == null ? "Processando…" : message);
        } else {
            progress.setProgress(0);
            progress.setIndeterminate(false);
            progressLabel.setText("");
        }
    }

    private void updateProgress(String stage, int percent, long completed, long total) {
        progress.setIndeterminate(percent < 0);
        if (percent >= 0) progress.setProgress(percent);
        String detail = total > 0 ? " · " + formatBytes(completed) + " / " + formatBytes(total)
                : " · " + formatBytes(completed);
        progressLabel.setText(stage + (percent >= 0 ? " " + percent + "%" : "") + detail);
    }

    private void finishWithError(String context, Exception error) {
        appendLog("ERR", context + ": " + friendlyError(error), color(R.color.akira_error));
        networkStatus.setText("● Erro");
        networkStatus.setTextColor(color(R.color.akira_error));
        Toast.makeText(this, context + ". Consulte a atividade.", Toast.LENGTH_LONG).show();
        setBusy(false, null);
    }

    private String friendlyError(Exception error) {
        if (error instanceof UnknownHostException) return "servidor do link não encontrado.";
        if (error instanceof ConnectException) return "conexão recusada. Ative o GoldHEN BinLoader e confira o IP do PS4.";
        if (error instanceof SocketTimeoutException) return "tempo esgotado. Celular e PS4 devem estar na mesma rede.";
        return error.getMessage() == null ? error.getClass().getSimpleName() : error.getMessage();
    }

    private void pasteUrl() {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        if (!clipboard.hasPrimaryClip() || clipboard.getPrimaryClip() == null || clipboard.getPrimaryClip().getItemCount() == 0) {
            Toast.makeText(this, "A área de transferência está vazia.", Toast.LENGTH_SHORT).show();
            return;
        }
        CharSequence value = clipboard.getPrimaryClip().getItemAt(0).coerceToText(this);
        applySharedUrl(value == null ? "" : value.toString(), "Link colado");
    }

    private void handleIncomingShare(Intent intent) {
        if (intent == null || !Intent.ACTION_SEND.equals(intent.getAction())) return;
        Uri stream = intent.getParcelableExtra(Intent.EXTRA_STREAM);
        if (stream != null) {
            selectPayloadFile(stream);
        } else if ("text/plain".equals(intent.getType())) {
            applySharedUrl(intent.getStringExtra(Intent.EXTRA_TEXT), "Link recebido");
        }
    }

    private void applySharedUrl(String sharedText, String source) {
        String extracted = UrlUtils.extractFirstHttpUrl(sharedText);
        if (extracted == null) {
            Toast.makeText(this, "Nenhum link HTTP/HTTPS foi encontrado.", Toast.LENGTH_LONG).show();
            return;
        }
        clearSelectedPayload(false);
        try {
            String normalized = UrlUtils.requireHttpUrl(extracted).toString();
            downloadUrl.setText(normalized);
            downloadUrl.setSelection(normalized.length());
            saveSettings();
            appendLog("INF", source + " e pronto para validar.", color(R.color.akira_accent));
        } catch (Exception error) {
            urlLayout.setError(error.getMessage());
        }
    }

    private void selectPayloadFile(Uri uri) {
        if (uri == null) return;
        PayloadMetadata metadata = readPayloadMetadata(uri);
        String lower = metadata.name.toLowerCase(Locale.ROOT);
        if (!lower.endsWith(".bin") && !lower.endsWith(".elf")) {
            new MaterialAlertDialogBuilder(this)
                    .setTitle("Arquivo não reconhecido")
                    .setMessage("Selecione um payload BIN ou ELF extraído. Páginas HTML, ZIP, RAR e 7z não são payloads.")
                    .setPositiveButton("Escolher outro", (dialog, which) -> payloadFilePicker.launch(new String[] {"*/*"}))
                    .setNegativeButton("Cancelar", null).show();
            return;
        }
        if (metadata.size > MAX_LOCAL_PAYLOAD_BYTES) {
            Toast.makeText(this, "O payload ultrapassa 64 MB.", Toast.LENGTH_LONG).show();
            return;
        }
        try {
            getContentResolver().takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
        } catch (SecurityException ignored) { }
        selectedPayloadUri = uri;
        selectedPayloadName = metadata.name;
        selectedPayloadSize = metadata.size;
        downloadUrl.setText("");
        trustedPayload.setChecked(false);
        updateSelectedFileUi();
        saveSettings();
        appendLog("INF", "Arquivo selecionado: " + metadata.name, color(R.color.akira_accent));
    }

    private PayloadMetadata readPayloadMetadata(Uri uri) {
        String name = null;
        long size = -1;
        try (Cursor cursor = getContentResolver().query(uri,
                new String[] {OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE}, null, null, null)) {
            if (cursor != null && cursor.moveToFirst()) {
                int nameColumn = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                int sizeColumn = cursor.getColumnIndex(OpenableColumns.SIZE);
                if (nameColumn >= 0 && !cursor.isNull(nameColumn)) name = cursor.getString(nameColumn);
                if (sizeColumn >= 0 && !cursor.isNull(sizeColumn)) size = cursor.getLong(sizeColumn);
            }
        } catch (RuntimeException ignored) { }
        if (name == null || name.trim().isEmpty()) {
            String segment = uri.getLastPathSegment();
            name = segment == null ? "payload.bin" : segment;
        }
        return new PayloadMetadata(name, size);
    }

    private File copyPayloadToCache(Input input, ProgressListener listener) throws IOException {
        File target = File.createTempFile("orbisdrop-local-", ".tmp", getCacheDir());
        long copied = 0;
        try (InputStream source = getContentResolver().openInputStream(input.localPayloadUri);
             FileOutputStream output = new FileOutputStream(target)) {
            if (source == null) throw new IOException("Não foi possível abrir o arquivo local.");
            byte[] buffer = new byte[8192];
            int read;
            while ((read = source.read(buffer)) != -1) {
                copied += read;
                if (copied > MAX_LOCAL_PAYLOAD_BYTES) throw new IOException("Payload acima do limite de 64 MB.");
                output.write(buffer, 0, read);
                int percent = input.localPayloadSize > 0 ? (int) Math.min(100, copied * 100L / input.localPayloadSize) : -1;
                listener.onProgress("Lendo arquivo local", percent, copied, input.localPayloadSize);
            }
        } catch (IOException error) {
            deleteTemporary(target);
            throw error;
        }
        return target;
    }

    private void clearSelectedPayload(boolean notify) {
        selectedPayloadUri = null;
        selectedPayloadName = null;
        selectedPayloadSize = -1;
        updateSelectedFileUi();
        saveSettings();
        if (notify) Toast.makeText(this, "Arquivo removido. Use um link ou selecione outro arquivo.", Toast.LENGTH_SHORT).show();
    }

    private void updateSelectedFileUi() {
        boolean selected = selectedPayloadUri != null;
        selectedFileCard.setVisibility(selected ? View.VISIBLE : View.GONE);
        downloadUrl.setEnabled(!selected);
        if (selected) selectedFileLabel.setText(selectedPayloadName + (selectedPayloadSize >= 0 ? " · " + formatBytes(selectedPayloadSize) : ""));
    }

    private void clearLog() {
        logBuffer.clear();
        logView.setText("");
        appendLog("INF", "Atividade limpa.", color(R.color.akira_accent));
    }

    private void copyLog() {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        String diagnostic = "OrbisDrop\nDestino: PS4 BinLoader 9090\nIP: " + text(consoleIp)
                + "\nFonte: " + (selectedPayloadUri == null ? "link" : "arquivo local") + "\n\n" + logView.getText();
        clipboard.setPrimaryClip(ClipData.newPlainText("Diagnóstico OrbisDrop", diagnostic));
        Toast.makeText(this, "Diagnóstico copiado.", Toast.LENGTH_SHORT).show();
    }

    private void appendLog(String level, String message, @ColorInt int color) {
        SpannableString line = new SpannableString("[" + clock.format(new Date()) + "] " + level + "  " + message + "\n");
        line.setSpan(new ForegroundColorSpan(color), 0, line.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        logBuffer.append(line);
        logView.setText(logBuffer);
    }

    private void resetErrors() {
        ipLayout.setError(null);
        urlLayout.setError(null);
        trustedPayload.setError(null);
    }

    private String text(TextInputEditText field) {
        return field.getText() == null ? "" : field.getText().toString().trim();
    }

    private int color(int id) {
        return ContextCompat.getColor(this, id);
    }

    private String formatBytes(long bytes) {
        if (bytes < 1024) return bytes + " B";
        double value = bytes;
        String[] units = {"KB", "MB", "GB"};
        int unit = -1;
        do { value /= 1024.0; unit++; } while (value >= 1024 && unit < units.length - 1);
        return String.format(Locale.getDefault(), "%.1f %s", value, units[unit]);
    }

    private void deleteTemporary(File file) {
        if (file != null && file.exists() && !file.delete()) file.deleteOnExit();
    }

    @Override
    protected void onDestroy() {
        networkExecutor.shutdownNow();
        super.onDestroy();
    }

    private static final class Input {
        private final String ip;
        private final URL url;
        private final Uri localPayloadUri;
        private final String localPayloadName;
        private final long localPayloadSize;

        private Input(String ip, URL url, Uri localPayloadUri, String localPayloadName, long localPayloadSize) {
            this.ip = ip;
            this.url = url;
            this.localPayloadUri = localPayloadUri;
            this.localPayloadName = localPayloadName;
            this.localPayloadSize = localPayloadSize;
        }

        private boolean hasLocalPayload() { return localPayloadUri != null; }
    }

    private static final class PayloadMetadata {
        private final String name;
        private final long size;
        private PayloadMetadata(String name, long size) { this.name = name; this.size = size; }
    }
}
