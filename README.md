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

## Android

The phone app installs from an APK. There is no store listing.

1. Double-click `Build-Phone.cmd`. It writes the APK to `android\app\build\outputs\apk\debug\app-debug.apk`. You need the Android SDK once. Android Studio’s SDK at `%LOCALAPPDATA%\Android\Sdk` is enough.
2. Copy that APK onto the phone (USB, Drive, or mail it to yourself).
3. Open the APK on the phone and install it. If Android blocks it, allow installs from that app (Files, Chrome, or Gmail).
4. Or, with the phone plugged in and USB debugging on: `adb install -r android\app\build\outputs\apk\debug\app-debug.apk`

## Connect the phone to the PC

1. Put the phone and the PC on the same Wi-Fi. Mobile data alone will not see the PC.
2. Start the PC app with `Run-Bluey.cmd` and leave it running.
3. Open Bluey on the phone. It looks for the PC and connects by itself.
4. If it stays on “Looking for your PC…”, right-click the tray icon, choose **Show connection address**, and type that `host:port` into the phone. Tap **Connect**.
5. Double-tap the face to wake him. Hold the big button to talk, then let go.

The address changes when you restart the PC app. The phone finds the new one on the same Wi-Fi. If it does not, use **Show connection address** again.
