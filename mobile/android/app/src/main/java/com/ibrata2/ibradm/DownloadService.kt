package com.ibrata2.ibradm

import android.app.Notification
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.IBinder
import android.provider.MediaStore
import android.webkit.MimeTypeMap
import com.yausername.youtubedl_android.YoutubeDL
import com.yausername.youtubedl_android.YoutubeDLRequest
import java.io.File
import kotlin.concurrent.thread

/** Downloads the list one by one in the background (a notification shows the progress), then puts each file in
 * Downloads/IBRA Download Manager where the gallery, the music app and the file manager see it. */
class DownloadService : Service() {
    companion object {
        const val FOLDER = "IBRA Download Manager"
        @Volatile private var running = false
        @Volatile var currentId: String? = null

        fun start(ctx: Context) {
            ctx.startForegroundService(Intent(ctx, DownloadService::class.java))
        }

        fun cancel(ctx: Context, job: Job) {
            if (job.status == Job.RUNNING) {
                try { YoutubeDL.getInstance().destroyProcessById(job.id) } catch (_: Throwable) { }
            }
            job.status = Job.CANCELLED
            job.detail = ""
            Downloads.save(ctx)
            Downloads.changed()
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val n = notification(L.t("جاري التحضير…", "Getting ready…"), -1f)
        if (Build.VERSION.SDK_INT >= 29) startForeground(1, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC) else startForeground(1, n)
        if (!running) {
            running = true
            thread(name = "downloads") {
                try { loop() } finally {
                    running = false
                    stopForeground(STOP_FOREGROUND_REMOVE)
                    stopSelf()
                }
            }
        }
        return START_NOT_STICKY
    }

    private fun loop() {
        Engine.await()
        while (true) {
            val job = Downloads.next() ?: return
            currentId = job.id
            run(job)
            currentId = null
            Downloads.save(this)
            Downloads.changed()
        }
    }

    private fun run(job: Job) {
        job.status = Job.RUNNING
        job.progress = 0f
        job.error = null
        job.detail = L.t("جاري البحث عن الملف…", "Looking for the file…")
        Downloads.changed()
        Engine.error?.let { fail(job, L.t("تعذر تشغيل محرك التحميل: ", "The download engine couldn't start: ") + it); return }

        val dir = File(cacheDir, "jobs/${job.id}").apply { deleteRecursively(); mkdirs() }
        val req = YoutubeDLRequest(job.url).apply {
            addOption("--no-playlist")
            addOption("--no-mtime")
            addOption("--newline")
            addOption("-o", dir.absolutePath + "/%(title).120B.%(ext)s")
            when (job.mode) {
                "mp3" -> { addOption("-f", "bestaudio/best"); addOption("-x"); addOption("--audio-format", "mp3"); addOption("--audio-quality", "0") }
                "best" -> { addOption("-f", "bv*+ba/b"); addOption("-S", "res,ext:mp4:m4a"); addOption("--merge-output-format", "mp4") }
                else -> { addOption("-f", "bv*+ba/b"); addOption("-S", "res:${job.mode},ext:mp4:m4a"); addOption("--merge-output-format", "mp4") }
            }
        }
        var lastShown = 0L
        try {
            YoutubeDL.getInstance().execute(req, job.id) { progress, eta, line ->
                if (job.status == Job.CANCELLED) return@execute
                if (progress >= 0f) job.progress = progress
                if (job.title.isEmpty() && line.contains("Destination: ")) job.title = File(line.substringAfter("Destination: ").trim()).nameWithoutExtension
                job.detail = when {
                    line.startsWith("[Merger]") || line.startsWith("[ExtractAudio]") || line.startsWith("[VideoConvertor]") -> L.t("جاري تجهيز الملف…", "Finishing the file…")
                    progress > 0f -> "${progress.toInt()}%" + if (eta > 0) " — " + L.t("باقي ", "") + fmtEta(eta) + L.t("", " left") else ""
                    else -> job.detail
                }
                val now = System.currentTimeMillis()
                if (now - lastShown > 700) {
                    lastShown = now
                    Downloads.changed()
                    update(job)
                }
            }
        } catch (e: Throwable) {
            if (job.status == Job.CANCELLED) { dir.deleteRecursively(); return }
            fail(job, cleanError(e.message ?: e.toString()))
            dir.deleteRecursively()
            return
        }
        if (job.status == Job.CANCELLED) { dir.deleteRecursively(); return }

        var file = dir.walkTopDown().filter { it.isFile && !it.name.endsWith(".part") && !it.name.endsWith(".ytdl") }.maxByOrNull { it.length() }
        if (file == null) { fail(job, L.t("لم يتم العثور على ملف بعد التحميل", "No file was found after the download")); return }
        file = withRealType(file)
        try {
            job.detail = L.t("جاري الحفظ…", "Saving…")
            Downloads.changed()
            val mime = mimeOf(file.name)
            val uri = publish(file, mime)
            job.uri = uri.toString()
            job.mime = mime
            job.title = file.nameWithoutExtension
            job.status = Job.DONE
            job.progress = 100f
            job.detail = file.name + " — " + size(file.length())
            done(job)
        } catch (e: Throwable) {
            fail(job, L.t("تعذر حفظ الملف: ", "Couldn't save the file: ") + (e.message ?: e.toString()))
        } finally {
            dir.deleteRecursively()
        }
    }

    /** Copies the finished file into Downloads/IBRA Download Manager (no storage permission needed). */
    private fun publish(file: File, mime: String): Uri {
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, file.name)
            put(MediaStore.MediaColumns.MIME_TYPE, mime)
            put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/" + FOLDER)
            put(MediaStore.MediaColumns.IS_PENDING, 1)
        }
        val uri = contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: error("MediaStore")
        contentResolver.openOutputStream(uri)!!.use { out -> file.inputStream().use { it.copyTo(out, 256 * 1024) } }
        values.clear()
        values.put(MediaStore.MediaColumns.IS_PENDING, 0)
        contentResolver.update(uri, values, null, null)
        return uri
    }

    private fun fail(job: Job, message: String) {
        job.status = Job.FAILED
        job.error = message
        job.detail = ""
        Downloads.changed()
        val nm = getSystemService(NotificationManager::class.java)
        nm.notify(job.id.hashCode(), Notification.Builder(this, Engine.CHANNEL_DONE)
            .setSmallIcon(R.drawable.ic_notify).setContentTitle(L.t("فشل التحميل", "Download failed"))
            .setContentText(message).setStyle(Notification.BigTextStyle().bigText(message))
            .setContentIntent(openApp()).setAutoCancel(true).build())
    }

    private fun done(job: Job) {
        val nm = getSystemService(NotificationManager::class.java)
        val open = Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse(job.uri), job.mime).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        val pi = PendingIntent.getActivity(this, job.id.hashCode(), open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        nm.notify(job.id.hashCode(), Notification.Builder(this, Engine.CHANNEL_DONE)
            .setSmallIcon(R.drawable.ic_notify).setContentTitle(L.t("اكتمل التحميل", "Download complete"))
            .setContentText(job.title).setContentIntent(pi).setAutoCancel(true).build())
    }

    private fun update(job: Job) {
        getSystemService(NotificationManager::class.java).notify(1, notification(job.title.ifEmpty { job.url }, job.progress))
    }

    private fun notification(text: String, progress: Float): Notification =
        Notification.Builder(this, Engine.CHANNEL)
            .setSmallIcon(R.drawable.ic_notify)
            .setContentTitle(L.t("جاري التحميل", "Downloading"))
            .setContentText(text)
            .setProgress(100, progress.coerceAtLeast(0f).toInt(), progress < 0f)
            .setOngoing(true)
            .setContentIntent(openApp())
            .build()

    private fun openApp(): PendingIntent =
        PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
}

fun mimeOf(name: String): String =
    MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substringAfterLast('.', "").lowercase()) ?: "application/octet-stream"

fun size(b: Long): String = when {
    b >= 1L shl 30 -> String.format("%.2f GB", b / (1L shl 30).toDouble())
    b >= 1L shl 20 -> String.format("%.1f MB", b / (1L shl 20).toDouble())
    b >= 1L shl 10 -> String.format("%.0f KB", b / (1L shl 10).toDouble())
    else -> "$b B"
}

fun fmtEta(s: Long): String = if (s >= 3600) "%d:%02d:%02d".format(s / 3600, s / 60 % 60, s % 60) else "%d:%02d".format(s / 60, s % 60)

/** yt-dlp's error, in a few words. */
fun cleanError(msg: String): String {
    val line = msg.lines().lastOrNull { it.contains("ERROR", true) } ?: msg.lines().firstOrNull { it.isNotBlank() } ?: msg
    val m = line.substringAfter("ERROR:").trim()
    return when {
        m.contains("Unsupported URL", true) -> L.t("هذا الرابط غير مدعوم (لا يوجد فيه فيديو أو ملف يمكن تحميله)", "This link isn't supported (no video or file to download in it)")
        m.contains("login", true) || m.contains("sign in", true) || m.contains("private", true) -> L.t("هذا المحتوى خاص أو يحتاج تسجيل دخول", "This content is private or needs a login")
        m.contains("Unable to resolve host", true) || m.contains("Network", true) -> L.t("لا يوجد اتصال بالإنترنت", "No internet connection")
        else -> m.take(300)
    }
}

/** A file with no type in its name (or yt-dlp's "unknown_video"): named after what it really is. */
fun withRealType(f: File): File {
    val ext = f.extension.lowercase()
    if (ext.isNotEmpty() && ext != "unknown_video") return f
    val head = ByteArray(16)
    val n = f.inputStream().use { it.read(head) }
    fun at(i: Int, vararg sig: Int) = n >= i + sig.size && sig.indices.all { head[i + it].toInt() and 0xFF == sig[it] }
    val real = when {
        at(0, 0x4D, 0x5A) -> "exe"
        at(4, 0x66, 0x74, 0x79, 0x70) -> "mp4"
        at(0, 0x1A, 0x45, 0xDF, 0xA3) -> "mkv"
        at(0, 0x50, 0x4B, 3, 4) -> "zip"
        at(0, 0x52, 0x61, 0x72, 0x21) -> "rar"
        at(0, 0x25, 0x50, 0x44, 0x46) -> "pdf"
        at(0, 0x49, 0x44, 0x33) || at(0, 0xFF, 0xFB) -> "mp3"
        at(0, 0x89, 0x50, 0x4E, 0x47) -> "png"
        at(0, 0xFF, 0xD8, 0xFF) -> "jpg"
        else -> return f
    }
    val named = File(f.parentFile, f.nameWithoutExtension + "." + real)
    return if (f.renameTo(named)) named else f
}
