package dev.bluey.phone

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.atomic.AtomicInteger
import kotlin.math.PI
import kotlin.math.sin

class Speaker {
    private val queued = AtomicInteger()
    private val queue = LinkedBlockingQueue<ByteArray>()
    private var track: AudioTrack? = null
    private var chirp: AudioTrack? = null
    private var thread: Thread? = null
    @Volatile private var running = false

    fun start() {
        if (running) return
        val min = AudioTrack.getMinBufferSize(RATE, AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT)
        track = buildTrack(maxOf(min, RATE)).also { it.play() }
        chirp = buildTrack(RATE / 2).also { it.play() }
        running = true
        thread = Thread({
            while (running) {
                val chunk = try {
                    queue.take()
                } catch (_: InterruptedException) {
                    break
                }
                if (chunk.isEmpty()) continue
                track?.write(chunk, 0, chunk.size)
                queued.addAndGet(-chunk.size)
            }
        }, "bluey-speaker").apply { isDaemon = true; start() }
    }

    fun enqueue(pcm: ByteArray) {
        if (pcm.isEmpty() || !running) return
        queued.addAndGet(pcm.size)
        queue.offer(pcm)
    }

    fun busy(): Boolean = queued.get() > RATE / 4

    fun clear() {
        queue.clear()
        queued.set(0)
        track?.pause()
        track?.flush()
        track?.play()
    }

    fun chirp(high: Boolean) {
        val tone = chirp ?: return
        val count = (RATE * 0.05).toInt()
        val frequency = if (high) 880.0 else 520.0
        val pcm = ByteArray(count * 2)
        for (index in 0 until count) {
            val envelope = 1.0 - index.toDouble() / count
            val sample = (sin(2 * PI * frequency * index / RATE) * 0.22 * envelope * 32767).toInt()
            pcm[index * 2] = (sample and 0xff).toByte()
            pcm[index * 2 + 1] = (sample shr 8 and 0xff).toByte()
        }
        tone.write(pcm, 0, pcm.size)
    }

    fun stop() {
        running = false
        thread?.interrupt()
        queue.clear()
        track?.release()
        chirp?.release()
        track = null
        chirp = null
    }

    private fun buildTrack(buffer: Int): AudioTrack = AudioTrack.Builder()
        .setAudioAttributes(
            AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_MEDIA)
                .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                .build()
        )
        .setAudioFormat(
            AudioFormat.Builder()
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                .setSampleRate(RATE)
                .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                .build()
        )
        .setBufferSizeInBytes(buffer)
        .setTransferMode(AudioTrack.MODE_STREAM)
        .build()

    companion object {
        const val RATE = 24000

        fun toRate(pcm: ByteArray, fromRate: Int): ByteArray = when (fromRate) {
            RATE -> pcm
            48000 -> dropEveryOther(pcm)
            16000 -> upsample16to24(pcm)
            else -> pcm
        }

        private fun dropEveryOther(pcm: ByteArray): ByteArray {
            val samples = pcm.size / 4
            val out = ByteArray(samples * 2)
            for (index in 0 until samples) {
                out[index * 2] = pcm[index * 4]
                out[index * 2 + 1] = pcm[index * 4 + 1]
            }
            return out
        }

        private fun upsample16to24(pcm: ByteArray): ByteArray {
            val samples = pcm.size / 2
            if (samples == 0) return ByteArray(0)
            val outSamples = samples * 3 / 2
            val out = ByteArray(outSamples * 2)
            var written = 0
            for (index in 0 until samples) {
                val sample = (pcm[index * 2].toInt() and 0xff) or (pcm[index * 2 + 1].toInt() shl 8)
                writeSample(out, written, sample)
                written++
                if (index % 2 == 1 && written < outSamples) {
                    val previous = (pcm[(index - 1) * 2].toInt() and 0xff) or (pcm[(index - 1) * 2 + 1].toInt() shl 8)
                    writeSample(out, written, (previous + sample) / 2)
                    written++
                }
            }
            return out
        }

        private fun writeSample(out: ByteArray, index: Int, sample: Int) {
            out[index * 2] = (sample and 0xff).toByte()
            out[index * 2 + 1] = (sample shr 8 and 0xff).toByte()
        }
    }
}
