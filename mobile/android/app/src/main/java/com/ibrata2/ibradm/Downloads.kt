package com.ibrata2.ibradm

import android.content.Context
import android.os.Handler
import android.os.Looper
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale
import java.util.UUID

/** Texts: Arabic, or English when the phone is not in Arabic. */
object L {
    val ar: Boolean get() = Locale.getDefault().language == "ar"
    fun t(ar: String, en: String) = if (this.ar) ar else en
}

/** One download: a link and how to take it ("best", "1080", "720", "480" or "mp3"). */
class Job(
    val id: String = UUID.randomUUID().toString().take(12),
    val url: String,
    val mode: String,
    var title: String = "",
    var status: String = WAITING,
    var progress: Float = 0f,
    var detail: String = "",
    var uri: String? = null,
    var mime: String? = null,
    var error: String? = null,
    val added: Long = System.currentTimeMillis(),
) {
    companion object {
        const val WAITING = "waiting"
        const val RUNNING = "running"
        const val DONE = "done"
        const val FAILED = "failed"
        const val CANCELLED = "cancelled"

        fun from(o: JSONObject) = Job(
            id = o.getString("id"), url = o.getString("url"), mode = o.optString("mode", "best"),
            title = o.optString("title"), status = o.optString("status", FAILED), progress = o.optDouble("progress", 0.0).toFloat(),
            detail = o.optString("detail"), uri = o.optString("uri").ifEmpty { null }, mime = o.optString("mime").ifEmpty { null },
            error = o.optString("error").ifEmpty { null }, added = o.optLong("added"),
        )
    }

    val active get() = status == WAITING || status == RUNNING

    fun json() = JSONObject().apply {
        put("id", id); put("url", url); put("mode", mode); put("title", title); put("status", status)
        put("progress", progress.toDouble()); put("detail", detail); put("uri", uri ?: ""); put("mime", mime ?: "")
        put("error", error ?: ""); put("added", added)
    }
}

/** The download list (kept on the phone) and who is watching it (the screen). */
object Downloads {
    val jobs = mutableListOf<Job>()
    private val listeners = mutableSetOf<() -> Unit>()
    private val main = Handler(Looper.getMainLooper())
    private var loaded = false

    fun load(ctx: Context) {
        if (loaded) return
        loaded = true
        val text = ctx.getSharedPreferences("downloads", Context.MODE_PRIVATE).getString("list", "[]") ?: "[]"
        val arr = JSONArray(text)
        synchronized(jobs) {
            for (i in 0 until arr.length()) {
                val j = Job.from(arr.getJSONObject(i))
                // The app was closed in the middle: those start again.
                if (j.status == Job.RUNNING) j.status = Job.WAITING
                jobs.add(j)
            }
        }
    }

    fun save(ctx: Context) {
        val arr = JSONArray()
        synchronized(jobs) { jobs.take(200).forEach { arr.put(it.json()) } }
        ctx.getSharedPreferences("downloads", Context.MODE_PRIVATE).edit().putString("list", arr.toString()).apply()
    }

    fun add(ctx: Context, job: Job) {
        synchronized(jobs) { jobs.add(0, job) }
        save(ctx)
        changed()
    }

    fun remove(ctx: Context, job: Job) {
        synchronized(jobs) { jobs.remove(job) }
        save(ctx)
        changed()
    }

    fun next(): Job? = synchronized(jobs) { jobs.lastOrNull { it.status == Job.WAITING } }

    fun watch(l: () -> Unit) { listeners.add(l) }
    fun unwatch(l: () -> Unit) { listeners.remove(l) }
    fun changed() = main.post { listeners.toList().forEach { it() } }
}
