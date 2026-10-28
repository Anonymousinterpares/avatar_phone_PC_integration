package dev.bluey.phone

import android.graphics.Bitmap
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlin.math.hypot
import kotlin.math.roundToInt

@Composable
fun DesktopPasswordScreen(
    initialPassword: String = "",
    initialRemember: Boolean = true,
    onSubmit: (password: String, remember: Boolean) -> Unit,
    onBack: () -> Unit,
) {
    var password by remember { mutableStateOf(initialPassword) }
    var rememberPassword by remember { mutableStateOf(initialRemember) }
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black)
            .padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("Desktop Password", color = Color.White, fontSize = 24.sp)
        Spacer(Modifier.height(16.dp))
        Text(
            "The PC requires a password for remote desktop.",
            color = Color(0xFFB9B2CC),
            fontSize = 14.sp,
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(24.dp))
        TextField(
            value = password,
            onValueChange = { password = it },
            label = { Text("Password") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
            visualTransformation = androidx.compose.ui.text.input.PasswordVisualTransformation(),
        )
        Spacer(Modifier.height(12.dp))
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.Start,
        ) {
            Checkbox(
                checked = rememberPassword,
                onCheckedChange = { rememberPassword = it },
            )
            Spacer(Modifier.width(8.dp))
            Text("Remember password on this phone", color = Color.White, fontSize = 14.sp)
        }
        Spacer(Modifier.height(20.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Button(onClick = onBack) { Text("Back") }
            Button(onClick = { onSubmit(password, rememberPassword) }) { Text("Connect") }
        }
    }
}

@Composable
fun HelpCodeScreen(
    onSubmit: (String) -> Unit,
    onBack: () -> Unit,
) {
    var code by remember { mutableStateOf("") }
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black)
            .padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("Help code", color = Color.White, fontSize = 24.sp)
        Spacer(Modifier.height(8.dp))
        Text(
            "Type the code shown on the computer.",
            color = Color(0xFFB9B2CC),
            fontSize = 14.sp,
            textAlign = TextAlign.Center,
        )
        Spacer(Modifier.height(16.dp))
        TextField(
            value = code,
            onValueChange = { code = it.take(8) },
            singleLine = true,
            label = { Text("Code") },
        )
        Spacer(Modifier.height(20.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Button(onClick = onBack) { Text("Back") }
            Button(onClick = { if (code.isNotBlank()) onSubmit(code.trim()) }) { Text("Watch") }
        }
    }
}

@Composable
fun DesktopViewScreen(
    frame: Bitmap?,
    cropFrame: Bitmap? = null,
    cropRect: Rect? = null,
    fps: Int,
    quality: Int,
    scale: Float,
    grayscale: Boolean,
    audioEnabled: Boolean = true,
    audioQuality: String = "medium",
    mutePc: Boolean = false,
    ttsEnabled: Boolean = true,
    ttsReady: Boolean = false,
    ttsRunning: Boolean = false,
    ttsStatus: String = "Checking...",
    ttsError: String = "",
    onSettingsChanged: (fps: Int, quality: Int, scale: Float, grayscale: Boolean) -> Unit,
    onAudioSettingsChanged: (enabled: Boolean, quality: String, mutePc: Boolean) -> Unit = { _, _, _ -> },
    onTouch: (action: String, x: Float, y: Float, extra: Map<String, Any>) -> Unit,
    onCropChanged: (cropX: Float, cropY: Float, cropW: Float, cropH: Float) -> Unit = { _, _, _, _ -> },
    onKeyboard: () -> Unit,
    onBack: () -> Unit,
    onToggleOrientation: () -> Unit,
    onTestTts: () -> Unit = {},
    onToggleTts: () -> Unit = {},
    helpMode: Boolean = false,
    helpControl: Boolean = false,
    helpAsking: Boolean = false,
    onRequestControl: () -> Unit = {},
    onTalkDown: () -> Unit = {},
    onTalkUp: () -> Unit = {},
) {
    var showSettings by remember { mutableStateOf(false) }
    var controlsOpen by remember { mutableStateOf(true) }
    var panMode by remember { mutableStateOf(false) }
    var barHeightPx by remember { mutableIntStateOf(0) }
    var lastCornerAt by remember { mutableStateOf(0L) }
    var localFps by remember(fps) { mutableIntStateOf(fps) }
    var localQuality by remember(quality) { mutableIntStateOf(quality) }
    var localScale by remember(scale) { mutableFloatStateOf(scale) }
    var localGrayscale by remember(grayscale) { mutableStateOf(grayscale) }
    var showKeyboard by remember { mutableStateOf(false) }
    var keyText by remember { mutableStateOf("") }

    var zoomScale by remember { mutableFloatStateOf(1.0f) }
    var panOffset by remember { mutableStateOf(Offset.Zero) }
    var canvasSize by remember { mutableStateOf(IntSize.Zero) }

    val currentFrame by rememberUpdatedState(frame)
    val currentOnTouch by rememberUpdatedState(onTouch)
    val currentOnCropChanged by rememberUpdatedState(onCropChanged)
    val baseFrameW = frame?.width ?: 0
    val baseFrameH = frame?.height ?: 0

    // Past 120% zoom, ask the PC for native-quality pixels of the whole area
    // of the full desktop image that is currently inside the phone screen.
    LaunchedEffect(zoomScale, panOffset, canvasSize, baseFrameW, baseFrameH) {
        if (zoomScale <= SharpViewZoom) {
            currentOnCropChanged(0f, 0f, 1f, 1f)
            return@LaunchedEffect
        }
        if (baseFrameW <= 0 || baseFrameH <= 0 || canvasSize.width <= 0 || canvasSize.height <= 0) {
            return@LaunchedEffect
        }
        delay(250)
        val view = visibleDesktopFraction(
            canvasSize.width.toFloat(),
            canvasSize.height.toFloat(),
            baseFrameW.toFloat(),
            baseFrameH.toFloat(),
            zoomScale,
            panOffset,
        )
        if (view == null) currentOnCropChanged(0f, 0f, 1f, 1f)
        else currentOnCropChanged(view.x, view.y, view.w, view.h)
    }

    var talking by remember { mutableStateOf(false) }
    val zoomed = zoomScale > 1.05f
    // Keyed on the boolean so a further pinch, while the bar is open, does not hide it again.
    LaunchedEffect(zoomed) { controlsOpen = !zoomed }
    val density = LocalDensity.current
    val topInset = if (!zoomed && controlsOpen && barHeightPx > 0) {
        with(density) { barHeightPx.toDp() }
    } else {
        0.dp
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black),
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(top = topInset)
                .clipToBounds()
                .onSizeChanged { canvasSize = it }
                .pointerInput(Unit) {
                    awaitEachGesture {
                        val down = awaitFirstDown(requireUnconsumed = false)
                        val startTime = System.currentTimeMillis()
                        var isPinching = false
                        var movedOneFinger = false
                        var prevDistance = 0f
                        var prevCentroid = Offset.Zero
                        val touchSlop = 12f
                        val canvasW0 = size.width.toFloat().coerceAtLeast(1f)
                        val canvasH0 = size.height.toFloat().coerceAtLeast(1f)
                        val corner = minOf(canvasW0, canvasH0) * 0.22f
                        // A touch that starts here must not click or move the mouse. The second tap shows the bar.
                        val revealZone = isRevealCorner(down.position, canvasW0, corner) &&
                            zoomScale > 1.05f &&
                            !controlsOpen

                        while (true) {
                            val event = awaitPointerEvent()
                            val activePointers = event.changes.filter { it.pressed }
                            if (activePointers.isEmpty()) break

                            if (activePointers.size >= 2) {
                                isPinching = true
                                val p1 = activePointers[0].position
                                val p2 = activePointers[1].position
                                val distance = hypot(p1.x - p2.x, p1.y - p2.y)
                                val centroid = Offset((p1.x + p2.x) / 2f, (p1.y + p2.y) / 2f)

                                if (prevDistance > 0f) {
                                    val factor = distance / prevDistance
                                    val newZoom = (zoomScale * factor).coerceIn(1.0f, 5.0f)
                                    val dPan = centroid - prevCentroid
                                    val canvasW = size.width.toFloat().coerceAtLeast(1f)
                                    val canvasH = size.height.toFloat().coerceAtLeast(1f)
                                    val frameBmp = currentFrame
                                    val imgW = frameBmp?.width?.toFloat() ?: canvasW
                                    val imgH = frameBmp?.height?.toFloat() ?: canvasH
                                    zoomScale = if (newZoom <= 1.05f) 1.0f else newZoom
                                    panOffset = if (zoomScale <= 1.05f) {
                                        Offset.Zero
                                    } else {
                                        clampedPan(panOffset + dPan, canvasW, canvasH, imgW, imgH, zoomScale)
                                    }
                                }
                                prevDistance = distance
                                prevCentroid = centroid
                                activePointers.forEach { it.consume() }
                            } else {
                                prevDistance = 0f
                                if (!isPinching && activePointers.size == 1) {
                                    val pointer = activePointers[0]
                                    val canvasW = size.width.toFloat().coerceAtLeast(1f)
                                    val canvasH = size.height.toFloat().coerceAtLeast(1f)
                                    val frameBmp = currentFrame
                                    val imgW = frameBmp?.width?.toFloat() ?: canvasW
                                    val imgH = frameBmp?.height?.toFloat() ?: canvasH
                                    val dist = hypot(
                                        pointer.position.x - down.position.x,
                                        pointer.position.y - down.position.y,
                                    )
                                    if (panMode) {
                                        if (dist > touchSlop || movedOneFinger) {
                                            val delta = if (!movedOneFinger) {
                                                pointer.position - down.position
                                            } else {
                                                pointer.position - pointer.previousPosition
                                            }
                                            movedOneFinger = true
                                            panOffset = clampedPan(
                                                panOffset + delta,
                                                canvasW,
                                                canvasH,
                                                imgW,
                                                imgH,
                                                zoomScale,
                                            )
                                            if (zoomScale <= 1.05f) panOffset = Offset.Zero
                                        }
                                    } else if (!revealZone && (dist > touchSlop || movedOneFinger)) {
                                        movedOneFinger = true
                                        val (nx, ny) = canvasToPc(
                                            pointer.position,
                                            canvasW,
                                            canvasH,
                                            imgW,
                                            imgH,
                                            zoomScale,
                                            panOffset,
                                        )
                                        currentOnTouch("move", nx, ny, emptyMap())
                                    }
                                    pointer.consume()
                                }
                            }
                        }

                        if (!isPinching && !movedOneFinger) {
                            val now = System.currentTimeMillis()
                            if (revealZone) {
                                val second = isSecondTap(now, lastCornerAt)
                                lastCornerAt = if (second) 0L else now
                                if (second) controlsOpen = true
                            } else if (!panMode) {
                                val canvasW = size.width.toFloat().coerceAtLeast(1f)
                                val canvasH = size.height.toFloat().coerceAtLeast(1f)
                                val frameBmp = currentFrame
                                val imgW = frameBmp?.width?.toFloat() ?: canvasW
                                val imgH = frameBmp?.height?.toFloat() ?: canvasH
                                val (nx, ny) = canvasToPc(
                                    down.position,
                                    canvasW,
                                    canvasH,
                                    imgW,
                                    imgH,
                                    zoomScale,
                                    panOffset,
                                )
                                val duration = now - startTime
                                if (duration > 400) {
                                    currentOnTouch("click", nx, ny, mapOf("right" to true))
                                } else {
                                    currentOnTouch("click", nx, ny, emptyMap())
                                }
                            }
                        }
                    }
                },
        ) {
            if (frame != null) {
                DesktopPicture(frame, cropFrame, cropRect, zoomScale, panOffset)
            } else {
                Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    Text("Waiting for first frame…", color = Color(0xFFB9B2CC), fontSize = 16.sp)
                }
            }
        }

        if (!controlsOpen && !zoomed) {
            Button(
                onClick = { controlsOpen = true },
                modifier = Modifier
                    .align(Alignment.TopEnd)
                    .padding(8.dp),
                shape = RoundedCornerShape(12.dp),
            ) {
                Text("⚙ Controls", fontSize = 11.sp)
            }
        }

        // Absent from composition while hidden, so covered buttons cannot take taps.
        if (controlsOpen) {
            Column(
                modifier = Modifier
                    .align(Alignment.TopStart)
                    .fillMaxWidth()
                    .onSizeChanged { barHeightPx = it.height }
                    .pointerInput(Unit) {
                        // Keeps the bar's own area as the hit target, including gaps between buttons.
                        awaitPointerEventScope {
                            while (true) {
                                val event = awaitPointerEvent(PointerEventPass.Final)
                                event.changes.forEach { if (!it.isConsumed) it.consume() }
                            }
                        }
                    },
            ) {
        if (helpMode) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(Color(0xFF1A1A2E))
                    .padding(horizontal = 10.dp, vertical = 6.dp),
                verticalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                Text(
                    when {
                        helpControl -> "You can use the mouse. The person at the computer can take it back."
                        helpAsking -> "Waiting for the person at the computer to press Allow."
                        else -> "Watching. Touches do not click until they allow it."
                    },
                    color = Color.White,
                    fontSize = 13.sp,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(
                        if (talking) "Talking…" else "Hold to talk",
                        color = Color.White,
                        fontSize = 14.sp,
                        modifier = Modifier
                            .background(Color(0xFF4254D6), RoundedCornerShape(10.dp))
                            .pointerInput(Unit) {
                                awaitEachGesture {
                                    awaitFirstDown()
                                    talking = true
                                    onTalkDown()
                                    try {
                                        waitForUpOrCancellation()
                                    } finally {
                                        talking = false
                                        onTalkUp()
                                    }
                                }
                            }
                            .padding(horizontal = 14.dp, vertical = 10.dp),
                    )
                    if (!helpControl) {
                        Button(onClick = onRequestControl, enabled = !helpAsking) {
                            Text(if (helpAsking) "Asking…" else "Request control")
                        }
                    }
                }
            }
        }
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(Color(0xFF1A1A2E))
                    .padding(horizontal = 6.dp, vertical = 4.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Button(onClick = onBack) { Text("← Back", fontSize = 11.sp) }

                Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Button(onClick = {
                        val next = (zoomScale / 1.3f).coerceIn(1.0f, 5.0f)
                        zoomScale = if (next <= 1.05f) 1.0f else next
                        if (zoomScale <= 1.05f) panOffset = Offset.Zero
                    }) { Text("−", fontSize = 13.sp) }

                    Button(
                        onClick = { panMode = !panMode },
                        colors = androidx.compose.material3.ButtonDefaults.buttonColors(
                            containerColor = if (panMode) Color(0xFF4254D6) else Color(0xFF2A2A40),
                        ),
                    ) { Text("✋", fontSize = 13.sp) }

                    Button(onClick = {
                        zoomScale = (zoomScale * 1.3f).coerceIn(1.0f, 5.0f)
                    }) { Text("+", fontSize = 13.sp) }

                    if (zoomScale > 1.05f) {
                        Button(onClick = {
                            zoomScale = 1.0f
                            panOffset = Offset.Zero
                        }) {
                            Text("${(zoomScale * 100).roundToInt()}%", fontSize = 11.sp)
                        }
                    }

                    Button(onClick = onToggleOrientation) { Text("🔄", fontSize = 13.sp) }
                    Button(onClick = { onAudioSettingsChanged(!audioEnabled, audioQuality, mutePc) }) {
                        Text(if (audioEnabled) "🔊" else "🔇", fontSize = 13.sp)
                    }
                    Button(onClick = { controlsOpen = false }) { Text("⛶", fontSize = 13.sp) }
                    Button(onClick = { showKeyboard = !showKeyboard }) { Text("⌨", fontSize = 13.sp) }
                    Button(onClick = { showSettings = !showSettings }) { Text("⚙", fontSize = 13.sp) }
                }
            }

            if (showSettings) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 260.dp)
                        .background(Color(0xFF1A1A2E))
                        .verticalScroll(rememberScrollState())
                        .padding(12.dp),
                ) {
                    Text("FPS: $localFps", color = Color.White, fontSize = 13.sp)
                    Slider(
                        value = localFps.toFloat(),
                        onValueChange = { localFps = it.roundToInt() },
                        onValueChangeFinished = { onSettingsChanged(localFps, localQuality, localScale, localGrayscale) },
                        valueRange = 1f..30f,
                        steps = 28,
                    )
                    Text("JPEG Quality: $localQuality", color = Color.White, fontSize = 13.sp)
                    Slider(
                        value = localQuality.toFloat(),
                        onValueChange = { localQuality = it.roundToInt() },
                        onValueChangeFinished = { onSettingsChanged(localFps, localQuality, localScale, localGrayscale) },
                        valueRange = 10f..95f,
                        steps = 16,
                    )
                    Text("Resolution: ${(localScale * 100).roundToInt()}%", color = Color.White, fontSize = 13.sp)
                    Slider(
                        value = localScale,
                        onValueChange = { localScale = it },
                        onValueChangeFinished = { onSettingsChanged(localFps, localQuality, localScale, localGrayscale) },
                        valueRange = 0.1f..1f,
                    )
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("Grayscale", color = Color.White, fontSize = 13.sp)
                        Switch(
                            checked = localGrayscale,
                            onCheckedChange = {
                                localGrayscale = it
                                onSettingsChanged(localFps, localQuality, localScale, it)
                            },
                        )
                    }

                    Spacer(Modifier.height(10.dp))
                    Text("Audio Streaming", color = Color(0xFFA9BCFF), fontSize = 14.sp)
                    Spacer(Modifier.height(4.dp))

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Text("Stream Sound to Phone", color = Color.White, fontSize = 13.sp)
                        Switch(
                            checked = audioEnabled,
                            onCheckedChange = { onAudioSettingsChanged(it, audioQuality, mutePc) },
                        )
                    }

                    if (audioEnabled) {
                        Spacer(Modifier.height(6.dp))
                        Text("Sound Quality:", color = Color.White, fontSize = 12.sp)
                        Row(
                            horizontalArrangement = Arrangement.spacedBy(6.dp),
                            modifier = Modifier.fillMaxWidth().padding(top = 4.dp),
                        ) {
                            val options = listOf("low" to "Low (16k)", "medium" to "Med (24k)", "high" to "High (44k stereo)")
                            for ((qKey, qLabel) in options) {
                                val selected = audioQuality == qKey
                                Button(
                                    onClick = { onAudioSettingsChanged(audioEnabled, qKey, mutePc) },
                                    modifier = Modifier.weight(1f),
                                    colors = androidx.compose.material3.ButtonDefaults.buttonColors(
                                        containerColor = if (selected) Color(0xFF4254D6) else Color(0xFF2A2A40),
                                    ),
                                ) {
                                    Text(qLabel, fontSize = 10.sp, maxLines = 1)
                                }
                            }
                        }

                        Spacer(Modifier.height(8.dp))
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.SpaceBetween,
                        ) {
                            Column(modifier = Modifier.weight(1f).padding(end = 8.dp)) {
                                Text("Reroute PC Sound to Phone", color = Color.White, fontSize = 13.sp)
                                Text("Silences PC speakers and routes audio to phone", color = Color(0xFFB9B2CC), fontSize = 11.sp)
                            }
                            Switch(
                                checked = mutePc,
                                onCheckedChange = { onAudioSettingsChanged(audioEnabled, audioQuality, it) },
                            )
                        }
                    }

                    Spacer(Modifier.height(8.dp))
                    androidx.compose.material3.HorizontalDivider(color = Color(0xFF3A3850), thickness = 1.dp)
                    Spacer(Modifier.height(8.dp))

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            val dotColor = when {
                                !ttsEnabled -> Color(0xFF888899)
                                ttsReady -> Color(0xFF4CAF50)
                                ttsRunning -> Color(0xFFFFC107)
                                else -> Color(0xFFF44336)
                            }
                            Box(
                                modifier = Modifier
                                    .size(10.dp)
                                    .background(dotColor, androidx.compose.foundation.shape.CircleShape)
                            )
                            Text(
                                text = "TTS: $ttsStatus",
                                color = Color.White,
                                fontSize = 12.sp,
                                fontWeight = androidx.compose.ui.text.font.FontWeight.Medium,
                            )
                        }
                        Switch(
                            checked = ttsEnabled,
                            onCheckedChange = { onToggleTts() },
                        )
                    }

                    if (ttsError.isNotEmpty()) {
                        Text("Error: $ttsError", color = Color(0xFFFF6B6B), fontSize = 11.sp)
                    }

                    Spacer(Modifier.height(4.dp))
                    Button(
                        onClick = onTestTts,
                        modifier = Modifier.fillMaxWidth(),
                        colors = androidx.compose.material3.ButtonDefaults.buttonColors(
                            containerColor = Color(0xFF3F51B5),
                        ),
                    ) {
                        Text("🗣️ Test TTS Voice (No LLM)", fontSize = 12.sp)
                    }

                    Spacer(Modifier.height(12.dp))
                    Button(
                        onClick = { showSettings = false },
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Text("Done", fontSize = 13.sp)
                    }
                }
            }

            if (showKeyboard) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .background(Color(0xFF1A1A2E))
                        .padding(8.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    TextField(
                        value = keyText,
                        onValueChange = { keyText = it },
                        modifier = Modifier.weight(1f),
                        singleLine = true,
                        label = { Text("Type here") },
                    )
                    Button(onClick = {
                        if (keyText.isNotEmpty()) {
                            onTouch("type", 0f, 0f, mapOf("text" to keyText))
                            keyText = ""
                        }
                    }) { Text("Send") }
                    Button(onClick = { onTouch("press", 0f, 0f, mapOf("keys" to "enter")) }) { Text("↵") }
                }
            }
        }

        }
    }
}

@Composable
private fun DesktopPicture(
    frame: Bitmap,
    cropFrame: Bitmap?,
    cropRect: Rect?,
    zoomScale: Float,
    panOffset: Offset,
) {
    val imageBitmap = remember(frame) { frame.asImageBitmap() }
    val cropImageBitmap = remember(cropFrame) { cropFrame?.asImageBitmap() }
    Canvas(modifier = Modifier.fillMaxSize()) {
        val canvasW = size.width
        val canvasH = size.height
        val imgW = imageBitmap.width.toFloat()
        val imgH = imageBitmap.height.toFloat()
        val fitScale = minOf(canvasW / imgW, canvasH / imgH)
        val baseW = imgW * fitScale
        val baseH = imgH * fitScale
        val totalW = baseW * zoomScale
        val totalH = baseH * zoomScale

        val maxPanX = if (totalW > canvasW) (totalW - canvasW) / 2f else 0f
        val maxPanY = if (totalH > canvasH) (totalH - canvasH) / 2f else 0f
        val curPanX = panOffset.x.coerceIn(-maxPanX, maxPanX)
        val curPanY = panOffset.y.coerceIn(-maxPanY, maxPanY)

        val drawLeft = (canvasW / 2f + curPanX) - totalW / 2f
        val drawTop = (canvasH / 2f + curPanY) - totalH / 2f

        drawImage(
            image = imageBitmap,
            srcOffset = IntOffset.Zero,
            srcSize = IntSize(imageBitmap.width, imageBitmap.height),
            dstOffset = IntOffset(drawLeft.roundToInt(), drawTop.roundToInt()),
            dstSize = IntSize(totalW.roundToInt().coerceAtLeast(1), totalH.roundToInt().coerceAtLeast(1)),
        )

        if (zoomScale > SharpViewZoom && cropImageBitmap != null && cropRect != null &&
            cropRect.width > 0.01f && cropRect.height > 0.01f &&
            (cropRect.width < 0.98f || cropRect.height < 0.98f)
        ) {
            val cropDstLeft = drawLeft + cropRect.left * totalW
            val cropDstTop = drawTop + cropRect.top * totalH
            val cropDstW = cropRect.width * totalW
            val cropDstH = cropRect.height * totalH
            drawImage(
                image = cropImageBitmap,
                srcOffset = IntOffset.Zero,
                srcSize = IntSize(cropImageBitmap.width, cropImageBitmap.height),
                dstOffset = IntOffset(cropDstLeft.roundToInt(), cropDstTop.roundToInt()),
                dstSize = IntSize(cropDstW.roundToInt().coerceAtLeast(1), cropDstH.roundToInt().coerceAtLeast(1)),
            )
        }
    }
}

private fun clampedPan(
    pan: Offset,
    canvasW: Float,
    canvasH: Float,
    imgW: Float,
    imgH: Float,
    zoom: Float,
): Offset {
    if (imgW <= 0f || imgH <= 0f || canvasW <= 0f || canvasH <= 0f) return Offset.Zero
    val fitScale = minOf(canvasW / imgW, canvasH / imgH)
    val totalW = imgW * fitScale * zoom
    val totalH = imgH * fitScale * zoom
    val maxPanX = if (totalW > canvasW) (totalW - canvasW) / 2f else 0f
    val maxPanY = if (totalH > canvasH) (totalH - canvasH) / 2f else 0f
    return Offset(
        pan.x.coerceIn(-maxPanX, maxPanX),
        pan.y.coerceIn(-maxPanY, maxPanY),
    )
}

private fun isRevealCorner(pos: Offset, width: Float, cornerPx: Float): Boolean {
    return pos.x >= width - cornerPx && pos.y >= 0f && pos.y <= cornerPx
}

private fun isSecondTap(now: Long, lastAt: Long): Boolean {
    val gap = now - lastAt
    return gap in 40L..400L
}

private const val SharpViewZoom = 1.2f

private data class DesktopFraction(val x: Float, val y: Float, val w: Float, val h: Float)

/** Fraction of the full desktop image that intersects the phone screen. */
private fun visibleDesktopFraction(
    canvasW: Float,
    canvasH: Float,
    imgW: Float,
    imgH: Float,
    zoomScale: Float,
    pan: Offset,
): DesktopFraction? {
    if (imgW <= 0f || imgH <= 0f || canvasW <= 0f || canvasH <= 0f) return null
    val fitScale = minOf(canvasW / imgW, canvasH / imgH)
    val totalW = imgW * fitScale * zoomScale
    val totalH = imgH * fitScale * zoomScale
    if (totalW <= 0f || totalH <= 0f) return null
    val maxPanX = if (totalW > canvasW) (totalW - canvasW) / 2f else 0f
    val maxPanY = if (totalH > canvasH) (totalH - canvasH) / 2f else 0f
    val curPanX = pan.x.coerceIn(-maxPanX, maxPanX)
    val curPanY = pan.y.coerceIn(-maxPanY, maxPanY)
    val drawLeft = (canvasW / 2f + curPanX) - totalW / 2f
    val drawTop = (canvasH / 2f + curPanY) - totalH / 2f
    val left = ((0f - drawLeft) / totalW).coerceIn(0f, 1f)
    val top = ((0f - drawTop) / totalH).coerceIn(0f, 1f)
    val right = ((canvasW - drawLeft) / totalW).coerceIn(0f, 1f)
    val bottom = ((canvasH - drawTop) / totalH).coerceIn(0f, 1f)
    val w = right - left
    val h = bottom - top
    if (w <= 0.01f || h <= 0.01f) return null
    return DesktopFraction(left, top, w, h)
}

private fun canvasToPc(
    pos: Offset,
    canvasW: Float,
    canvasH: Float,
    imgW: Float,
    imgH: Float,
    zoomScale: Float,
    pan: Offset,
): Pair<Float, Float> {
    if (imgW <= 0f || imgH <= 0f || canvasW <= 0f || canvasH <= 0f) return Pair(0f, 0f)
    val fitScale = minOf(canvasW / imgW, canvasH / imgH)
    val totalW = imgW * fitScale * zoomScale
    val totalH = imgH * fitScale * zoomScale
    val maxPanX = if (totalW > canvasW) (totalW - canvasW) / 2f else 0f
    val maxPanY = if (totalH > canvasH) (totalH - canvasH) / 2f else 0f
    val curPanX = pan.x.coerceIn(-maxPanX, maxPanX)
    val curPanY = pan.y.coerceIn(-maxPanY, maxPanY)

    val drawLeft = (canvasW / 2f + curPanX) - totalW / 2f
    val drawTop = (canvasH / 2f + curPanY) - totalH / 2f

    val nx = ((pos.x - drawLeft) / totalW).coerceIn(0f, 1f)
    val ny = ((pos.y - drawTop) / totalH).coerceIn(0f, 1f)
    return Pair(nx, ny)
}
