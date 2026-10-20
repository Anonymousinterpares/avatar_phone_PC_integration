package dev.bluey.phone

import android.Manifest
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.content.pm.PackageManager
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.ContextCompat
import org.json.JSONObject
import java.util.concurrent.Executors

class MainActivity : ComponentActivity() {
    private val work = Executors.newCachedThreadPool()
    private val main = Handler(Looper.getMainLooper())
    private val speaker = Speaker()
    private val deskAudio = DesktopAudioPlayer()
    private lateinit var link: DeskLink
    private lateinit var live: LiveVoice
    private lateinit var plan: PlanSpeech
    private lateinit var finder: Finder
    private var mic: MicService? = null
    private var bound = false
    @Volatile private var realtime = false
    @Volatile private var holding = false
    @Volatile private var asking = false
    @Volatile private var provider = ""
    @Volatile private var route = ""
    @Volatile private var speechFailed = false
    @Volatile private var connecting = false
    private var captionGeneration = 0
    private var wakeAfterPermission = false
    private var ui by mutableStateOf(Ui())
    private var sessionPassword: String? = null

    private val permissions = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        if (result[Manifest.permission.RECORD_AUDIO] != true) {
            ui = ui.copy(status = "Bluey needs the microphone.")
            return@registerForActivityResult
        }
        if (wakeAfterPermission) {
            wakeAfterPermission = false
            wake()
        }
    }

    private val micConnection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, service: IBinder?) {
            bound = true
            mic = (service as MicService.Local).service
            mic?.onPcm = { pcm -> if (realtime && holding) live.appendPcm(pcm) }
            if (realtime) {
                try {
                    mic?.setCapturing(true)
                } catch (ex: Exception) {
                    ui = ui.copy(status = ex.message ?: "This phone could not open the microphone.")
                }
            }
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            bound = false
            mic = null
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.P) {
            window.attributes.layoutInDisplayCutoutMode =
                WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
        }
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        androidx.core.view.WindowCompat.setDecorFitsSystemWindows(window, false)
        setImmersiveMode(true)

        link = DeskLink(
            onPacket = { packet -> main.post { onPacket(packet) } },
            onDropped = { main.post { onDropped() } },
        )
        live = LiveVoice(
            link = link,
            speaker = speaker,
            work = work,
            onCaption = { text, done ->
                main.post {
                    ui = ui.copy(
                        caption = text,
                        talking = !done && text.isNotEmpty(),
                        mood = if (done) "listening" else "talking",
                    )
                    if (done) armCaptionExpiry() else captionGeneration++
                }
            },
            onStatus = { text -> main.post { ui = ui.copy(status = text) } },
        )
        plan = PlanSpeech(this)
        finder = Finder(this) { name, host, port -> main.post { addHost(name, host, port) } }
        speaker.start()
        volumeControlStream = android.media.AudioManager.STREAM_MUSIC
        val prefs = getSharedPreferences("bluey", MODE_PRIVATE)
        ui = ui.copy(
            address = prefs.getString("address", "").orEmpty(),
            deskRememberPassword = prefs.getBoolean("desk_remember_password", false),
        )
        setContent {
            if (ui.deskMode) {
                androidx.activity.compose.BackHandler { stopDesktop() }
                if (ui.deskNeedsPassword && !ui.deskAuthenticated) {
                    val sp = getSharedPreferences("bluey", MODE_PRIVATE)
                    val initialPass = if (sp.getBoolean("desk_remember_password", false)) {
                        sp.getString("desk_password", "").orEmpty()
                    } else {
                        sessionPassword.orEmpty()
                    }
                    val initialRemember = sp.getBoolean("desk_remember_password", true)

                    DesktopPasswordScreen(
                        initialPassword = initialPass,
                        initialRemember = initialRemember,
                        onSubmit = { pass, remember -> submitDesktopPassword(pass, remember) },
                        onBack = { stopDesktop() },
                    )
                } else {
                    DesktopViewScreen(
                        frame = ui.deskFrame,
                        cropFrame = ui.deskCropFrame,
                        cropRect = ui.deskCropRect,
                        fps = ui.deskFps,
                        quality = ui.deskQuality,
                        scale = ui.deskScale,
                        grayscale = ui.deskGrayscale,
                        audioEnabled = ui.deskAudioEnabled,
                        audioQuality = ui.deskAudioQuality,
                        mutePc = ui.deskMutePc,
                        onSettingsChanged = { f, q, s, g -> sendDesktopSettings(f, q, s, g) },
                        onAudioSettingsChanged = { en, q, m -> sendDesktopAudioSettings(en, q, m) },
                        onTouch = { action, x, y, extra -> sendDesktopInput(action, x, y, extra) },
                        onCropChanged = { cx, cy, cw, ch -> sendDesktopCrop(cx, cy, cw, ch) },
                        onKeyboard = { },
                        onBack = { stopDesktop() },
                        onToggleOrientation = { toggleOrientation() },
                    )
                }
            } else {
                BlueyScreen(
                    ui = ui,
                    onAddress = { ui = ui.copy(address = it) },
                    onConnect = { connectTyped() },
                    onPick = { host ->
                        ui = ui.copy(address = "${host.host}:${host.port}")
                        connect(host.host, host.port, force = true)
                    },
                    onDoubleTap = { if (ui.awake) sleep() else wake() },
                    onHoldStart = { holdStart() },
                    onHoldEnd = { holdEnd() },
                    onType = { sendTyped(it) },
                    onDesktop = { startDesktop() },
                )
            }
        }
        finder.start()
    }

    override fun onDestroy() {
        finder.stop()
        live.close()
        plan.destroy()
        speaker.stop()
        deskAudio.stop()
        stopMic()
        link.close()
        work.shutdownNow()
        super.onDestroy()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            setImmersiveMode(true)
        }
    }

    private fun setImmersiveMode(enabled: Boolean = true) {
        androidx.core.view.WindowCompat.setDecorFitsSystemWindows(window, !enabled)
        val controller = androidx.core.view.WindowCompat.getInsetsController(window, window.decorView)
        if (enabled) {
            controller.systemBarsBehavior =
                androidx.core.view.WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            controller.hide(androidx.core.view.WindowInsetsCompat.Type.systemBars())
        } else {
            controller.show(androidx.core.view.WindowInsetsCompat.Type.systemBars())
        }
    }

    private fun toggleOrientation() {
        val isLandscape = resources.configuration.orientation == android.content.res.Configuration.ORIENTATION_LANDSCAPE
        requestedOrientation = if (isLandscape) {
            android.content.pm.ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
        } else {
            android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        }
    }

    private fun connectTyped() {
        val parsed = parseAddress(ui.address)
        if (parsed == null) ui = ui.copy(status = "Type the address as host:port. The PC tray can show it.")
        else connect(parsed.first, parsed.second, force = true)
    }

    private fun connect(host: String, port: Int, force: Boolean) {
        if (!force && (connecting || link.connected)) return
        connecting = true
        holding = false
        asking = false
        speechFailed = false
        captionGeneration++
        live.close()
        plan.cancel()
        stopMic()
        ui = ui.copy(status = "Connecting to $host:$port…", awake = false, talking = false)
        work.execute {
            try {
                link.connect(host, port)
                getSharedPreferences("bluey", MODE_PRIVATE).edit().putString("address", "$host:$port").apply()
                main.post {
                    connecting = false
                    ui = ui.copy(status = "Connected. Double-tap the face to wake him.", address = "$host:$port", awake = false, connected = true)
                }
            } catch (ex: Exception) {
                main.post {
                    connecting = false
                    ui = ui.copy(status = ex.message ?: "Could not reach the PC.")
                }
            }
        }
    }

    private fun addHost(name: String, host: String, port: Int) {
        val found = Host(name, host, port)
        ui = ui.copy(hosts = (listOf(found) + ui.hosts.filter { it.host != host || it.port != port }).take(4))
        if (!link.connected) connect(host, port, force = false)
    }

    private fun wake() {
        if (!link.connected) {
            ui = ui.copy(status = "Connect to the PC first. It has to be running in the tray.")
            return
        }
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            wakeAfterPermission = true
            val asks = mutableListOf(Manifest.permission.RECORD_AUDIO)
            if (android.os.Build.VERSION.SDK_INT >= 33) asks.add(Manifest.permission.POST_NOTIFICATIONS)
            permissions.launch(asks.toTypedArray())
            return
        }
        work.execute {
            try {
                val reply = link.request("awake", timeoutMs = 15_000)
                val session = JSONObject(reply.optString("text", "{}"))
                main.post {
                    ui = ui.copy(awake = true, mood = "listening", caption = "", micOn = false, micLevel = 0f)
                    applySession(session)
                }
            } catch (ex: Exception) {
                main.post { ui = ui.copy(status = ex.message ?: "Wake failed.") }
            }
        }
    }

    private fun sleep() {
        holding = false
        asking = false
        speechFailed = false
        mic?.holding = false
        live.close()
        plan.cancel()
        stopMic()
        captionGeneration++
        ui = ui.copy(awake = false, talking = false, mood = "sleepy", caption = "", micOn = false, micLevel = 0f)
        work.execute { try { link.send("asleep") } catch (_: Exception) { } }
    }

    private fun holdStart() {
        if (!ui.awake || holding || asking) return
        holding = true
        speechFailed = false
        captionGeneration++
        speaker.chirp(true)
        ui = ui.copy(
            mood = "listening",
            talking = false,
            micOn = true,
            micLevel = 0.2f,
            caption = "Mic is on. Listening…",
            status = "Mic is on. Speak, then let go.",
        )
        if (realtime) {
            mic?.holding = true
            if (!live.isReady()) {
                ui = ui.copy(status = "Mic is on, but live voice is not connected yet.")
            }
            return
        }
        mic?.setCapturing(false)
        plan.start(object : PlanSpeech.Listener {
            override fun onReady() {
                if (!holding) return
                ui = ui.copy(micOn = true, status = "Mic is on. I can hear you. Speak, then let go.")
            }

            override fun onLevel(level: Float) {
                if (holding) ui = ui.copy(micLevel = level)
            }

            override fun onPartial(text: String) {
                if (speechFailed) return
                ui = ui.copy(caption = text, status = "Hearing: $text", micOn = holding)
                work.execute { try { link.send("caption", text) } catch (_: Exception) { } }
            }

            override fun onFinal(text: String) {
                deliverSpeech(text)
            }

            override fun onFailure(message: String) {
                speechFailed = true
                ui = ui.copy(
                    micOn = false,
                    micLevel = 0f,
                    talking = false,
                    mood = "listening",
                    caption = message,
                    status = message,
                )
                armCaptionExpiry()
            }
        })
    }

    private fun holdEnd() {
        if (!holding) return
        holding = false
        speaker.chirp(false)
        ui = ui.copy(micOn = false, micLevel = 0f)
        if (realtime) {
            mic?.holding = false
            if (live.isReady()) {
                live.release()
                ui = ui.copy(mood = "thinking", status = "Sent your voice. Waiting for the answer…")
            } else {
                ui = ui.copy(
                    mood = "listening",
                    status = "Mic was on, but live voice is not connected. Choose Cerebras in the PC tray, save, then double-tap the face.",
                )
            }
            return
        }
        if (speechFailed) {
            speechFailed = false
            plan.cancel()
            return
        }
        ui = ui.copy(mood = "thinking", status = "Mic off. Reading what you said…")
        plan.stop()
    }

    private fun sendTyped(text: String) {
        val said = text.trim()
        if (!ui.awake || said.isEmpty()) return
        if (asking) {
            ui = ui.copy(status = "Wait for this answer, then send the next line.")
            return
        }
        if (holding) {
            holding = false
            speechFailed = false
            mic?.holding = false
            plan.cancel()
            speaker.chirp(false)
            ui = ui.copy(micOn = false, micLevel = 0f)
        }
        deliverSpeech(said)
    }

    private fun deliverSpeech(text: String) {
        if (speechFailed) return
        val said = text.trim()
        if (said.isEmpty()) {
            val message = "Mic was on, but I didn't understand any words. Hold the face and speak a full sentence."
            ui = ui.copy(micOn = false, micLevel = 0f, talking = false, mood = "listening", caption = message, status = message)
            armCaptionExpiry()
            return
        }
        asking = true
        ui = ui.copy(
            caption = said,
            mood = "thinking",
            talking = false,
            micOn = false,
            micLevel = 0f,
            status = "Heard you. Asking ${engineName()}…",
        )
        work.execute {
            try {
                link.request("ask", text = said, timeoutMs = 180_000)
                main.post {
                    asking = false
                    ui = ui.copy(
                        talking = false,
                        mood = if (ui.awake) "listening" else ui.mood,
                        status = "${engineName()} answered.",
                    )
                }
            } catch (ex: Exception) {
                main.post {
                    asking = false
                    val message = ex.message ?: "The ask failed."
                    ui = ui.copy(status = message, caption = message, talking = false, mood = "listening")
                    armCaptionExpiry()
                }
            }
        }
    }

    private fun armCaptionExpiry() {
        val ticket = ++captionGeneration
        main.postDelayed({
            if (ticket != captionGeneration || holding) return@postDelayed
            ui = ui.copy(
                caption = "",
                talking = false,
                mood = if (ui.awake) "listening" else ui.mood,
                status = if (ui.awake) statusLine() else ui.status,
            )
        }, 12_000)
    }

    private fun engineName(): String = when (provider) {
        "cerebras" -> "Cerebras"
        "openrouter" -> "OpenRouter"
        "chatgpt", "openai" -> "ChatGPT"
        "anthropic" -> "Claude"
        "xai" -> "Grok"
        else -> "the PC"
    }

    private fun statusLine(): String = when {
        realtime && provider == "openai" -> "Hold the face. This phone streams your voice to OpenAI."
        realtime && provider == "xai" -> "Hold the face. This phone streams your voice to Grok."
        realtime -> "Hold the face to talk."
        provider == "cerebras" -> "Hold the face. This phone turns speech into text, then Cerebras answers."
        provider == "openrouter" -> {
            val who = if (route.isBlank()) "OpenRouter" else "OpenRouter ($route)"
            "Hold the face. This phone turns speech into text, then $who answers."
        }
        provider == "chatgpt" -> "Hold the face. This phone turns speech into text, then ChatGPT answers."
        provider == "anthropic" -> "Hold the face. This phone turns speech into text, then Claude answers."
        else -> "Hold the face. This phone turns speech into text, then the PC answers."
    }

    private fun openLive() {
        work.execute {
            try {
                val reply = link.request("realtimeToken", timeoutMs = 30_000)
                val secret = JSONObject(reply.optString("text", "{}"))
                main.post {
                    if (ui.awake) live.open(secret)
                }
            } catch (ex: Exception) {
                main.post { ui = ui.copy(status = ex.message ?: "Live voice did not start.") }
            }
        }
    }

    private fun applySession(session: JSONObject) {
        val wasLive = realtime
        realtime = session.optBoolean("realtime")
        provider = session.optString("provider", "")
        route = session.optString("route", "")
        if (!ui.awake) return
        if (realtime) startMic() else stopMic()
        ui = ui.copy(status = statusLine())
        if (realtime) openLive() else if (wasLive) live.close()
    }

    private fun onPacket(packet: JSONObject) {
        packet.optJSONObject("face")?.let { face ->
            val remote = face.optString("mood", ui.mood)
            val keep = ui.micOn || ui.talking || ui.mood == "thinking" || ui.mood == "talking" || ui.mood == "happy"
            ui = ui.copy(
                gazeX = face.optDouble("gazeX", 0.0).toFloat(),
                gazeY = face.optDouble("gazeY", 0.0).toFloat(),
                mood = if (keep) ui.mood else remote,
            )
        }
        when (packet.optString("command")) {
            "hello" -> if (!ui.awake) ui = ui.copy(status = "Connected to ${packet.optString("text", "the PC")}. Double-tap to wake.")
            "wake" -> if (!ui.awake) wake()
            "sleep" -> if (ui.awake) sleep()
            "stop" -> {
                live.cancel()
                ui = ui.copy(talking = false, caption = "Stopped", mood = "resting")
                armCaptionExpiry()
            }
            "session" -> applySession(JSONObject(packet.optString("text", "{}")))
            "caption" -> {
                captionGeneration++
                ui = ui.copy(caption = packet.optString("text"), talking = true, mood = "talking")
            }
            "captionDone" -> {
                ui = ui.copy(
                    caption = packet.optString("text"),
                    talking = false,
                    mood = if (ui.awake) "listening" else "resting",
                )
                armCaptionExpiry()
            }
            "deskFrame" -> {
                val imageData = packet.optString("image", "")
                if (imageData.isNotEmpty()) {
                    try {
                        val bytes = android.util.Base64.decode(imageData, android.util.Base64.DEFAULT)
                        val bitmap = android.graphics.BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                        if (bitmap != null) {
                            val textStr = packet.optString("text", "{}")
                            var cropX = 0f
                            var cropY = 0f
                            var cropW = 1f
                            var cropH = 1f
                            try {
                                val doc = JSONObject(textStr)
                                cropX = doc.optDouble("cropX", 0.0).toFloat()
                                cropY = doc.optDouble("cropY", 0.0).toFloat()
                                cropW = doc.optDouble("cropW", 1.0).toFloat()
                                cropH = doc.optDouble("cropH", 1.0).toFloat()
                            } catch (_: Exception) { }

                            if (cropW < 0.98f || cropH < 0.98f) {
                                ui = ui.copy(
                                    deskCropFrame = bitmap,
                                    deskCropRect = androidx.compose.ui.geometry.Rect(cropX, cropY, cropW, cropH),
                                )
                            } else {
                                ui = ui.copy(deskFrame = bitmap, deskCropFrame = null, deskCropRect = null)
                            }
                        }
                    } catch (_: Exception) { }
                }
            }
            "deskAudio" -> {
                if (ui.deskMode && ui.deskAudioEnabled) {
                    val audioData = packet.optString("audio", "")
                    if (audioData.isNotEmpty()) {
                        try {
                            val pcm = android.util.Base64.decode(audioData, android.util.Base64.DEFAULT)
                            val textStr = packet.optString("text", "{}")
                            var rate = 24000
                            var ch = 1
                            try {
                                val doc = JSONObject(textStr)
                                rate = doc.optInt("rate", 24000)
                                ch = doc.optInt("ch", 1)
                            } catch (_: Exception) { }
                            deskAudio.play(pcm, rate, ch)
                        } catch (_: Exception) { }
                    }
                }
            }
        }
    }

    private fun onDropped() {
        live.close()
        plan.cancel()
        stopMic()
        deskAudio.stop()
        holding = false
        asking = false
        speechFailed = false
        captionGeneration++
        requestedOrientation = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
        setImmersiveMode(true)
        ui = ui.copy(
            status = "The PC connection dropped.",
            awake = false,
            talking = false,
            mood = "sleepy",
            micOn = false,
            micLevel = 0f,
            connected = false,
            deskMode = false,
            deskFrame = null,
            deskCropFrame = null,
            deskCropRect = null,
            deskAuthenticated = false,
            deskNeedsPassword = false,
        )
    }

    private fun startDesktop() {
        if (!link.connected) {
            ui = ui.copy(status = "Connect to the PC first.")
            return
        }
        work.execute {
            try {
                val prefs = getSharedPreferences("bluey", MODE_PRIVATE)
                val testPassword = sessionPassword
                    ?: (if (prefs.getBoolean("desk_remember_password", false)) prefs.getString("desk_password", "") else null)
                    ?: ""

                val authReply = link.request("deskAuth", text = testPassword, timeoutMs = 10_000)
                val result = authReply.optString("text", "")
                main.post {
                    if (result == "ok") {
                        if (testPassword.isNotEmpty()) {
                            sessionPassword = testPassword
                        }
                        ui = ui.copy(deskMode = true, deskAuthenticated = true, deskNeedsPassword = false)
                        setImmersiveMode(true)
                        requestedOrientation = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                        requestDesktopStream()
                        sendDesktopAudioSettings(ui.deskAudioEnabled, ui.deskAudioQuality, ui.deskMutePc)
                    } else {
                        ui = ui.copy(deskMode = true, deskNeedsPassword = true, deskAuthenticated = false)
                    }
                }
            } catch (ex: Exception) {
                main.post { ui = ui.copy(status = ex.message ?: "Desktop auth failed.") }
            }
        }
    }

    private fun submitDesktopPassword(password: String, remember: Boolean) {
        work.execute {
            try {
                val reply = link.request("deskAuth", text = password, timeoutMs = 10_000)
                val result = reply.optString("text", "")
                main.post {
                    if (result == "ok") {
                        sessionPassword = password
                        val prefs = getSharedPreferences("bluey", MODE_PRIVATE).edit()
                        if (remember) {
                            prefs.putString("desk_password", password)
                            prefs.putBoolean("desk_remember_password", true)
                        } else {
                            prefs.remove("desk_password")
                            prefs.putBoolean("desk_remember_password", false)
                        }
                        prefs.apply()

                        ui = ui.copy(deskAuthenticated = true, deskNeedsPassword = false)
                        setImmersiveMode(true)
                        requestedOrientation = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                        requestDesktopStream()
                        sendDesktopAudioSettings(ui.deskAudioEnabled, ui.deskAudioQuality, ui.deskMutePc)
                    } else {
                        ui = ui.copy(status = "Wrong password.")
                    }
                }
            } catch (ex: Exception) {
                main.post { ui = ui.copy(status = ex.message ?: "Auth failed.") }
            }
        }
    }

    private fun requestDesktopStream() {
        work.execute {
            try {
                val settings = JSONObject()
                settings.put("fps", ui.deskFps)
                settings.put("quality", ui.deskQuality)
                settings.put("scale", ui.deskScale.toDouble())
                settings.put("grayscale", ui.deskGrayscale)
                link.request("deskStart", text = settings.toString(), timeoutMs = 10_000)
            } catch (ex: Exception) {
                main.post { ui = ui.copy(status = ex.message ?: "Could not start desktop.") }
            }
        }
    }

    private fun stopDesktop() {
        deskAudio.stop()
        work.execute {
            try { link.request("deskAudioStop", timeoutMs = 3_000) } catch (_: Exception) { }
        }
        work.execute {
            try { link.request("deskStop", timeoutMs = 5_000) } catch (_: Exception) { }
        }
        requestedOrientation = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
        setImmersiveMode(true)
        ui = ui.copy(
            deskMode = false,
            deskFrame = null,
            deskCropFrame = null,
            deskCropRect = null,
            deskAuthenticated = false,
            deskNeedsPassword = false,
        )
    }

    private fun sendDesktopAudioSettings(enabled: Boolean, quality: String, mutePc: Boolean) {
        ui = ui.copy(deskAudioEnabled = enabled, deskAudioQuality = quality, deskMutePc = mutePc)
        if (!enabled) {
            deskAudio.stop()
        }
        work.execute {
            try {
                val settings = JSONObject()
                settings.put("enabled", enabled)
                val rate = when (quality) {
                    "high" -> 44100
                    "low" -> 16000
                    else -> 24000
                }
                val ch = if (quality == "high") 2 else 1
                settings.put("rate", rate)
                settings.put("ch", ch)
                settings.put("mutePc", mutePc)
                link.send("deskAudioSettings", settings.toString())
            } catch (_: Exception) { }
        }
    }

    private fun sendDesktopCrop(cropX: Float, cropY: Float, cropW: Float, cropH: Float) {
        work.execute {
            try {
                val crop = JSONObject()
                crop.put("cropX", cropX.toDouble())
                crop.put("cropY", cropY.toDouble())
                crop.put("cropW", cropW.toDouble())
                crop.put("cropH", cropH.toDouble())
                link.send("deskCrop", crop.toString())
            } catch (_: Exception) { }
        }
    }

    private fun sendDesktopSettings(fps: Int, quality: Int, scale: Float, grayscale: Boolean) {
        ui = ui.copy(deskFps = fps, deskQuality = quality, deskScale = scale, deskGrayscale = grayscale)
        work.execute {
            try {
                val settings = JSONObject()
                settings.put("fps", fps)
                settings.put("quality", quality)
                settings.put("scale", scale.toDouble())
                settings.put("grayscale", grayscale)
                link.send("deskSettings", settings.toString())
            } catch (_: Exception) { }
        }
    }

    private fun sendDesktopInput(action: String, x: Float, y: Float, extra: Map<String, Any>) {
        work.execute {
            try {
                val input = JSONObject()
                input.put("action", action)
                input.put("x", x.toDouble())
                input.put("y", y.toDouble())
                for ((k, v) in extra) input.put(k, v)
                link.send("deskInput", input.toString())
            } catch (_: Exception) { }
        }
    }

    private fun startMic() {
        if (bound) return
        val intent = Intent(this, MicService::class.java)
        ContextCompat.startForegroundService(this, intent)
        bindService(intent, micConnection, Context.BIND_AUTO_CREATE)
    }

    private fun stopMic() {
        mic?.holding = false
        if (bound) {
            try { unbindService(micConnection) } catch (_: Exception) { }
            bound = false
        }
        stopService(Intent(this, MicService::class.java))
        mic = null
    }

    private fun parseAddress(raw: String): Pair<String, Int>? {
        val text = raw.trim()
        val colon = text.lastIndexOf(':')
        if (colon <= 0) return null
        val host = text.substring(0, colon).trim()
        val port = text.substring(colon + 1).toIntOrNull() ?: return null
        if (host.isEmpty() || port !in 1..65535) return null
        return host to port
    }
}
