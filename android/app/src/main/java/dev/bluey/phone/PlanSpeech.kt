package dev.bluey.phone

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer

/**
 * Hold-to-talk text. Android's speech recognizer (the Google speech service on a normal phone)
 * turns audio into text on the phone. The PC is not asked until the finger comes up.
 * A pause does not finish the sentence. The recognizer often ends a phrase on its own,
 * so the words so far are kept and listening starts again until stop().
 */
class PlanSpeech(private val context: Context) {
    interface Listener {
        fun onReady()
        fun onLevel(level: Float)
        fun onPartial(text: String)
        fun onFinal(text: String)
        fun onFailure(message: String)
    }

    private val main = Handler(Looper.getMainLooper())
    private var recognizer: SpeechRecognizer? = null
    private var listener: Listener? = null
    private var generation = 0
    private var ear = 0
    private var delivered = true
    private var stopping = false
    private var committed = ""
    private var lastPartial = ""
    private var misses = 0

    fun available(): Boolean = SpeechRecognizer.isRecognitionAvailable(context)

    fun start(listener: Listener) {
        silence()
        this.listener = listener
        delivered = false
        stopping = false
        committed = ""
        lastPartial = ""
        misses = 0
        val ticket = ++generation
        if (!available()) {
            fail("This phone has no speech recognizer. Install the Google app and allow its microphone.")
            return
        }
        listen(ticket)
    }

    /** Finger up: ask the recognizer for the phrase it has so far. */
    fun stop() {
        if (delivered) return
        stopping = true
        val active = recognizer
        if (active == null) {
            finish(lastPartial)
            return
        }
        try {
            active.stopListening()
        } catch (_: Exception) {
            finish(lastPartial)
            return
        }
        main.postDelayed({ finish(lastPartial) }, 900)
    }

    fun cancel() {
        silence()
    }

    fun destroy() {
        silence()
        listener = null
    }

    private fun silence() {
        generation++
        delivered = true
        stopping = true
        main.removeCallbacksAndMessages(null)
        val active = recognizer
        recognizer = null
        try {
            active?.destroy()
        } catch (_: Exception) {
        }
    }

    private fun listen(ticket: Int) {
        if (ticket != generation || delivered) return
        val mine = ++ear
        val created = try {
            SpeechRecognizer.createSpeechRecognizer(context)
        } catch (_: Exception) {
            fail("This phone could not start speech recognition.")
            return
        }
        created.setRecognitionListener(object : RecognitionListener {
            override fun onReadyForSpeech(params: Bundle?) {
                if (current(ticket, mine)) listener?.onReady()
            }

            override fun onBeginningOfSpeech() {}

            override fun onRmsChanged(rmsdB: Float) {
                if (!current(ticket, mine)) return
                listener?.onLevel(((rmsdB + 2f) / 12f).coerceIn(0f, 1f))
            }

            override fun onBufferReceived(buffer: ByteArray?) {}
            override fun onEndOfSpeech() {}

            override fun onError(error: Int) {
                if (!current(ticket, mine)) return
                if (stopping) {
                    finish(lastPartial)
                    return
                }
                if (error == SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS) {
                    fail(describe(error))
                    return
                }
                val dead = error == SpeechRecognizer.ERROR_CLIENT || error == SpeechRecognizer.ERROR_RECOGNIZER_BUSY
                keepListening(ticket, recreate = dead)
            }

            override fun onPartialResults(partialResults: Bundle?) {
                if (!current(ticket, mine)) return
                val text = partialResults.best()
                if (text.isEmpty()) return
                misses = 0
                val shown = join(committed, text)
                lastPartial = shown
                listener?.onPartial(shown)
            }

            override fun onResults(results: Bundle?) {
                if (!current(ticket, mine)) return
                val text = results.best()
                if (text.isNotEmpty()) {
                    misses = 0
                    committed = join(committed, text)
                    lastPartial = committed
                    listener?.onPartial(committed)
                }
                if (!stopping) {
                    keepListening(ticket, recreate = false)
                    return
                }
                finish(lastPartial)
            }

            override fun onEvent(eventType: Int, params: Bundle?) {}
        })
        recognizer = created
        val intent = recognitionIntent()
        try {
            created.startListening(intent)
        } catch (_: Exception) {
            fail("The speech recognizer did not start. Let go and hold again.")
        }
    }

    private fun recognitionIntent(): Intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
        putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
        putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true)
        putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 3)
        putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_MINIMUM_LENGTH_MILLIS, 1500L)
        // Finger up ends the turn. A pause in a long sentence must not.
        // Some phones ignore these and end anyway. keepListening then continues.
        putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS, 20000L)
        putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS, 20000L)
    }

    private fun keepListening(ticket: Int, recreate: Boolean) {
        if (ticket != generation || stopping || delivered) return
        misses++
        if (lastPartial.isBlank() && misses >= 6) {
            fail("Mic is on, but no words came through. Hold the face and speak a full sentence.")
            return
        }
        main.postDelayed({
            if (ticket != generation || stopping || delivered) return@postDelayed
            if (!recreate && recognizer != null) {
                try {
                    recognizer?.startListening(recognitionIntent())
                    return@postDelayed
                } catch (_: Exception) {
                }
            }
            val old = recognizer
            recognizer = null
            ear++
            try {
                old?.destroy()
            } catch (_: Exception) {
            }
            listen(ticket)
        }, 200)
    }

    private fun current(ticket: Int, mine: Int): Boolean {
        return ticket == generation && mine == ear && !delivered
    }

    private fun finish(text: String) {
        if (delivered) return
        delivered = true
        generation++
        main.removeCallbacksAndMessages(null)
        val active = recognizer
        recognizer = null
        try {
            active?.destroy()
        } catch (_: Exception) {
        }
        listener?.onFinal(text.trim())
    }

    private fun fail(message: String) {
        if (delivered) return
        delivered = true
        generation++
        main.removeCallbacksAndMessages(null)
        val active = recognizer
        recognizer = null
        try {
            active?.destroy()
        } catch (_: Exception) {
        }
        listener?.onFailure(message)
    }

    private fun describe(error: Int): String = when (error) {
        SpeechRecognizer.ERROR_AUDIO -> "The microphone was busy. Let go and hold again."
        SpeechRecognizer.ERROR_CLIENT -> "Speech recognition stopped. Let go, then hold the face and speak."
        SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS -> "Bluey needs microphone permission. Allow it, then hold again."
        SpeechRecognizer.ERROR_NETWORK, SpeechRecognizer.ERROR_NETWORK_TIMEOUT ->
            "The phone's speech service needs a network connection."
        SpeechRecognizer.ERROR_NO_MATCH, SpeechRecognizer.ERROR_SPEECH_TIMEOUT ->
            "I heard the mic, but I didn't understand any words."
        SpeechRecognizer.ERROR_RECOGNIZER_BUSY -> "The speech recognizer is busy. Wait a second and hold again."
        SpeechRecognizer.ERROR_SERVER -> "The phone's speech service failed. Try again."
        else -> "I couldn't turn that into text (speech error $error)."
    }

    private fun Bundle?.best(): String {
        return this?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)
            ?.firstOrNull()
            ?.trim()
            .orEmpty()
    }

    private fun join(earlier: String, next: String): String {
        val left = earlier.trim()
        val right = next.trim()
        if (left.isEmpty()) return right
        if (right.isEmpty()) return left
        if (right.startsWith(left)) return right
        if (left.endsWith(right)) return left
        return "$left $right"
    }
}
