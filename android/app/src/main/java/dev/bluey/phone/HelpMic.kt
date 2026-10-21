package dev.bluey.phone

import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder

/** 16 kHz mono PCM from the phone microphone, in short chunks. */
class HelpMic(private val onPcm: (ByteArray) -> Unit) {
    private var record: AudioRecord? = null
    @Volatile private var running = false
    private var thread: Thread? = null

    fun start() {
        stop()
        val min = AudioRecord.getMinBufferSize(RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
        if (min <= 0) return
        val opened = try {
            AudioRecord.Builder()
                .setAudioSource(MediaRecorder.AudioSource.VOICE_COMMUNICATION)
                .setAudioFormat(
                    AudioFormat.Builder()
                        .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                        .setSampleRate(RATE)
                        .setChannelMask(AudioFormat.CHANNEL_IN_MONO)
                        .build()
                )
                .setBufferSizeInBytes(min * 2)
                .build()
        } catch (_: Exception) {
            return
        }
        if (opened.state != AudioRecord.STATE_INITIALIZED) {
            opened.release()
            return
        }
        opened.startRecording()
        record = opened
        running = true
        thread = Thread({
            val buffer = ByteArray(1280)
            while (running) {
                val read = try {
                    opened.read(buffer, 0, buffer.size)
                } catch (_: Exception) {
                    break
                }
                if (read > 0 && running) onPcm(buffer.copyOf(read))
            }
        }, "bluey-help-mic").apply { isDaemon = true; start() }
    }

    fun stop() {
        running = false
        try { thread?.join(400) } catch (_: Exception) { }
        thread = null
        val opened = record
        record = null
        if (opened != null) {
            try { opened.stop() } catch (_: Exception) { }
            opened.release()
        }
    }

    private companion object {
        const val RATE = 16000
    }
}
