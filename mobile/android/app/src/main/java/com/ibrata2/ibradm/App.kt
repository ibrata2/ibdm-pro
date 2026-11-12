package com.ibrata2.ibradm

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.util.Log
import com.yausername.ffmpeg.FFmpeg
import com.yausername.youtubedl_android.YoutubeDL
import java.util.concurrent.CountDownLatch
import kotlin.concurrent.thread

class App : Application() {
    override fun onCreate() {
        super.onCreate()
        Downloads.load(this)
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(Engine.CHANNEL, L.t("التحميلات", "Downloads"), NotificationManager.IMPORTANCE_LOW))
        nm.createNotificationChannel(NotificationChannel(Engine.CHANNEL_DONE, L.t("اكتمال التحميل", "Finished downloads"), NotificationManager.IMPORTANCE_DEFAULT))
        Engine.start(this)
    }
}

/** The download engine: yt-dlp (with its own Python) and FFmpeg, the same tools the Windows app uses.
 * The first start unpacks them (a few seconds); yt-dlp updates itself once a day, because sites like YouTube
 * change all the time. */
object Engine {
    const val CHANNEL = "downloads"
    const val CHANNEL_DONE = "done"
    private val ready = CountDownLatch(1)
    @Volatile var error: String? = null

    fun start(ctx: Context) {
        thread(name = "engine") {
            try {
                YoutubeDL.getInstance().init(ctx)
                FFmpeg.getInstance().init(ctx)
            } catch (e: Throwable) {
                Log.e("IBRA", "engine", e)
                error = e.message ?: e.toString()
            }
            ready.countDown()
            updateIfDue(ctx)
        }
    }

    /** Waits until the engine is unpacked. */
    fun await() { ready.await() }

    private fun updateIfDue(ctx: Context) {
        if (error != null) return
        val prefs = ctx.getSharedPreferences("engine", Context.MODE_PRIVATE)
        val last = prefs.getLong("updated", 0)
        if (System.currentTimeMillis() - last < 24 * 3600 * 1000L) return
        try {
            YoutubeDL.getInstance().updateYoutubeDL(ctx, YoutubeDL.UpdateChannel.STABLE)
            prefs.edit().putLong("updated", System.currentTimeMillis()).apply()
        } catch (e: Throwable) {
            Log.w("IBRA", "engine update: ${e.message}")
        }
    }

    fun version(ctx: Context): String = try { YoutubeDL.getInstance().version(ctx) ?: "" } catch (e: Throwable) { "" }
}
