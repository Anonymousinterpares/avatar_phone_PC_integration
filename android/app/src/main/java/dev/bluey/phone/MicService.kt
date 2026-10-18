package dev.bluey.phone

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Binder
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat

class MicService : Service() {
    inner class Local : Binder() {
        val service: MicService get() = this@MicService
    }

    private val binder = Local()
    private var record: AudioRecord? = null
    private var thread: Thread? = null
    @Volatile private var running = false
    @Volatile var holding = false
    @Volatile var sampleRate = Speaker.RATE
    var onPcm: ((ByteArray) -> Unit)? = null

    override fun onBind(intent: Intent): IBinder = binder

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val notification = notification()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            startForeground(1, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE)
        } else {
            startForeground(1, notification)
        }
        return START_STICKY
    }

    /** Opens the mic only for live voice. Plan mode leaves it free for speech recognition. */
    fun setCapturing(on: Boolean) {
        synchronized(this) {
            if (on) startRecordingLocked() else stopRecordingLocked()
        }
    }

    override fun onDestroy() {
        running = false
        thread?.interrupt()
        record?.release()
        record = null
        super.onDestroy()
    }

    private fun stopRecordingLocked() {
        running = false
        val active = record
        record = null
        thread = null
        try {
            active?.stop()
        } catch (_: Exception) {
        }
        try {
            active?.release()
        } catch (_: Exception) {
        }
    }

    private fun startRecordingLocked() {
        if (running && record != null) return
        stopRecordingLocked()
        val opened = openRecord()
        record = opened
        running = true
        val captureRate = sampleRate
        thread = Thread({
            val buffer = ByteArray(maxOf(opened.bufferSizeInFrames * 2, captureRate / 5))
            while (running && record === opened) {
                val read = try {
                    opened.read(buffer, 0, buffer.size)
                } catch (_: Exception) {
                    break
                }
                if (read > 0 && holding) {
                    onPcm?.invoke(Speaker.toRate(buffer.copyOf(read), captureRate))
                }
            }
        }, "bluey-mic").apply { isDaemon = true; start() }
    }

    private fun openRecord(): AudioRecord {
        for (rate in intArrayOf(Speaker.RATE, 48000, 16000)) {
            val min = AudioRecord.getMinBufferSize(rate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
            if (min <= 0) continue
            val candidate = try {
                AudioRecord.Builder()
                    .setAudioSource(MediaRecorder.AudioSource.VOICE_RECOGNITION)
                    .setAudioFormat(
                        AudioFormat.Builder()
                            .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                            .setSampleRate(rate)
                            .setChannelMask(AudioFormat.CHANNEL_IN_MONO)
                            .build()
                    )
                    .setBufferSizeInBytes(min * 2)
                    .build()
            } catch (_: Exception) {
                continue
            }
            if (candidate.state == AudioRecord.STATE_INITIALIZED) {
                candidate.startRecording()
                sampleRate = rate
                return candidate
            }
            candidate.release()
        }
        throw IllegalStateException("This phone could not open the microphone.")
    }

    private fun notification(): Notification {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel("bluey", "Bluey", NotificationManager.IMPORTANCE_LOW)
        )
        return NotificationCompat.Builder(this, "bluey")
            .setSmallIcon(android.R.drawable.ic_btn_speak_now)
            .setContentTitle("Bluey")
            .setContentText("The microphone is on while Bluey is awake.")
            .setOngoing(true)
            .build()
    }
}
