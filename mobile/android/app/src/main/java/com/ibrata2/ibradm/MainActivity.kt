package com.ibrata2.ibradm

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.ClipboardManager
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup.LayoutParams.MATCH_PARENT
import android.view.ViewGroup.LayoutParams.WRAP_CONTENT
import android.view.inputmethod.EditorInfo
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import kotlin.concurrent.thread

/** One screen: paste (or share) a link, pick the quality, download. The list below shows every download. */
class MainActivity : Activity() {
    private val bg = Color.parseColor("#0E0E11")
    private val card = Color.parseColor("#1A1A1F")
    private val line = Color.parseColor("#2C2C33")
    private val ink = Color.parseColor("#F2F2F4")
    private val muted = Color.parseColor("#A0A0AA")
    private val red = Color.parseColor("#D7141F")
    private val green = Color.parseColor("#4CC26B")

    private lateinit var link: EditText
    private lateinit var list: LinearLayout
    private lateinit var updateBar: TextView
    private var mode = "best"
    private val modeButtons = mutableListOf<Pair<String, TextView>>()
    private val refresh = { render() }

    private fun dp(v: Int) = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v.toFloat(), resources.displayMetrics).toInt()
    private fun round(color: Int, radius: Int = 14, stroke: Int? = null) = GradientDrawable().apply {
        setColor(color); cornerRadius = dp(radius).toFloat(); if (stroke != null) setStroke(dp(1), stroke)
    }
    private fun text(s: String, size: Float = 14f, color: Int = ink, bold: Boolean = false) = TextView(this).apply {
        text = s; setTextColor(color); textSize = size; if (bold) typeface = Typeface.DEFAULT_BOLD
    }
    private fun button(s: String, color: Int = card, fg: Int = ink, click: () -> Unit) = TextView(this).apply {
        text = s; setTextColor(fg); textSize = 14f; typeface = Typeface.DEFAULT_BOLD; gravity = Gravity.CENTER
        setPadding(dp(14), dp(10), dp(14), dp(10)); background = round(color, 12, if (color == card) line else null)
        isClickable = true; setOnClickListener { click() }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.decorView.layoutDirection = if (L.ar) View.LAYOUT_DIRECTION_RTL else View.LAYOUT_DIRECTION_LTR
        val root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setBackgroundColor(bg); setPadding(dp(16), dp(14), dp(16), 0) }

        // header: logo, name, "about"
        val head = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        head.addView(ImageView(this).apply { setImageResource(R.drawable.logo) }, LinearLayout.LayoutParams(dp(42), dp(42)))
        val names = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(10), 0, dp(10), 0) }
        names.addView(text("IBRA Download Manager", 18f, ink, true))
        names.addView(text(L.t("برمجة و تطوير ابراهيم الخزرجي", "Programmed and developed by Ibrahim Al-Khazraji"), 11.5f, muted))
        head.addView(names, LinearLayout.LayoutParams(0, WRAP_CONTENT, 1f))
        head.addView(button("ⓘ") { about() })
        root.addView(head)

        updateBar = button("", red, Color.WHITE) {}.apply { visibility = View.GONE }
        root.addView(updateBar, LinearLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT).apply { topMargin = dp(10) })

        // the link
        val box = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; background = round(card, 16, line); setPadding(dp(14), dp(12), dp(14), dp(14)) }
        box.addView(text(L.t("الصق الرابط", "Paste the link"), 13f, muted))
        val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        link = EditText(this).apply {
            hint = "https://…"; setHintTextColor(muted); setTextColor(ink); textSize = 14f; isSingleLine = true
            inputType = InputType.TYPE_TEXT_VARIATION_URI; imeOptions = EditorInfo.IME_ACTION_GO; textDirection = View.TEXT_DIRECTION_LTR
            background = round(bg, 10, line); setPadding(dp(10), dp(10), dp(10), dp(10))
            setOnEditorActionListener { _, _, _ -> download(); true }
        }
        row.addView(link, LinearLayout.LayoutParams(0, WRAP_CONTENT, 1f))
        row.addView(button(L.t("لصق", "Paste")) { paste(true) }, LinearLayout.LayoutParams(WRAP_CONTENT, WRAP_CONTENT).apply { marginStart = dp(8) })
        box.addView(row, LinearLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT).apply { topMargin = dp(6) })

        // the quality
        box.addView(text(L.t("الجودة", "Quality"), 13f, muted).apply { setPadding(0, dp(12), 0, dp(6)) })
        val modes = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        listOf("best" to L.t("أفضل جودة", "Best"), "1080" to "1080p", "720" to "720p", "480" to "480p", "mp3" to L.t("صوت MP3", "Audio MP3")).forEach { (key, label) ->
            val b = button(label) { mode = key; paintModes() }
            modeButtons.add(key to b)
            modes.addView(b, LinearLayout.LayoutParams(WRAP_CONTENT, WRAP_CONTENT).apply { marginEnd = dp(6) })
        }
        box.addView(HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false; addView(modes) })
        paintModes()
        box.addView(button(L.t("⬇  تحميل", "⬇  Download"), red, Color.WHITE) { download() }.apply { textSize = 16f },
            LinearLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT).apply { topMargin = dp(14) })
        box.addView(text(L.t("يوتيوب، انستغرام، تيك توك، فيسبوك، X وأي ملف مباشر. تقدر كذلك من أي تطبيق: مشاركة ← IBRA DM.",
            "YouTube, Instagram, TikTok, Facebook, X and any direct file. Or from any app: Share → IBRA DM."), 11.5f, muted).apply { setPadding(0, dp(10), 0, 0) })
        root.addView(box, LinearLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT).apply { topMargin = dp(12) })

        // the downloads
        val listHead = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL; setPadding(0, dp(16), 0, dp(8)) }
        listHead.addView(text(L.t("التحميلات", "Downloads"), 16f, ink, true), LinearLayout.LayoutParams(0, WRAP_CONTENT, 1f))
        listHead.addView(button(L.t("حذف المكتملة", "Clear finished")) { clearFinished() }.apply { textSize = 12f; setPadding(dp(10), dp(6), dp(10), dp(6)) })
        root.addView(listHead)
        list = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(0, 0, 0, dp(24)) }
        root.addView(ScrollView(this).apply { addView(list) }, LinearLayout.LayoutParams(MATCH_PARENT, 0, 1f))
        setContentView(root)

        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
        handle(intent)
        if (Downloads.jobs.any { it.status == Job.WAITING }) DownloadService.start(this)
        checkUpdate()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handle(intent)
    }

    override fun onResume() {
        super.onResume()
        Downloads.watch(refresh)
        render()
        if (link.text.isEmpty()) paste(false)
    }

    override fun onPause() {
        super.onPause()
        Downloads.unwatch(refresh)
    }

    /** A link shared from another app (YouTube "Share", the browser…) or opened from the test tools. */
    private fun handle(intent: Intent?) {
        val shared = intent?.getStringExtra(Intent.EXTRA_TEXT) ?: intent?.dataString ?: return
        val url = Regex("https?://\\S+").find(shared)?.value ?: return
        link.setText(url)
        intent.getStringExtra("mode")?.let { mode = it; paintModes() }
        if (intent.getBooleanExtra("start", false)) download()
    }

    private fun paste(always: Boolean) {
        val clip = (getSystemService(CLIPBOARD_SERVICE) as ClipboardManager).primaryClip
        val t = clip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(this)?.toString() ?: ""
        val url = Regex("https?://\\S+").find(t)?.value
        if (url != null) link.setText(url) else if (always) Toast.makeText(this, L.t("لا يوجد رابط بالحافظة", "No link on the clipboard"), Toast.LENGTH_SHORT).show()
    }

    private fun paintModes() {
        modeButtons.forEach { (key, b) ->
            val on = key == mode
            b.background = round(if (on) red else card, 12, if (on) null else line)
            b.setTextColor(if (on) Color.WHITE else ink)
        }
    }

    private fun download() {
        val url = Regex("https?://\\S+").find(link.text.toString())?.value
        if (url == null) { Toast.makeText(this, L.t("الصق رابطاً صحيحاً", "Paste a valid link"), Toast.LENGTH_SHORT).show(); return }
        Downloads.add(this, Job(url = url, mode = mode))
        link.setText("")
        DownloadService.start(this)
        Toast.makeText(this, L.t("بدأ التحميل", "Download started"), Toast.LENGTH_SHORT).show()
    }

    private fun clearFinished() {
        synchronized(Downloads.jobs) { Downloads.jobs.removeAll { !it.active } }
        Downloads.save(this)
        render()
    }

    private fun render() {
        list.removeAllViews()
        val jobs = synchronized(Downloads.jobs) { Downloads.jobs.toList() }
        if (jobs.isEmpty()) {
            list.addView(text(L.t("لا توجد تحميلات بعد.", "No downloads yet."), 13f, muted).apply { setPadding(0, dp(8), 0, 0) })
            return
        }
        for (j in jobs) list.addView(row(j), LinearLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT).apply { bottomMargin = dp(8) })
    }

    private fun row(j: Job): View {
        val v = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; background = round(card, 14, line); setPadding(dp(12), dp(10), dp(12), dp(10)) }
        val name = j.title.ifEmpty { j.url }
        v.addView(text(name, 14f, ink, true).apply { maxLines = 2; textDirection = View.TEXT_DIRECTION_FIRST_STRONG })
        val modeName = when (j.mode) { "mp3" -> "MP3"; "best" -> L.t("أفضل جودة", "Best"); else -> j.mode + "p" }
        val status = when (j.status) {
            Job.WAITING -> L.t("بالانتظار", "Waiting") to muted
            Job.RUNNING -> (j.detail.ifEmpty { L.t("جاري التحميل", "Downloading") }) to red
            Job.DONE -> (L.t("مكتمل", "Complete") + " — " + j.detail) to green
            Job.CANCELLED -> L.t("أُلغي", "Cancelled") to muted
            else -> (L.t("فشل: ", "Failed: ") + (j.error ?: "")) to red
        }
        v.addView(text("$modeName · ${status.first}", 12f, status.second).apply { setPadding(0, dp(4), 0, 0); textDirection = View.TEXT_DIRECTION_FIRST_STRONG })
        if (j.status == Job.RUNNING) {
            val bar = ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal).apply {
                max = 1000; progress = (j.progress * 10).toInt(); isIndeterminate = j.progress <= 0f
                progressTintList = android.content.res.ColorStateList.valueOf(red)
            }
            v.addView(bar, LinearLayout.LayoutParams(MATCH_PARENT, dp(10)).apply { topMargin = dp(6) })
        }
        val actions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; setPadding(0, dp(8), 0, 0) }
        fun act(s: String, c: () -> Unit) = actions.addView(button(s) { c() }.apply { textSize = 12f; setPadding(dp(12), dp(6), dp(12), dp(6)) },
            LinearLayout.LayoutParams(WRAP_CONTENT, WRAP_CONTENT).apply { marginEnd = dp(6) })
        when (j.status) {
            Job.DONE -> {
                act(L.t("فتح", "Open")) { open(j) }
                act(L.t("مشاركة", "Share")) { share(j) }
                act(L.t("إزالة", "Remove")) { Downloads.remove(this, j) }
            }
            Job.WAITING, Job.RUNNING -> act(L.t("إلغاء", "Cancel")) { DownloadService.cancel(this, j) }
            else -> {
                act(L.t("إعادة المحاولة", "Retry")) { j.status = Job.WAITING; j.error = null; Downloads.save(this); DownloadService.start(this); render() }
                act(L.t("إزالة", "Remove")) { Downloads.remove(this, j) }
            }
        }
        v.addView(actions)
        return v
    }

    private fun open(j: Job) {
        try {
            startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse(j.uri), j.mime).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
        } catch (e: Exception) {
            Toast.makeText(this, L.t("لا يوجد تطبيق يفتح هذا الملف", "No app can open this file"), Toast.LENGTH_SHORT).show()
        }
    }

    private fun share(j: Job) {
        val i = Intent(Intent.ACTION_SEND).setType(j.mime ?: "*/*").putExtra(Intent.EXTRA_STREAM, Uri.parse(j.uri)).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        startActivity(Intent.createChooser(i, L.t("مشاركة", "Share")))
    }

    private fun about() {
        val msg = L.t(
            "IBRA Download Manager ${BuildConfig.VERSION_NAME}\n\nبرمجة و تطوير ابراهيم الخزرجي\n\nالملفات تُحفظ في: التنزيلات / IBRA Download Manager\n\nتيليجرام: t.me/ibraprogram\nتيك توك: ibraprogram",
            "IBRA Download Manager ${BuildConfig.VERSION_NAME}\n\nProgrammed and developed by Ibrahim Al-Khazraji\n\nFiles are saved in: Downloads / IBRA Download Manager\n\nTelegram: t.me/ibraprogram\nTikTok: ibraprogram")
        AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
            .setTitle(L.t("حول", "About")).setMessage(msg)
            .setPositiveButton(L.t("تيليجرام", "Telegram")) { _, _ -> startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("https://t.me/ibraprogram"))) }
            .setNeutralButton(L.t("تيك توك", "TikTok")) { _, _ -> startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("https://www.tiktok.com/@ibraprogram"))) }
            .setNegativeButton(L.t("إغلاق", "Close"), null).show()
    }

    /** A newer version on GitHub: a red bar at the top opens the APK for this phone. */
    private fun checkUpdate() {
        thread {
            try {
                val c = URL("https://api.github.com/repos/ibrata2/ibdm-pro/releases/latest").openConnection() as HttpURLConnection
                c.setRequestProperty("Accept", "application/vnd.github+json")
                c.connectTimeout = 15000; c.readTimeout = 15000
                val json = JSONObject(c.inputStream.bufferedReader().readText())
                val latest = json.getString("tag_name").trimStart('v')
                if (!newer(latest, BuildConfig.VERSION_NAME)) return@thread
                val abi = if (Build.SUPPORTED_ABIS.firstOrNull() == "armeabi-v7a") "armv7" else "arm64"
                val assets = json.getJSONArray("assets")
                var apk: String? = null
                for (i in 0 until assets.length()) {
                    val a = assets.getJSONObject(i)
                    if (a.getString("name").endsWith("-$abi.apk")) apk = a.getString("browser_download_url")
                }
                val target = apk ?: json.getString("html_url")
                runOnUiThread {
                    updateBar.text = L.t("⬆  يتوفر إصدار جديد $latest — اضغط للتحميل", "⬆  Version $latest is available — tap to download")
                    updateBar.setOnClickListener { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(target))) }
                    updateBar.visibility = View.VISIBLE
                }
            } catch (_: Exception) { }
        }
    }

    private fun newer(a: String, b: String): Boolean {
        val x = a.split('.').map { it.toIntOrNull() ?: 0 }
        val y = b.split('.').map { it.toIntOrNull() ?: 0 }
        for (i in 0 until maxOf(x.size, y.size)) {
            val p = x.getOrElse(i) { 0 }; val q = y.getOrElse(i) { 0 }
            if (p != q) return p > q
        }
        return false
    }
}
