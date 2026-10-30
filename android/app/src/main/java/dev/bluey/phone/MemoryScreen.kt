package dev.bluey.phone

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import org.json.JSONObject

@Composable
fun MemoryScreen(
    body: String,
    onClose: () -> Unit,
    onAction: (String) -> Unit,
) {
    val parsed = remember(body) { runCatching { JSONObject(if (body.isBlank()) "{}" else body) }.getOrNull() }
    var armForget by remember { mutableStateOf(false) }
    val items = parsed?.optJSONArray("items")
    val backups = parsed?.optJSONArray("backups")
    val note = parsed?.optString("note").orEmpty()
    val problem = parsed?.optString("error").orEmpty()
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black)
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Text("What he remembers", color = Color.White, fontSize = 22.sp, fontWeight = FontWeight.SemiBold)
        Text(
            "This list stays on the PC. A habit does not click or open anything while No touch is on, or while computer use is off.",
            color = Color(0xFFB9B2CC),
            fontSize = 13.sp,
        )
        if (problem.isNotEmpty()) Text(problem, color = Color(0xFFF3A6D8), fontSize = 14.sp)
        if (parsed?.optBoolean("tooNew") == true) {
            Text("This file is from a newer Bluey. It was left as it is.", color = Color(0xFFF3A6D8), fontSize = 14.sp)
        }
        if (parsed?.optBoolean("unreadable") == true) {
            Text("The memory file could not be read. Load a backup below.", color = Color(0xFFF3A6D8), fontSize = 14.sp)
        }
        if (note.isNotEmpty()) Text(note, color = Color.White, fontSize = 14.sp)
        if (items == null || items.length() == 0) {
            Text("Nothing yet.", color = Color(0xFFB9B2CC), fontSize = 15.sp)
        } else {
            for (index in 0 until items.length()) {
                val item = items.optJSONObject(index) ?: continue
                val id = item.optString("id")
                val kind = item.optString("kind")
                val use = item.optString("use")
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .background(Color(0xFF1E1B2E), RoundedCornerShape(12.dp))
                        .padding(12.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    Text(item.optString("text"), color = Color.White, fontSize = 15.sp)
                    Text(item.optString("source"), color = Color(0xFFB9B2CC), fontSize = 12.sp)
                    if (kind == "never") {
                        Button(onClick = { onAction("forget|$id") }) { Text("Forget") }
                    } else {
                        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            UseButton("Just do it", use == "just_do_it" || (kind == "like" && use == "use")) {
                                onAction("use|$id|" + if (kind == "like") "use" else "just_do_it")
                            }
                            UseButton("Ask first", use == "ask_first") { onAction("use|$id|ask_first") }
                            UseButton("Off", use == "off") { onAction("use|$id|off") }
                        }
                        Button(onClick = { onAction("forget|$id") }) { Text("Forget") }
                    }
                }
            }
        }
        Button(onClick = { onAction("backup") }, modifier = Modifier.fillMaxWidth()) { Text("Save a copy") }
        Button(
            onClick = {
                if (armForget) {
                    armForget = false
                    onAction("forgetAll")
                } else {
                    armForget = true
                }
            },
            modifier = Modifier.fillMaxWidth(),
        ) { Text(if (armForget) "Tap again to forget habits and likes" else "Forget everything") }
        Text("Backups on this PC", color = Color.White, fontSize = 16.sp)
        if (backups == null || backups.length() == 0) {
            Text("No backup yet.", color = Color(0xFFB9B2CC), fontSize = 13.sp)
        } else {
            for (index in 0 until backups.length()) {
                val copy = backups.optJSONObject(index) ?: continue
                val name = copy.optString("name")
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .background(Color(0xFF1E1B2E), RoundedCornerShape(12.dp))
                        .padding(12.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    Text(copy.optString("label") + " · " + copy.optInt("count") + " notes", color = Color.White, fontSize = 14.sp)
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        Button(onClick = { onAction("restore|$name|replace") }) { Text("Replace") }
                        Button(onClick = { onAction("restore|$name|add") }) { Text("Add") }
                    }
                }
            }
        }
        Button(onClick = onClose, modifier = Modifier.fillMaxWidth()) { Text("Back") }
    }
}

@Composable
private fun UseButton(label: String, on: Boolean, click: () -> Unit) {
    Button(
        onClick = click,
        colors = androidx.compose.material3.ButtonDefaults.buttonColors(
            containerColor = if (on) Color(0xFF4254D6) else Color(0xFF2A2A40),
        ),
    ) { Text(label) }
}
