package com.tayson.ps4linksender.util

object Format {
    fun bytes(value: Long): String {
        if (value < 0) return "?"
        val units = arrayOf("B", "KB", "MB", "GB", "TB")
        var v = value.toDouble()
        var i = 0
        while (v >= 1024 && i < units.lastIndex) { v /= 1024; i++ }
        return if (i == 0) "$value ${units[i]}" else "%.2f %s".format(v, units[i])
    }

    fun rate(bytesPerSecond: Long): String = if (bytesPerSecond <= 0) "—" else "${bytes(bytesPerSecond)}/s"

    fun percent(done: Long, total: Long): String {
        if (total <= 0) return "0%"
        val p = (done.coerceIn(0, total).toDouble() * 100.0 / total.toDouble())
        return if (p < 10.0) "%.1f%%".format(p) else "%.0f%%".format(p)
    }

    /** Hide signed query parameters/tokens from the UI while keeping the full URL internally. */
    fun redactUrl(url: String): String {
        val noFragment = url.substringBefore('#')
        val q = noFragment.indexOf('?')
        return if (q >= 0) noFragment.substring(0, q) + "?…" else noFragment
    }
}
