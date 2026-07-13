package com.akirabox.android.net;

public interface ProgressListener {
    void onProgress(String stage, int percent, long completedBytes, long totalBytes);
}
