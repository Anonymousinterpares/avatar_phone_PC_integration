package dev.bluey.phone

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import java.util.concurrent.LinkedBlockingQueue

class DesktopAudioPlayer {
    private var track: AudioTrack? = null
    private var currentRate: Int = 0
    private var currentChannels: Int = 0
    private val queue = LinkedBlockingQueue<ByteArray>()
    private var thread: Thread? = null
    @Volatile private var running = false

    @Synchronized
    fun play(pcm: ByteArray, sampleRate: Int, channels: Int) {
        if (pcm.isEmpty()) return
        if (sampleRate != currentRate || channels != currentChannels || track == null) {
            reinit(sampleRate, channels)
        }
        // Keep queue small (max ~200ms) to prevent audio drift or latency buildup
        while (queue.size > 5) {
            queue.poll()
        }
        queue.offer(pcm)
    }

    @Synchronized
    private fun reinit(sampleRate: Int, channels: Int) {
        stop()
        currentRate = sampleRate
        currentChannels = channels

        val channelConfig = if (channels == 2) AudioFormat.CHANNEL_OUT_STEREO else AudioFormat.CHANNEL_OUT_MONO
        val minBuf = AudioTrack.getMinBufferSize(sampleRate, channelConfig, AudioFormat.ENCODING_PCM_16BIT)
        val bufSize = maxOf(minBuf, sampleRate * channels * 2 / 10) // ~100ms buffer

        track = AudioTrack.Builder()
            .setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                    .build()
            )
            .setAudioFormat(
                AudioFormat.Builder()
                    .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .setSampleRate(sampleRate)
                    .setChannelMask(channelConfig)
                    .build()
            )
            .setBufferSizeInBytes(bufSize)
            .setTransferMode(AudioTrack.MODE_STREAM)
            .build().also {
                try { it.play() } catch (_: Exception) { }
            }

        running = true
        thread = Thread({
            while (running) {
                val chunk = try {
                    queue.take()
                } catch (_: InterruptedException) {
                    break
                }
                if (chunk.isNotEmpty() && running) {
                    track?.write(chunk, 0, chunk.size)
                }
            }
        }, "desktop-audio-player").apply {
            isDaemon = true
            priority = Thread.MAX_PRIORITY
            start()
        }
    }

    @Synchronized
    fun stop() {
        running = false
        thread?.interrupt()
        thread = null
        queue.clear()
        try {
            track?.stop()
            track?.release()
        } catch (_: Exception) { }
        track = null
        currentRate = 0
        currentChannels = 0
    }
}
