package com.tayson.proxylab;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.graphics.Color;
import android.os.Bundle;
import android.view.View;
import android.view.WindowManager;
import android.widget.*;
import java.net.*;
import java.util.*;
import java.util.concurrent.*;

public final class MainActivity extends Activity {
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private EditText address;
    private TextView status, proxyUrl;
    private Button resolveBtn, startBtn, stopBtn, copyBtn;
    private UrlResolver.Result resolved;
    private ProxyServer proxy;
    private String endpoint;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        ScrollView scroll = new ScrollView(this);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(24, 28, 24, 28);
        root.setBackgroundColor(Color.rgb(247, 248, 250));
        scroll.addView(root);
        TextView title = text("TaySon Proxy Lab", 26, true);
        root.addView(title);
        root.addView(text("Experimento independente • sem Cloudflare Worker", 14, false));
        addSpace(root);
        root.addView(text("URL do arquivo ou página MediaFire", 15, true));
        address = new EditText(this);
        address.setSingleLine(false);
        address.setMinLines(2);
        address.setTextSize(14);
        address.setHint("https://www.mediafire.com/file/... ou https://host/arquivo.pkg");
        address.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_VARIATION_URI);
        root.addView(address);
        resolveBtn = button("1. Resolver e validar", root);
        startBtn = button("2. Iniciar proxy local", root);
        stopBtn = button("Parar proxy", root);
        copyBtn = button("Copiar URL para o PS4", root);
        startBtn.setEnabled(false);
        stopBtn.setEnabled(false);
        copyBtn.setEnabled(false);
        addSpace(root);
        root.addView(text("Diagnóstico", 18, true));
        status = text("Cole uma URL e toque em Resolver e validar.", 14, false);
        status.setTextIsSelectable(true);
        root.addView(status);
        addSpace(root);
        root.addView(text("URL do proxy", 18, true));
        proxyUrl = text("Ainda não iniciado.", 13, false);
        proxyUrl.setTextIsSelectable(true);
        root.addView(proxyUrl);
        addSpace(root);
        root.addView(text("Mantenha esta tela aberta e o celular na mesma rede do PS4. O app não envia payload/ELF nem registra tarefa BGFT nesta versão experimental.", 13, false));
        setContentView(scroll);
        resolveBtn.setOnClickListener(v -> resolve());
        startBtn.setOnClickListener(v -> startProxy());
        stopBtn.setOnClickListener(v -> stopProxy());
        copyBtn.setOnClickListener(v -> {
            if (endpoint == null) return;
            ((ClipboardManager)getSystemService(Context.CLIPBOARD_SERVICE)).setPrimaryClip(ClipData.newPlainText("TaySon Proxy URL", endpoint));
            Toast.makeText(this, "URL copiada", Toast.LENGTH_SHORT).show();
        });
    }
    private TextView text(String s, int sp, boolean bold) {
        TextView t = new TextView(this); t.setText(s); t.setTextSize(sp); t.setTextColor(Color.rgb(28, 32, 38));
        t.setPadding(0, 8, 0, 8);
        if (bold) t.setTypeface(null, android.graphics.Typeface.BOLD);
        return t;
    }
    private Button button(String label, LinearLayout parent) {
        Button b = new Button(this); b.setText(label); b.setAllCaps(false); parent.addView(b); return b;
    }
    private void addSpace(LinearLayout parent) { View v = new View(this); parent.addView(v, new LinearLayout.LayoutParams(1, 20)); }
    private void resolve() {
        stopProxy();
        resolved = null; startBtn.setEnabled(false); resolveBtn.setEnabled(false);
        status.setText("Consultando provedor e validando os primeiros bytes do PKG...");
        final String url = address.getText().toString();
        worker.execute(() -> {
            try {
                UrlResolver.Result r = UrlResolver.resolve(url);
                resolved = r;
                String host = URI.create(r.url).getHost();
                String msg = "HTTP: " + r.status + "\nHost final: " + host + "\nHTTP Range 206: " + (r.rangeSupported ? "CONFIRMADO" : "NÃO CONFIRMADO")
                        + "\nTamanho: " + (r.totalBytes >= 0 ? Long.toString(r.totalBytes) + " bytes" : "não informado")
                        + "\nTipo: " + r.contentType + "\nCabeçalho PKG: CONFIRMADO";
                runOnUiThread(() -> { status.setText(msg); startBtn.setEnabled(r.rangeSupported); resolveBtn.setEnabled(true); });
            } catch (Exception e) {
                runOnUiThread(() -> { status.setText("Falha: " + e.getMessage()); resolveBtn.setEnabled(true); });
            }
        });
    }
    private void startProxy() {
        if (resolved == null || !resolved.rangeSupported) return;
        final String ip = localWifiIp();
        if (ip == null) { status.append("\nNenhum endereço IPv4 Wi-Fi encontrado."); return; }
        try {
            proxy = new ProxyServer(resolved);
            endpoint = proxy.start(ip);
            proxyUrl.setText(endpoint);
            startBtn.setEnabled(false); stopBtn.setEnabled(true); copyBtn.setEnabled(true);
            status.append("\nProxy iniciado na porta 8765. Teste HTTP limitado concluído; instalação no PS4 NÃO testada.");
        } catch (Exception e) { status.append("\nErro ao iniciar proxy: " + e.getMessage()); }
    }
    private void stopProxy() {
        if (proxy != null) { proxy.stop(); proxy = null; }
        endpoint = null;
        if (proxyUrl != null) proxyUrl.setText("Parado.");
        if (startBtn != null) startBtn.setEnabled(resolved != null && resolved.rangeSupported);
        if (stopBtn != null) stopBtn.setEnabled(false);
        if (copyBtn != null) copyBtn.setEnabled(false);
    }
    private static String localWifiIp() {
        try {
            Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
            while (interfaces.hasMoreElements()) {
                NetworkInterface n = interfaces.nextElement();
                if (!n.isUp() || n.isLoopback()) continue;
                String name = n.getName().toLowerCase(Locale.ROOT);
                if (!(name.startsWith("wlan") || name.startsWith("ap") || name.startsWith("swlan"))) continue;
                Enumeration<InetAddress> addresses = n.getInetAddresses();
                while (addresses.hasMoreElements()) {
                    InetAddress ip = addresses.nextElement();
                    if (ip instanceof Inet4Address && ip.isSiteLocalAddress()) return ip.getHostAddress();
                }
            }
        } catch (Exception ignored) { }
        return null;
    }
    @Override protected void onDestroy() { stopProxy(); worker.shutdownNow(); super.onDestroy(); }
}
