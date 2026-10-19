package dev.bluey.phone

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.withTimeoutOrNull
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.sin

data class Host(val name: String, val host: String, val port: Int) {
    val label: String get() = "$name  $host:$port"
}

data class Ui(
    val gazeX: Float = 0f,
    val gazeY: Float = 0f,
    val mood: String = "resting",
    val talking: Boolean = false,
    val caption: String = "",
    val status: String = "Looking for your PC…",
    val address: String = "",
    val hosts: List<Host> = emptyList(),
    val awake: Boolean = false,
    val micOn: Boolean = false,
    val micLevel: Float = 0f,
    val connected: Boolean = false,
    val deskMode: Boolean = false,
    val deskFrame: android.graphics.Bitmap? = null,
    val deskFps: Int = 10,
    val deskQuality: Int = 60,
    val deskScale: Float = 0.5f,
    val deskGrayscale: Boolean = false,
    val deskNeedsPassword: Boolean = false,
    val deskAuthenticated: Boolean = false,
    val deskCropFrame: android.graphics.Bitmap? = null,
    val deskCropRect: androidx.compose.ui.geometry.Rect? = null,
    val deskRememberPassword: Boolean = false,
)

private val Berry1 = Color(0xFFA9BCFF)
private val Berry2 = Color(0xFF6C86F5)
private val Berry3 = Color(0xFF4254D6)
private val Berry4 = Color(0xFF2B2F8F)
private val Navy = Color(0xFF1C1F66)
private val Ink = Color(0xFF17151F)
private val Blush = Color(0xFFF3A6D8)

@Composable
fun BlueyScreen(
    ui: Ui,
    onAddress: (String) -> Unit,
    onConnect: () -> Unit,
    onPick: (Host) -> Unit,
    onDoubleTap: () -> Unit,
    onHoldStart: () -> Unit,
    onHoldEnd: () -> Unit,
    onType: (String) -> Unit,
    onDesktop: () -> Unit,
) {
    var typingOpen by remember { mutableStateOf(false) }
    var draft by remember { mutableStateOf("") }
    val bubble = when {
        ui.caption.isNotEmpty() -> ui.caption
        !ui.awake -> "Zzzz"
        else -> ""
    }
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black)
            .imePadding()
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(ui.status, color = Color(0xFFD5D8E6), fontSize = 15.sp)
        FaceCanvas(
            ui = ui,
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f)
                .pointerInput(ui.awake) {
                    var lastTap = 0L
                    awaitEachGesture {
                        val down = awaitFirstDown()
                        val released = withTimeoutOrNull(220) { waitForUpOrCancellation() }
                        if (released != null) {
                            if (down.uptimeMillis - lastTap < 340) {
                                lastTap = 0L
                                onDoubleTap()
                            } else {
                                lastTap = down.uptimeMillis
                            }
                        } else if (ui.awake) {
                            onHoldStart()
                            try {
                                waitForUpOrCancellation()
                            } finally {
                                onHoldEnd()
                            }
                        }
                    }
                },
        )
        if (bubble.isNotEmpty()) {
            Text(
                bubble,
                color = Color.White,
                fontSize = if (bubble == "Zzzz") 28.sp else 18.sp,
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .background(Color(0xF01E1B29), RoundedCornerShape(16.dp))
                    .padding(14.dp),
            )
        }
        Text(
            if (ui.awake) "Hold the face or the button. Double-tap the face to sleep."
            else "Double-tap the face to wake him.",
            color = Color(0xFFB9B2CC),
            fontSize = 13.sp,
        )
        HoldButton(ui = ui, onHoldStart = onHoldStart, onHoldEnd = onHoldEnd)
        if (ui.awake) {
            Button(onClick = { typingOpen = !typingOpen }, modifier = Modifier.fillMaxWidth()) {
                Text(if (typingOpen) "Close typing" else "Manual typing")
            }
            if (typingOpen) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    TextField(
                        value = draft,
                        onValueChange = { draft = it },
                        modifier = Modifier.weight(1f),
                        label = { Text("Type instead of speaking") },
                    )
                    Button(onClick = {
                        val text = draft.trim()
                        if (text.isNotEmpty()) {
                            draft = ""
                            typingOpen = false
                            onType(text)
                        }
                    }) { Text("Send") }
                }
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            TextField(
                value = ui.address,
                onValueChange = onAddress,
                modifier = Modifier.weight(1f),
                singleLine = true,
                label = { Text("PC address") },
            )
            Button(onClick = onConnect) { Text("Connect") }
        }
        ui.hosts.take(4).forEach { host ->
            Button(onClick = { onPick(host) }, modifier = Modifier.fillMaxWidth()) {
                Text(host.label)
            }
        }
        Button(
            onClick = onDesktop,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text("Remote Desktop")
        }
    }
}

@Composable
private fun HoldButton(ui: Ui, onHoldStart: () -> Unit, onHoldEnd: () -> Unit) {
    val label = when {
        !ui.awake -> "Asleep"
        ui.micOn -> "Listening… let go to send"
        else -> "Hold"
    }
    Box(
        contentAlignment = Alignment.Center,
        modifier = Modifier
            .fillMaxWidth()
            .height(88.dp)
            .background(if (ui.awake) Berry3 else Color(0xFF2A3148), RoundedCornerShape(22.dp))
            .pointerInput(ui.awake) {
                if (!ui.awake) return@pointerInput
                awaitEachGesture {
                    awaitFirstDown()
                    onHoldStart()
                    try {
                        waitForUpOrCancellation()
                    } finally {
                        onHoldEnd()
                    }
                }
            },
    ) {
        if (ui.micOn) {
            Box(
                modifier = Modifier
                    .fillMaxHeight()
                    .fillMaxWidth(ui.micLevel.coerceIn(0.08f, 1f))
                    .align(Alignment.CenterStart)
                    .background(Color.White.copy(alpha = 0.22f), RoundedCornerShape(22.dp)),
            )
        }
        Text(label, color = Color.White, fontSize = 22.sp, textAlign = TextAlign.Center)
    }
}

@Composable
private fun FaceCanvas(ui: Ui, modifier: Modifier) {
    val clock by rememberInfiniteTransition(label = "bluey").animateFloat(
        initialValue = 0f,
        targetValue = 3600f,
        animationSpec = infiniteRepeatable(tween(durationMillis = 3_600_000, easing = LinearEasing)),
        label = "clock",
    )
    val gazeX by animateFloatAsState(ui.gazeX, tween(90), label = "gazeX")
    val gazeY by animateFloatAsState(ui.gazeY, tween(90), label = "gazeY")
    Canvas(modifier) {
        drawRect(Color.Black)
        drawBluey(ui, gazeX, gazeY, clock)
    }
}

private fun DrawScope.drawBluey(ui: Ui, gazeX: Float, gazeY: Float, clock: Float) {
    val expression = when {
        !ui.awake || ui.mood == "sleepy" || ui.mood == "resting" -> "sleepy"
        else -> ui.mood
    }
    val breathe = sin(clock * 1.8f)
    val talk = when {
        ui.talking -> 0.35f + 0.65f * abs(sin(clock * 14f))
        ui.micOn -> ui.micLevel.coerceIn(0f, 1f)
        else -> 0f
    }
    val blinkWindow = if (expression == "sleepy") 0.45f else 0.16f
    val blinkSlot = clock % if (expression == "sleepy") 2.4f else 3.8f
    val blink = if (blinkSlot < blinkWindow) sin(PI.toFloat() * blinkSlot / blinkWindow) else 0f
    val bounce = -talk * 16f + breathe * 3f
    val lean = (-gazeX.coerceIn(-1f, 1f) * 0.045f * 180f / PI).toFloat()

    val scale = size.width / 844f
    val eyeLine = 201f
    // Landscape design, anchored to the bottom so he peeks up the way the original face does.
    val top = size.height - 390f * scale
    val left = (size.width - 844f * scale) / 2f
    if (ui.micOn) {
        drawCircle(
            color = Berry2.copy(alpha = 0.16f + 0.38f * ui.micLevel.coerceIn(0f, 1f)),
            radius = (210f + 28f * ui.micLevel) * scale,
            center = Offset(left + 422f * scale, top + eyeLine * scale),
            style = Stroke(width = (14f + 10f * ui.micLevel) * scale),
        )
    }

    translate(left, top) {
        scale(scale, scale, pivot = Offset.Zero) {
            val body = Rect(12f, 44f + bounce, 832f, 744f + bounce)
            rotate(lean, pivot = Offset(body.center.x, body.bottom)) {
                val blob = blueberryPath(body)
                scale(1.05f, 1.04f, pivot = Offset(body.center.x, body.top + 120f)) {
                    drawPath(blob, Berry2.copy(alpha = 0.35f))
                }
                drawPath(
                    blob,
                    brush = Brush.linearGradient(
                        colorStops = arrayOf(
                            0f to Berry1,
                            0.34f to Berry2,
                            0.68f to Berry3,
                            1f to Berry4,
                        ),
                        start = Offset(body.left + 0.25f * body.width, body.top + 0.067f * body.height),
                        end = Offset(body.left + 0.75f * body.width, body.top + 0.933f * body.height),
                    ),
                )
                clipPath(blob) {
                    drawCircle(
                        brush = Brush.radialGradient(
                            colors = listOf(Color.White.copy(alpha = 0.55f), Color.White.copy(alpha = 0f)),
                            center = Offset(body.left + 246f, body.top + 126f),
                            radius = 243f,
                        ),
                        radius = 243f,
                        center = Offset(body.left + 246f, body.top + 126f),
                    )
                    drawOval(
                        Color.White.copy(alpha = 0.38f),
                        topLeft = Offset(body.left + 190f, body.top + 36f),
                        size = Size(52f, 24f),
                    )
                    drawOval(
                        Color.White.copy(alpha = 0.18f),
                        topLeft = Offset(body.left + 250f, body.top + 58f),
                        size = Size(18f, 10f),
                    )
                }
                drawPath(blob, Color.White.copy(alpha = 0.18f), style = Stroke(width = 3f))
                drawPath(
                    crownPath(Rect(body.left + 368f, body.top - 22f, body.left + 452f, body.top + 36f)),
                    Navy,
                )

                val eyeY = body.top + 157f
                val eyes = listOf(Offset(body.left + 280f, eyeY), Offset(body.left + 540f, eyeY))
                val blushAmount = if (expression == "happy") 0.85f else 0.28f
                eyes.forEachIndexed { index, eye ->
                    val side = if (index == 0) -1f else 1f
                    drawOval(
                        Blush.copy(alpha = 0.22f + 0.4f * blushAmount),
                        topLeft = Offset(eye.x - 45f + side * 62f - 8f, eye.y + 88f),
                        size = Size(106f, 54f),
                    )
                    drawBrow(eye, side, expression)
                    drawEye(expression, eye, gazeX, gazeY, side, blink, talk, body)
                }
                drawMouth(expression, body, talk)
                if (expression == "thinking") drawThinkDots(body, clock)
            }
        }
    }
}

private fun DrawScope.drawBrow(eye: Offset, side: Float, expression: String) {
    val lift = when (expression) {
        "listening" -> 10f
        "talking" -> 8f
        "pointing" -> -4f
        "thinking" -> 4f
        "happy" -> 16f
        else -> -10f
    }
    val tilt = when (expression) {
        "thinking" -> 0.22f
        "pointing" -> -0.14f
        "sleepy" -> 0.12f
        else -> 0f
    }
    val extra = if (expression == "thinking" && side > 0f) -0.25f else 0f
    val browY = eye.y - 112f - lift.coerceIn(-10f, 18f) * 0.7f
    val pivot = Offset(eye.x + side * 6f, browY)
    val degrees = (tilt * -side + extra) * 180f / PI.toFloat()
    rotate(degrees, pivot) {
        val arch = Path().apply {
            moveTo(pivot.x - 38f, pivot.y + 6f)
            quadraticTo(pivot.x, pivot.y - 14f, pivot.x + 38f, pivot.y + 6f)
        }
        drawPath(arch, Navy, style = Stroke(width = 14f, cap = StrokeCap.Round))
    }
}

private fun DrawScope.drawEye(
    expression: String,
    eye: Offset,
    gazeX: Float,
    gazeY: Float,
    side: Float,
    blink: Float,
    talk: Float,
    body: Rect,
) {
    if (expression == "happy") {
        val smile = Path().apply {
            moveTo(eye.x - 64f, eye.y + 28f)
            quadraticTo(eye.x, eye.y - 58f, eye.x + 64f, eye.y + 28f)
        }
        drawPath(smile, Ink, style = Stroke(width = 22f, cap = StrokeCap.Round))
        return
    }
    val open = max(0.08f, (1f - blink) * (1f - talk * 0.1f))
    val white = Rect(eye.x - 95f, eye.y - 95f * open, eye.x + 95f, eye.y + 95f * open)
    val whitePath = Path().apply { addOval(white) }
    drawPath(whitePath, Color.White)
    if (open < 0.2f) return

    var gx = gazeX
    var gy = gazeY
    val len = hypot(gx, gy)
    if (len > 1f) {
        gx /= len
        gy /= len
    }
    val radius = 46f * when (expression) {
        "listening" -> 1.12f
        "talking" -> 1.05f
        "pointing" -> 0.95f
        "thinking" -> 0.9f
        else -> 0.9f
    }
    var pupil = Offset(eye.x + gx * 50f - side * 3f, eye.y + gy * 50f * open)
    if (expression == "sleepy") pupil = Offset(pupil.x, max(pupil.y, eye.y) + 22f)
    clipPath(whitePath) {
        drawOval(Ink, topLeft = pupil - Offset(radius, radius), size = Size(radius * 2f, radius * 2f))
        drawOval(
            Color.White,
            topLeft = Offset(pupil.x - radius * 0.55f, pupil.y - radius * 0.7f),
            size = Size(radius * 0.5f, radius * 0.5f),
        )
        drawOval(
            Color.White.copy(alpha = 0.85f),
            topLeft = Offset(pupil.x + radius * 0.22f, pupil.y + radius * 0.18f),
            size = Size(radius * 0.22f, radius * 0.22f),
        )
        if (expression == "sleepy") {
            val droop = white.height * (0.52f + 0.06f * sin(body.top))
            drawSkinLid(body, Rect(eye.x - 110f, white.top - 8f, eye.x + 110f, white.top + droop))
        }
        if (expression == "pointing") {
            drawSkinLid(body, Rect(eye.x - 110f, white.top - 16f, eye.x + 110f, white.top + 28f))
        }
    }
}

private fun DrawScope.drawSkinLid(body: Rect, lid: Rect) {
    drawRect(
        brush = Brush.linearGradient(
            colors = listOf(Berry2, Berry3, Berry4),
            start = Offset(body.left, body.top),
            end = Offset(body.right, body.bottom),
        ),
        topLeft = lid.topLeft,
        size = lid.size,
    )
}

private fun DrawScope.drawMouth(expression: String, body: Rect, talk: Float) {
    val noseW = 64f - talk * 8f
    val noseH = 40f + talk * 36f
    val left = body.left + 410f - noseW / 2f
    val top = body.top + 272f - talk * 6f
    if (expression == "happy" && talk < 0.15f) {
        val smile = Path().apply {
            moveTo(left - 10f, top + 8f)
            quadraticTo(left + noseW / 2f, top + 62f, left + noseW + 10f, top + 8f)
            close()
        }
        drawPath(smile, Navy)
        return
    }
    drawOval(Navy, topLeft = Offset(left, top), size = Size(noseW, noseH))
    if (talk > 0.25f) {
        drawOval(
            Color(0xFFE58BC4).copy(alpha = 0.9f),
            topLeft = Offset(left + noseW * 0.22f, top + noseH * 0.58f),
            size = Size(noseW * 0.56f, noseH * 0.28f),
        )
    }
}

private fun DrawScope.drawThinkDots(body: Rect, clock: Float) {
    for (i in 0..2) {
        val wobble = sin(clock * 3f + i) * 4f
        drawOval(
            Color.White.copy(alpha = 1f - i * 0.28f),
            topLeft = Offset(body.left + 690f + i * 26f, body.top + 40f - i * 12f + wobble),
            size = Size(16f, 16f),
        )
    }
}

private fun blueberryPath(rect: Rect): Path {
    val w = rect.width
    val h = rect.height
    val tlW = 0.52f * w
    val tlH = 0.58f * h
    val trW = 0.48f * w
    val trH = 0.56f * h
    val brW = 0.46f * w
    val brH = 0.44f * h
    val blW = 0.54f * w
    val blH = 0.42f * h
    val k = 0.5523f
    return Path().apply {
        moveTo(rect.left + tlW, rect.top)
        lineTo(rect.right - trW, rect.top)
        cubicTo(
            rect.right - trW * (1f - k), rect.top,
            rect.right, rect.top + trH * (1f - k),
            rect.right, rect.top + trH,
        )
        lineTo(rect.right, rect.bottom - brH)
        cubicTo(
            rect.right, rect.bottom - brH * (1f - k),
            rect.right - brW * (1f - k), rect.bottom,
            rect.right - brW, rect.bottom,
        )
        lineTo(rect.left + blW, rect.bottom)
        cubicTo(
            rect.left + blW * (1f - k), rect.bottom,
            rect.left, rect.bottom - blH * (1f - k),
            rect.left, rect.bottom - blH,
        )
        lineTo(rect.left, rect.top + tlH)
        cubicTo(
            rect.left, rect.top + tlH * (1f - k),
            rect.left + tlW * (1f - k), rect.top,
            rect.left + tlW, rect.top,
        )
        close()
    }
}

private fun crownPath(rect: Rect): Path {
    val points = arrayOf(
        30f to 4f, 36f to 16f, 52f to 12f, 40f to 24f, 46f to 36f,
        30f to 28f, 14f to 36f, 20f to 24f, 8f to 12f, 24f to 16f,
    )
    return Path().apply {
        points.forEachIndexed { index, (x, y) ->
            val px = rect.left + x / 60f * rect.width
            val py = rect.top + y / 40f * rect.height
            if (index == 0) moveTo(px, py) else lineTo(px, py)
        }
        close()
    }
}
