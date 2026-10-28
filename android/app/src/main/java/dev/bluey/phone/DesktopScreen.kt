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
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.layout.positionInParent
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlin.math.abs
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
    onCropChanged: (cropX: Float, cropY: Float, cropW: Float, cropH: Float, maxW: Int, maxH: Int) -> Unit = { _, _, _, _, _, _ -> },
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
    var lastBarAt by remember { mutableStateOf(0L) }
    var buttonRowTop by remember { mutableFloatStateOf(0f) }
    var buttonRowBottom by remember { mutableFloatStateOf(-1f) }
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
    val currentBaseW by rememberUpdatedState(baseFrameW)
    val currentBaseH by rememberUpdatedState(baseFrameH)
    val sharpTiles = remember { mutableStateListOf<SharpTile>() }
    var frameClock by remember { mutableLongStateOf(0L) }

    // Ask ahead of the finger. A still wait would leave the new area soft until the swipe ends.
    LaunchedEffect(Unit) {
        var lastHalo: DesktopFraction? = null
        var lastSharp = false
        var lastViewAt = 0L
        var lastView = DesktopFraction(0f, 0f, 1f, 1f)
        var velX = 0f
        var velY = 0f
        while (true) {
            val zoom = zoomScale
            val canvasW = canvasSize.width
            val canvasH = canvasSize.height
            val imgW = currentBaseW
            val imgH = currentBaseH
            if (zoom <= SharpViewZoom || imgW <= 0 || imgH <= 0 || canvasW <= 0 || canvasH <= 0) {
                if (lastSharp || lastHalo == null) {
                    currentOnCropChanged(0f, 0f, 1f, 1f, 0, 0)
                    lastHalo = DesktopFraction(0f, 0f, 1f, 1f)
                    lastSharp = false
                }
                velX = 0f
                velY = 0f
                delay(48)
                continue
            }
            val view = visibleDesktopFraction(
                canvasW.toFloat(),
                canvasH.toFloat(),
                imgW.toFloat(),
                imgH.toFloat(),
                zoom,
                panOffset,
            )
            if (view == null) {
                delay(32)
                continue
            }
            val now = System.nanoTime()
            val dt = (now - lastViewAt) / 1_000_000_000f
            if (lastViewAt != 0L && dt in 0.008f..0.25f) {
                velX = velX * 0.55f + ((view.x - lastView.x) / dt) * 0.45f
                velY = velY * 0.55f + ((view.y - lastView.y) / dt) * 0.45f
            } else if (dt > 0.25f) {
                velX = 0f
                velY = 0f
            }
            lastView = view
            lastViewAt = now
            val ahead = DesktopFraction(
                (view.x + velX * 0.12f).coerceIn(0f, (1f - view.w).coerceAtLeast(0f)),
                (view.y + velY * 0.12f).coerceIn(0f, (1f - view.h).coerceAtLeast(0f)),
                view.w,
                view.h,
            )
            val held = lastHalo
            if (!lastSharp || held == null || !fractionContains(held, ahead, 0.22f)) {
                val halo = travelHalo(view, velX, velY)
                val maxW = (canvasW * (halo.w / view.w)).roundToInt().coerceIn(1, canvasW * 3)
                val maxH = (canvasH * (halo.h / view.h)).roundToInt().coerceIn(1, canvasH * 3)
                currentOnCropChanged(halo.x, halo.y, halo.w, halo.h, maxW, maxH)
                lastHalo = halo
                lastSharp = true
            }
            delay(32)
        }
    }

    LaunchedEffect(cropFrame, cropRect) {
        val bmp = cropFrame
        val rect = cropRect
        if (bmp == null || rect == null || rect.width < 0.01f || rect.height < 0.01f) return@LaunchedEffect
        val image = bmp.asImageBitmap()
        val now = System.nanoTime()
        val existing = sharpTiles.indexOfLast { sameCrop(it.rect, rect) }
        if (existing >= 0) {
            val kept = sharpTiles[existing].born
            sharpTiles.removeAt(existing)
            sharpTiles.add(SharpTile(image, rect, kept))
        } else {
            sharpTiles.add(SharpTile(image, rect, now))
        }
        while (sharpTiles.size > 4) {
            val newest = sharpTiles.last().rect
            val victim = sharpTiles.indexOfFirst { !rectsTouch(it.rect, newest, 0.02f) }
            if (victim in 0 until sharpTiles.lastIndex) sharpTiles.removeAt(victim)
            else sharpTiles.removeAt(0)
        }
    }

    val zoomedPastSharp = zoomScale > SharpViewZoom
    LaunchedEffect(zoomedPastSharp) {
        if (!zoomedPastSharp) sharpTiles.clear()
    }
    val newestBorn = sharpTiles.lastOrNull()?.born ?: 0L
    LaunchedEffect(newestBorn) {
        if (newestBorn == 0L) return@LaunchedEffect
        while (System.nanoTime() - newestBorn < 70_000_000L) {
            withFrameNanos { frameClock = it }
        }
        frameClock = System.nanoTime()
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
                        val revealZone = isRevealCorner(down.position, canvasW0, corner) && !controlsOpen

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
                DesktopPicture(frame, sharpTiles, frameClock, zoomScale, panOffset)
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
                        // Final pass so a button keeps the tap. An empty double-tap on the button row hides it.
                        awaitEachGesture {
                            val down = awaitFirstDown(pass = PointerEventPass.Final)
                            val onButtonRow = down.position.y >= buttonRowTop && down.position.y <= buttonRowBottom
                            down.consume()
                            val origin = down.position
                            var moved = !onButtonRow
                            while (true) {
                                val event = awaitPointerEvent(PointerEventPass.Final)
                                if (event.changes.count { it.pressed } > 1) moved = true
                                event.changes.forEach { change ->
                                    if (hypot(change.position.x - origin.x, change.position.y - origin.y) > 48f) {
                                        moved = true
                                    }
                                    if (!change.isConsumed) change.consume()
                                }
                                if (event.changes.none { it.pressed }) break
                            }
                            if (!moved) {
                                val now = System.currentTimeMillis()
                                if (isSecondTap(now, lastBarAt)) {
                                    lastBarAt = 0L
                                    controlsOpen = false
                                } else {
                                    lastBarAt = now
                                }
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
                    .onGloballyPositioned { coords ->
                        val top = coords.positionInParent().y
                        buttonRowTop = top
                        buttonRowBottom = top + coords.size.height
                    }
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
    tiles: List<SharpTile>,
    frameClock: Long,
    zoomScale: Float,
    panOffset: Offset,
) {
    val imageBitmap = remember(frame) { frame.asImageBitmap() }
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

        if (zoomScale <= SharpViewZoom || tiles.isEmpty()) return@Canvas
        val now = if (frameClock > 0L) frameClock else System.nanoTime()
        val newest = tiles.last()
        val newestLeft = drawLeft + newest.rect.left * totalW
        val newestTop = drawTop + newest.rect.top * totalH
        val newestW = newest.rect.width * totalW
        val newestH = newest.rect.height * totalH
        drawEdgeStretch(newest.image, newestLeft, newestTop, newestW, newestH, canvasW, canvasH)
        for (tile in tiles) {
            val age = now - tile.born
            val alpha = (age / 70_000_000f).coerceIn(0f, 1f)
            val left = drawLeft + tile.rect.left * totalW
            val top = drawTop + tile.rect.top * totalH
            drawImage(
                image = tile.image,
                srcOffset = IntOffset.Zero,
                srcSize = IntSize(tile.image.width, tile.image.height),
                dstOffset = IntOffset(left.roundToInt(), top.roundToInt()),
                dstSize = IntSize((tile.rect.width * totalW).roundToInt().coerceAtLeast(1), (tile.rect.height * totalH).roundToInt().coerceAtLeast(1)),
                alpha = alpha,
            )
        }
    }
}

private fun DrawScope.drawEdgeStretch(
    image: ImageBitmap,
    dstLeft: Float,
    dstTop: Float,
    dstW: Float,
    dstH: Float,
    canvasW: Float,
    canvasH: Float,
) {
    val srcW = image.width
    val srcH = image.height
    if (srcW < 2 || srcH < 2 || dstW < 2f || dstH < 2f) return
    val dstRight = dstLeft + dstW
    val dstBottom = dstTop + dstH
    if (dstLeft <= 1f && dstTop <= 1f && dstRight >= canvasW - 1f && dstBottom >= canvasH - 1f) return
    val limitX = canvasW * 0.45f
    val limitY = canvasH * 0.45f
    val gapLeft = if (dstLeft > 0f) minOf(dstLeft, limitX) else 0f
    val gapTop = if (dstTop > 0f) minOf(dstTop, limitY) else 0f
    val gapRight = if (dstRight < canvasW) minOf(canvasW - dstRight, limitX) else 0f
    val gapBottom = if (dstBottom < canvasH) minOf(canvasH - dstBottom, limitY) else 0f
    val top = dstTop.roundToInt()
    val height = dstH.roundToInt().coerceAtLeast(1)
    if (gapLeft > 1f) {
        drawImage(
            image,
            srcOffset = IntOffset(0, 0),
            srcSize = IntSize(1, srcH),
            dstOffset = IntOffset((dstLeft - gapLeft).roundToInt(), top),
            dstSize = IntSize(gapLeft.roundToInt().coerceAtLeast(1), height),
        )
    }
    if (gapRight > 1f) {
        drawImage(
            image,
            srcOffset = IntOffset(srcW - 1, 0),
            srcSize = IntSize(1, srcH),
            dstOffset = IntOffset(dstRight.roundToInt(), top),
            dstSize = IntSize(gapRight.roundToInt().coerceAtLeast(1), height),
        )
    }
    // Top and bottom strips include the corners, so a diagonal swipe does not leave a soft square there.
    val stripLeft = dstLeft - gapLeft
    val stripW = (gapLeft + dstW + gapRight).roundToInt().coerceAtLeast(1)
    if (gapTop > 1f) {
        drawImage(
            image,
            srcOffset = IntOffset(0, 0),
            srcSize = IntSize(srcW, 1),
            dstOffset = IntOffset(stripLeft.roundToInt(), (dstTop - gapTop).roundToInt()),
            dstSize = IntSize(stripW, gapTop.roundToInt().coerceAtLeast(1)),
        )
    }
    if (gapBottom > 1f) {
        drawImage(
            image,
            srcOffset = IntOffset(0, srcH - 1),
            srcSize = IntSize(srcW, 1),
            dstOffset = IntOffset(stripLeft.roundToInt(), dstBottom.roundToInt()),
            dstSize = IntSize(stripW, gapBottom.roundToInt().coerceAtLeast(1)),
        )
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

private data class SharpTile(val image: ImageBitmap, val rect: Rect, val born: Long)

private fun sameCrop(a: Rect, b: Rect): Boolean {
    return abs(a.left - b.left) < 0.02f && abs(a.top - b.top) < 0.02f &&
        abs(a.width - b.width) < 0.02f && abs(a.height - b.height) < 0.02f
}

private fun rectsTouch(a: Rect, b: Rect, pad: Float): Boolean {
    return a.left < b.right + pad && a.right > b.left - pad && a.top < b.bottom + pad && a.bottom > b.top - pad
}

private fun fractionContains(outer: DesktopFraction, inner: DesktopFraction, pad: Float): Boolean {
    val left = (inner.x - inner.w * pad).coerceAtLeast(0f)
    val top = (inner.y - inner.h * pad).coerceAtLeast(0f)
    val right = (inner.x + inner.w + inner.w * pad).coerceAtMost(1f)
    val bottom = (inner.y + inner.h + inner.h * pad).coerceAtMost(1f)
    return left >= outer.x - 0.002f &&
        top >= outer.y - 0.002f &&
        right <= outer.x + outer.w + 0.002f &&
        bottom <= outer.y + outer.h + 0.002f
}

private fun travelHalo(view: DesktopFraction, velX: Float, velY: Float): DesktopFraction {
    val movingX = abs(velX) > view.w * 0.4f
    val movingY = abs(velY) > view.h * 0.4f
    val leftMargin = when {
        movingX && velX < 0f -> view.w * 1.05f
        movingX -> view.w * 0.28f
        else -> view.w * 0.55f
    }
    val rightMargin = when {
        movingX && velX > 0f -> view.w * 1.05f
        movingX -> view.w * 0.28f
        else -> view.w * 0.55f
    }
    val topMargin = when {
        movingY && velY < 0f -> view.h * 1.05f
        movingY -> view.h * 0.28f
        else -> view.h * 0.55f
    }
    val bottomMargin = when {
        movingY && velY > 0f -> view.h * 1.05f
        movingY -> view.h * 0.28f
        else -> view.h * 0.55f
    }
    val shiftX = if (movingX) velX * 0.10f else 0f
    val shiftY = if (movingY) velY * 0.10f else 0f
    return clampFraction(
        view.x + shiftX - leftMargin,
        view.y + shiftY - topMargin,
        view.w + leftMargin + rightMargin,
        view.h + topMargin + bottomMargin,
    )
}

private fun clampFraction(x: Float, y: Float, w: Float, h: Float): DesktopFraction {
    var left = x
    var top = y
    var width = w.coerceAtLeast(0.02f)
    var height = h.coerceAtLeast(0.02f)
    if (left < 0f) {
        width += left
        left = 0f
    }
    if (top < 0f) {
        height += top
        top = 0f
    }
    if (left + width > 1f) width = 1f - left
    if (top + height > 1f) height = 1f - top
    width = width.coerceIn(0.02f, 1f)
    height = height.coerceIn(0.02f, 1f)
    if (left + width > 1f) left = (1f - width).coerceAtLeast(0f)
    if (top + height > 1f) top = (1f - height).coerceAtLeast(0f)
    return DesktopFraction(left, top, width, height)
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
