package dev.bluey.phone

import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import java.util.concurrent.CompletableFuture

/**
 * Newline-delimited JSON over TCP. Field names match the PC host.
 */
class DeskLink(
    private val onPacket: (JSONObject) -> Unit,
    private val onDropped: () -> Unit,
) {
    private val pending = ConcurrentHashMap<String, CompletableFuture<JSONObject>>()
    private val writeLock = Any()
    @Volatile private var socket: Socket? = null
    @Volatile private var output: OutputStream? = null
    @Volatile private var generation = 0

    val connected: Boolean get() = socket?.isConnected == true && socket?.isClosed == false

    fun connect(host: String, port: Int) {
        val ticket = synchronized(writeLock) {
            failPending()
            closeSocket()
            ++generation
        }
        val opened = Socket()
        try {
            opened.tcpNoDelay = true
            opened.connect(InetSocketAddress(host, port), 4000)
            synchronized(writeLock) {
                if (ticket != generation) throw IllegalStateException("Link closed.")
                socket = opened
                output = opened.getOutputStream()
            }
        } catch (ex: Exception) {
            try { opened.close() } catch (_: Exception) { }
            throw ex
        }
        Thread({ readLoop(opened.getInputStream(), ticket) }, "bluey-link").apply { isDaemon = true }.start()
    }

    fun send(command: String, text: String? = null, tool: String? = null, callId: String? = null, audio: String? = null) {
        val packet = JSONObject()
        packet.put("command", command)
        if (text != null) packet.put("text", text)
        if (tool != null) packet.put("tool", tool)
        if (callId != null) packet.put("callID", callId)
        if (audio != null) packet.put("audio", audio)
        write(packet)
    }

    fun request(command: String, text: String? = null, tool: String? = null, timeoutMs: Long = 120_000): JSONObject {
        val callId = UUID.randomUUID().toString()
        val future = CompletableFuture<JSONObject>()
        pending[callId] = future
        try {
            send(command, text, tool, callId)
            return future.get(timeoutMs, TimeUnit.MILLISECONDS)
        } catch (ex: TimeoutException) {
            pending.remove(callId)
            throw IllegalStateException("The PC did not answer.")
        } catch (ex: Exception) {
            pending.remove(callId)
            val cause = (ex as? java.util.concurrent.ExecutionException)?.cause ?: ex
            throw IllegalStateException(cause.message ?: "The PC link failed.")
        }
    }

    fun close() {
        synchronized(writeLock) {
            generation++
            failPending()
            closeSocket()
        }
    }

    private fun failPending() {
        pending.values.forEach { it.completeExceptionally(IllegalStateException("Link closed.")) }
        pending.clear()
    }

    private fun closeSocket() {
        try { socket?.close() } catch (_: Exception) { }
        socket = null
        output = null
    }

    private fun write(packet: JSONObject) {
        val bytes = (packet.toString() + "\n").toByteArray(Charsets.UTF_8)
        synchronized(writeLock) {
            val stream = output ?: throw IllegalStateException("Not connected to the PC.")
            stream.write(bytes)
            stream.flush()
        }
    }

    private fun readLoop(input: InputStream, ticket: Int) {
        val line = ByteArrayOutputStream()
        val chunk = ByteArray(16 * 1024)
        try {
            while (ticket == generation) {
                val read = input.read(chunk)
                if (read < 0) break
                for (index in 0 until read) {
                    val byte = chunk[index]
                    if (byte == '\n'.code.toByte()) {
                        if (line.size() > 0) dispatch(line.toByteArray())
                        line.reset()
                    } else {
                        line.write(byte.toInt())
                        if (line.size() > 8 * 1024 * 1024) line.reset()
                    }
                }
            }
        } catch (_: Exception) {
            // The PC closed the socket.
        } finally {
            if (ticket == generation) onDropped()
        }
    }

    private fun dispatch(bytes: ByteArray) {
        val packet = try {
            JSONObject(String(bytes, Charsets.UTF_8))
        } catch (_: Exception) {
            return
        }
        val callId = packet.optString("callID", "")
        val waiter = if (callId.isEmpty()) null else pending.remove(callId)
        if (waiter != null) waiter.complete(packet) else onPacket(packet)
    }
}
