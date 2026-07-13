package com.akirabox.android.net;

public final class TransferResult {
    private final long bytesSent;
    private final String sha256;
    private final String finalUrl;

    public TransferResult(long bytesSent, String sha256, String finalUrl) {
        this.bytesSent = bytesSent;
        this.sha256 = sha256;
        this.finalUrl = finalUrl;
    }

    public long getBytesSent() {
        return bytesSent;
    }

    public String getSha256() {
        return sha256;
    }

    public String getFinalUrl() {
        return finalUrl;
    }
}
