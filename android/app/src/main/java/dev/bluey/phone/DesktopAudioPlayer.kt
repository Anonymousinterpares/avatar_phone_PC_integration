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
    @Volatile private var paused = false

    @Synchronized
    fun play(pcm: ByteArray, sampleRate: Int, channels: Int) {
        if (pcm.isEmpty()) return
        if (sampleRate != currentRate || channels != currentChannels || track == null) {
            reinit(sampleRate, channels)
        }
        // Keep queue bounded to prevent latency buildup while absorbing network jitter
        while (queue.size > 10) {
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
        val safeMin = if (minBuf > 0) minBuf * 2 else 8192
        val bufSize = maxOf(safeMin, sampleRate * channels * 2 / 4) // ~250ms buffer

        try {
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
                    if (chunk.isNotEmpty() && running && !paused) {
                        val t = track
                        if (t != null) {
                            if (t.playState != AudioTrack.PLAYSTATE_PLAYING) {
                                try { t.play() } catch (_: Exception) { }
                            }
                            t.write(chunk, 0, chunk.size)
                        }
                    }
                }
            }, "desktop-audio-player").apply {
                isDaemon = true
                priority = Thread.MAX_PRIORITY
                start()
            }
        } catch (_: Exception) { }
    }

    fun setPaused(value: Boolean) {
        paused = value
        if (value) queue.clear()
    }

    @Synchronized
    fun stop() {
        paused = false
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

