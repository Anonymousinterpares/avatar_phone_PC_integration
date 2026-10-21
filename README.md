# Bluey

A small character on your Windows PC, driven from an Android phone. The phone is the microphone and the face. The PC holds the login, the screen, and the mouse.

## PC

Double-click `Run-Bluey.cmd` in this folder.

The first run installs the .NET build and starts Bluey. Later runs start it again. You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) once.

When it is running you get:

- a blueberry at the bottom of the screen
- a Bluey icon in the tray, next to the clock

If Windows Firewall asks, allow Bluey on private networks. That is the phone connection.

### Cerebras

The key is already in `.env` as `CEREBRAS_API_KEY`. That file stays on this machine.

1. Right-click the tray icon.
2. Choose **Sign in and keys…**
3. Set **Mode** to **Cerebras (qwen-3.8-27b)**.
4. The Cerebras key box is filled from `.env`. Click **Save**.

Cerebras answers on the PC when you let go of the phone. It does not keep a live voice socket open.

### OpenRouter

Qwen 3.8 27B, Qwen 3.8 Flash, Omni Flash, and Gemini Flash Lite, billed through your OpenRouter balance. Put the key in `.env` as `OPENROUTER_API_KEY`.

1. Right-click the tray icon and choose **Sign in and keys…**
2. Set **Mode** to **OpenRouter**.
3. Set **OpenRouter model**. **Qwen provider** applies only to **Qwen 3.8 27B (pick a host)**: ModelRun (faster), DekaLLM (cheaper), or NEAR AI (cheaper). The other models use OpenRouter `:nitro` and ignore that host list.
4. Click **Save**.

For Qwen 3.8 27B, Bluey pins that provider and does not fall back to another host. The phone still turns speech into text. The PC sends the same tools and screenshots.

**Let him use the computer** stays off until you check it in the tray. Ctrl+Alt+S stops him. Ctrl+Alt+Space wakes or sleeps him.

### Spoken replies

Optional. Bluey can read his answers aloud on this PC with Supertonic 3. The voice stays on the computer. No cloud speech call.

1. Install Python 3 if it is not already there.
2. Run `py -3 -m pip install supertonic`.
3. Right-click the tray icon. Check **Speak replies**. Under **Voice sex**, choose **Male** or **Female**.
4. The choice is written to `bluey.config.json` in this folder and loaded again every time Bluey starts.

Male uses Supertonic voice M1. Female uses F1. The first spoken line downloads the model, about 400 MB, into your user cache. `lang` in that same file can be `na`, `en`, or `pl`. `na` lets the model handle mixed language.

While speech is on, Bluey is told to wrap the line you should hear in `[[spoken]]` and `[[/spoken]]`. Only the words inside those marks are read aloud. The bubble still shows the whole reply, without the marks.

## Android

The phone app installs from an APK. There is no store listing.

1. Double-click `Build-Phone.cmd`. It writes the APK to `android\app\build\outputs\apk\debug\app-debug.apk`. You need the Android SDK once. Android Studio’s SDK at `%LOCALAPPDATA%\Android\Sdk` is enough.
2. Copy that APK onto the phone (USB, Drive, or mail it to yourself).
3. Open the APK on the phone and install it. If Android blocks it, allow installs from that app (Files, Chrome, or Gmail).
4. Or, with the phone plugged in and USB debugging on: `adb install -r android\app\build\outputs\apk\debug\app-debug.apk`

## Connect the phone to the PC

### On Local Wi-Fi
1. Put the phone and the PC on the same Wi-Fi.
2. Start the PC app with `Run-Bluey.cmd` and leave it running.
3. Open Bluey on the phone. It looks for the PC and connects by itself.
4. If it stays on “Looking for your PC…”, right-click the tray icon, choose **Show connection address**, and type that `host:port` into the phone. Tap **Connect**.
5. Double-tap the face to wake him. Hold the big button to talk, then let go.

### Over the Internet with Tailscale
1. Install [Tailscale](https://tailscale.com/) on both your PC and phone and sign in to the same account.
2. In the PC tray, right-click Bluey and choose **Show connection address**. One of the addresses listed will be your Tailscale IP (e.g. `100.x.y.z:port`).
3. Type that `100.x.y.z:port` into the PC address field on the phone and tap **Connect**. Your connection is encrypted end-to-end via WireGuard.

## Remote Desktop & Password Protection

You can view and control your PC screen live from your phone:

1. **Set a password on PC**: Right-click the Bluey tray icon and select **Set desktop password…**. Enter your secret password and click **Save**. (Leave empty if you don't want password protection).
2. **Access from phone**: Once connected to the PC, tap **Remote Desktop** on the phone.
3. If a password is set, the app prompts you to enter it before granting screen access.
4. **Live Controls & Tweaks**:
   - **Settings (⚙)**: Live sliders to tweak:
     - **FPS** (1 to 30)
     - **JPEG Quality** (10% to 95%)
     - **Resolution scale** (10% to 100%)
     - **Grayscale switch** (cuts bandwidth by ~3× on cellular or poor connections)
   - **Touch Interaction & Zoom**:
     - Single-finger tap: left-click.
     - Single-finger hold (>400ms): right-click.
     - Single-finger drag: move PC cursor smoothly.
     - Two-finger pinch: zoom in and out (1.0x to 5.0x).
     - Two-finger drag: pan across the zoomed display.
     - Top bar `+`, `−`, and `100%` buttons for quick zoom controls.
     - Past 120% zoom, the phone tells the PC which part of the full desktop is on screen. The PC sends that whole area at native resolution and JPEG quality 95.
   - **Password Security**:
     - "Remember password" option caches credentials securely on the device.
     - Session caching prevents repeated password prompts during the same connection.
   - **Full Screen & Orientation**:
     - Automatic landscape mode matching PC widescreen aspect ratio.
     - `🔄` button toggles between landscape and portrait.
     - Fullscreen immersive mode hides system bars by default across the entire app.
   - **Keyboard (⌨)**: Toggle the on-screen typing bar to send text or Enter to the focused window.
   - **← Back**: Returns to Bluey's face mode.

