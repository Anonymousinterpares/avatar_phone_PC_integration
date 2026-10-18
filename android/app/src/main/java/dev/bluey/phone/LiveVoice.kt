package dev.bluey.phone

import android.util.Base64
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.util.ArrayDeque
import java.util.concurrent.Callable
import java.util.concurrent.ExecutorService
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Phone-side realtime socket. The PC mints the ephemeral token.
 * xAI needs session.update after connect. OpenAI bakes the session into the secret.
 */
class LiveVoice(
    private val link: DeskLink,
    private val speaker: Speaker,
    private val work: ExecutorService,
    private val onCaption: (text: String, done: Boolean) -> Unit,
    private val onStatus: (String) -> Unit,
) {
    private val http = OkHttpClient.Builder()
        .pingInterval(20, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .build()
    private var socket: WebSocket? = null
    private val ready = AtomicBoolean(false)
    private val configure = AtomicBoolean(false)
    private val audio = ArrayDeque<String>()
    private val caption = StringBuilder()
    private val calls = mutableListOf<java.util.concurrent.Future<Pair<String, String>>>()
    private val lock = Any()

    fun isReady(): Boolean = ready.get()

    fun open(secret: JSONObject) {
        close()
        val token = secret.optString("token")
        val url = secret.optString("url")
        if (secret.has("error") || token.isEmpty() || url.isEmpty()) {
            onStatus(secret.optString("error", "The PC could not open a live voice session."))
            return
        }
        configure.set(secret.optBoolean("configureSession"))
        val request = Request.Builder()
            .url(url)
            .header("Authorization", "Bearer $token")
            .build()
        socket = http.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                if (configure.get() && secret.has("session")) {
                    val update = JSONObject()
                    update.put("type", "session.update")
                    update.put("session", secret.getJSONObject("session"))
                    webSocket.send(update.toString())
                } else {
                    markReady(webSocket)
                }
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                handle(webSocket, text)
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                ready.set(false)
                onStatus(t.message ?: "The live voice connection failed.")
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                ready.set(false)
            }
        })
    }

    fun appendPcm(pcm: ByteArray) {
        if (pcm.isEmpty()) return
        val encoded = Base64.encodeToString(pcm, Base64.NO_WRAP)
        val current = socket
        if (ready.get() && current != null) sendAudio(current, encoded) else synchronized(lock) { audio.addLast(encoded) }
    }

    fun release() {
        val current = socket ?: return
        current.send(JSONObject().put("type", "input_audio_buffer.commit").toString())
        current.send(JSONObject().put("type", "response.create").toString())
    }

    fun cancel() {
        socket?.send(JSONObject().put("type", "response.cancel").toString())
        speaker.clear()
        synchronized(lock) {
            caption.setLength(0)
            calls.clear()
        }
    }

    fun close() {
        ready.set(false)
        synchronized(lock) { audio.clear() }
        socket?.close(1000, "bye")
        socket = null
        speaker.clear()
    }

    private fun handle(webSocket: WebSocket, text: String) {
        val event = try { JSONObject(text) } catch (_: Exception) { return }
        when (event.optString("type")) {
            "session.updated" -> markReady(webSocket)
            "response.output_audio.delta", "response.audio.delta" -> {
                val delta = event.optString("delta")
                if (delta.isNotEmpty()) speaker.enqueue(Base64.decode(delta, Base64.DEFAULT))
            }
            "response.output_audio_transcript.delta",
            "response.audio_transcript.delta",
            "response.output_text.delta" -> appendCaption(event.optString("delta"), done = false)
            "response.function_call_arguments.done" -> queueTool(event)
            "response.done" -> finishTurn(webSocket)
            "error" -> onStatus(event.optJSONObject("error")?.optString("message") ?: "The voice model reported an error.")
        }
    }

    private fun markReady(webSocket: WebSocket) {
        if (!ready.compareAndSet(false, true)) return
        val buffered = synchronized(lock) {
            val copy = audio.toList()
            audio.clear()
            copy
        }
        buffered.forEach { sendAudio(webSocket, it) }
    }

    private fun sendAudio(webSocket: WebSocket, encoded: String) {
        webSocket.send(JSONObject().put("type", "input_audio_buffer.append").put("audio", encoded).toString())
    }

    private fun appendCaption(delta: String, done: Boolean) {
        if (delta.isEmpty() && !done) return
        val text = synchronized(lock) {
            caption.append(delta)
            caption.toString()
        }
        onCaption(text, done)
        val command = if (done) "captionDone" else "caption"
        try { link.send(command, text) } catch (_: Exception) { }
    }

    private fun queueTool(event: JSONObject) {
        val name = event.optString("name")
        val callId = event.optString("call_id")
        val arguments = when (val raw = event.opt("arguments")) {
            is String -> if (raw.isBlank()) "{}" else raw
            is JSONObject -> raw.toString()
            else -> "{}"
        }
        if (name.isEmpty() || callId.isEmpty()) return
        val future = work.submit(Callable {
            val reply = try {
                link.request("tool", text = arguments, tool = name, timeoutMs = 90_000)
            } catch (ex: Exception) {
                return@Callable callId to (ex.message ?: "The tool failed.")
            }
            callId to reply.optString("text", "Done.")
        })
        synchronized(lock) { calls.add(future) }
    }

    private fun finishTurn(webSocket: WebSocket) {
        val pending = synchronized(lock) {
            val copy = calls.toList()
            calls.clear()
            copy
        }
        val text = synchronized(lock) { caption.toString().also { caption.setLength(0) } }
        if (text.isNotEmpty()) onCaption(text, true)
        work.execute {
            val outputs = pending.map { future ->
                try { future.get(90, TimeUnit.SECONDS) }
                catch (ex: Exception) { "" to (ex.message ?: "The tool failed.") }
            }.filter { it.first.isNotEmpty() }
            outputs.forEach { (callId, output) ->
                val item = JSONObject()
                    .put("type", "function_call_output")
                    .put("call_id", callId)
                    .put("output", output)
                webSocket.send(JSONObject().put("type", "conversation.item.create").put("item", item).toString())
            }
            if (outputs.isNotEmpty()) {
                val until = System.currentTimeMillis() + 8000
                while (speaker.busy() && System.currentTimeMillis() < until) {
                    try { Thread.sleep(40) } catch (_: InterruptedException) { break }
                }
                webSocket.send(JSONObject().put("type", "response.create").toString())
            }
            if (text.isNotEmpty()) {
                try { link.send("captionDone", text) } catch (_: Exception) { }
            }
        }
    }
}
