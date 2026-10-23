# Bluey

A small character on your Windows PC, driven from an Android phone. The phone is the microphone and the face. The PC holds the login, the screen, and the mouse.

Two guides:

- [Instructions.md](Instructions.md) is the home guide. It is written for someone who is not setting up software every day: Tailscale, the phone app, the screen password, and helping a person who is sitting at the computer.
- This page is the full map. It includes the same ideas, plus how to build Bluey and which answer service to turn on.

## Talk to him

Start the PC app, then open Bluey on the phone and connect. You should see a blueberry at the bottom of the screen and a Bluey icon next to the clock.

1. Double-tap his face to wake him. Double-tap again to put him to sleep. **Ctrl+Alt+Space** does the same from the PC.
2. Hold his face, or the Hold button, and speak. Let go to send.
3. The answer stays for about 12 seconds, on the phone and in a bubble next to him on the PC.
4. If you cannot speak, tap **Manual typing** on the phone, type, and tap **Send**. The tray also has **Type an ask…**

Ask "what's this?" with the mouse on the thing you mean. He looks, then moves over to it. Say "stop pointing" or "go home" to send him back. Sleeping him also sends him to the bottom of the screen.

## Who answers

Bluey uses one answer service at a time. Right-click the tray icon, choose **Sign in and keys…**, set **Mode**, and click **Save**. The tray icon then says that mode and either "ready" or "needs login".

There are two ways he hears you:

- **Grok live** and **OpenAI live** keep a live voice connection. While you hold, the phone sends your voice to that service. The real key stays on the PC. The phone receives a short-lived token.
- **ChatGPT plan**, **Claude**, **Cerebras**, and **OpenRouter** use the phone's own speech recognizer. The phone turns speech into text. When you let go, the PC sends that text and gets the answer back. These modes do not open a live voice socket.

Keys you paste in the tray are saved for the current Windows user in `%AppData%\Bluey\settings.bin`. Cerebras and OpenRouter can also be filled from a `.env` file in this folder when their tray box is empty. That file stays on this machine. Git ignores it. The other modes are typed into the tray, or, for ChatGPT plan, signed in through the browser.

### Grok live (xAI API key)

This is the mode Bluey starts in.

1. Right-click the tray icon and choose **Sign in and keys…**
2. Set **Mode** to **Grok live (xAI API key)**.
3. Paste the key into **xAI API key** and click **Save**.

Voice uses `grok-voice-latest`. Written follow-ups use `grok-4.7`. Bluey reads the API key. A SuperGrok subscription is a separate bill and is not used here.

### OpenAI live (API key)

1. Right-click the tray icon and choose **Sign in and keys…**
2. Set **Mode** to **OpenAI live (API key)**.
3. Paste the key into **OpenAI API key** and click **Save**.

Voice uses `gpt-realtime-2.1`. Written follow-ups use `gpt-6.1-sol`. This mode uses the API key. The ChatGPT subscription is the next mode.

### ChatGPT plan

1. Right-click the tray icon and choose **Continue with ChatGPT**.
2. A browser window opens. Sign in and allow plan usage.
3. Bluey switches to **ChatGPT plan** and shows the account email.

The model is `gpt-6.1-sol`. The phone turns speech into text, and the PC asks ChatGPT when you let go. **Sign out of ChatGPT** in the tray ends that login.

### Claude (API key)

1. Right-click the tray icon and choose **Sign in and keys…**
2. Set **Mode** to **Claude API key**.
3. Paste the key into **Anthropic API key** and click **Save**.

The model is `claude-sonnet-4-5`. The phone turns speech into text, and Claude answers on the PC.

**Hand the ask to Claude Code** and **Hand the ask to Cursor** are separate. Those tray items send a typed task to the coding tool installed on this PC. They do not use the Claude API key above.

### Cerebras

Cerebras was added in this Windows and Android project. The modes above were already in the original integration.

1. Put a line in `.env` in this folder: `CEREBRAS_API_KEY=your-key`
2. Right-click the tray icon and choose **Sign in and keys…**
3. Set **Mode** to **Cerebras (qwen-3.8-27b)**.
4. The Cerebras box is filled from `.env` when it was empty. You can paste a key there instead. Click **Save**.

The model is `qwen-3.8-27b`. The phone turns speech into text. Cerebras answers on the PC when you let go.

### OpenRouter

OpenRouter bills Qwen and Gemini models through one balance.

1. Put a line in `.env` in this folder: `OPENROUTER_API_KEY=your-key`
2. Right-click the tray icon and choose **Sign in and keys…**
3. Set **Mode** to **OpenRouter**.
4. Set **OpenRouter model**. The key box is filled from `.env` when it was empty. Click **Save**.

**Qwen provider** applies only to **Qwen 3.8 27B (pick a host)**: ModelRun (faster), DekaLLM (cheaper), or NEAR AI (cheaper). Bluey pins that host and does not fall back to another one.

The other models use OpenRouter `:nitro` and ignore that host list:

- Qwen 3.8 Flash Next
- Qwen 3.8 Flash
- Qwen 3.8 Omni Flash
- Gemini 3.5 Flash Lite
- Gemini 3.1 Flash Lite

The phone still turns speech into text. The PC sends the same tools and screenshots.

## Let him use the computer

This switch is the same for every mode above. It stays off until you check **Let him use the computer** in the tray. Until then he can look and point, and he will not click.

With it on, you can ask him to open an app or a site, click, type, press a shortcut, scroll, or drag. He moves his own cursor, then puts your pointer back. Before sending, deleting, buying, or closing unsaved work, he says what he is about to do and waits. He will not type a password, and he will not press Win+L or Ctrl+Alt+Delete.

**Ctrl+Alt+S** stops him and sends him home.

**No touch**, on the tray, on the phone, or on a right-click of the blueberry, is for when you are away from the computer and only want to hear what is on the screen. While it is on, the prompt does not include computer commands at all. He can look and point at what he describes. He will not move the Windows mouse, click, type, scroll, or open anything. If you ask him to do one of those anyway, he says no-touch mode is on and he cannot do that task, and he looks sad. With speech off, a reply stays on screen for at least four seconds before the next one replaces it. With speech on, the next reply waits until that line has finished playing. A long reply scrolls inside the bubble once, following the sound as it actually plays, and stays at the bottom when that line ends. Pauses on commas and periods hold the text, and if the sound waits, the text waits with it. With speech off, it scrolls once during the four seconds and then stays. If speech stalls, the reason is appended to `%AppData%\Bluey\tts.log`. The switches stay in step, and the choice is remembered on this PC. Turn it off when you want him to use the computer again. **Let him use the computer** still has to be on for that. The reply sits beside him, not on top of him.

Asking about a fact that may have changed, such as the weather or the next public holiday, uses web search in the background. It does not open a browser. **Web search** in **Sign in and keys…** starts on the same provider as **Mode**. Change it and click **Save** to keep another provider. That choice is stored in `%AppData%\Bluey\web-search.json` and loaded again next time. OpenRouter searches with its own web tool and the OpenRouter key. Grok, OpenAI, and a ChatGPT plan sign-in use their own search. Claude and Cerebras do not search.

## Spoken replies

Optional. Bluey can read his answers aloud on this PC with Supertonic 3. The voice stays on the computer. There is no cloud speech call.

1. Install Python 3 if it is not already there. On the first page of the installer, turn on "Add python.exe to PATH".
2. Double-click `Setup-Voice.cmd` in this folder. It looks for the same Python Bluey starts. If Supertonic is missing, it asks before installing it and downloading the voice, about 400 MB, into your user folder. If you say no, Bluey still runs and does not speak.
3. Right-click the tray icon. Check **Speak replies**. Under **Voice sex**, choose **Male** or **Female**.
4. **Test TTS voice (speak sample)** says a short line when the voice is ready. The tray line **TTS:** shows whether it is ready.

The same install by hand is `py -3 -m pip install supertonic`. The choice is written to `bluey.config.json` and loaded again every time Bluey starts. Male uses Supertonic voice M1. Female uses F1. The voice files stay in your user cache, not in this repo.

`lang` in that file picks the spoken language. `na` lets the model handle mixed language. Other codes it accepts are `ar`, `bg`, `cs`, `da`, `de`, `el`, `en`, `es`, `et`, `fi`, `fr`, `hi`, `hr`, `hu`, `id`, `it`, `ja`, `ko`, `lt`, `lv`, `nl`, `pl`, `pt`, `ro`, `ru`, `sk`, `sl`, `sv`, `tr`, `uk`, and `vi`.

While speech is on, Bluey marks one line for the voice with `[[spoken]]` and `[[/spoken]]`. You hear that line. The bubble still shows the whole reply, without the marks.

## On this PC

Double-click `Run-Bluey.cmd` in this folder.

The first run installs the .NET build and starts Bluey. Later runs start it again. You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) once.

If Windows Firewall asks, allow Bluey on private networks. That is the phone connection.

Right-click the tray icon for the rest:

| Menu item | What it does |
| --- | --- |
| **Sign in and keys…** | Picks Grok, OpenAI, ChatGPT plan, Claude, Cerebras, or OpenRouter, and stores the key. |
| **Continue with ChatGPT** | Browser sign-in for the ChatGPT plan. |
| **Let him use the computer** | Allows clicks, typing, and the other computer actions. |
| **No touch** | He only describes what is on the screen. The same switch is on the phone. |
| **Show usage hints** | Opens a short panel of the same talk and pointing examples. |
| **Speak replies** and **Voice sex** | Reads one marked line aloud. |
| **Avatar…** | Size (large is the biggest), walk speed at 100%, 150%, or 200%, walk or teleport, which corner he sleeps in, and whether a reply sits in the caption bar or a comic-book cloud. |
| **Send him home** | Parks the blueberry at the bottom of the screen. |
| **Hide the blueberry** | Takes him off the screen without quitting. The same Hide is on a right-click of the dot, along with Talk, Go to sleep, and No touch. Left-drag moves him. Talk opens a box. Type and press Enter. Double-click the box to close it. |
| **Phone on the left / center / right** | Where his eyes rest. |
| **Allow help…** | Lets another phone, or another computer running Bluey, watch, talk, and ask for the mouse. |
| **Help another computer…** | Opens a window that connects to someone else's Bluey. No phone is required. |
| **Set desktop password…** | Password for the phone's remote desktop. Leave it empty to turn the password off. |
| **Show connection address** | The `host:port` to type on the phone. |
| **Quit** | Closes Bluey. |

## On the phone

The phone app is a file. It is not in a store.

1. Double-click `Build-Phone.cmd`. It writes the APK to `android\app\build\outputs\apk\debug\app-debug.apk`. You need the Android SDK once. Android Studio's SDK at `%LOCALAPPDATA%\Android\Sdk` is enough.
2. Copy that APK onto the phone (USB, Drive, or mail).
3. Open the APK on the phone and install it. If Android blocks it, allow installs from the app you used to open the file (Files, Chrome, or Gmail).
4. Or, with the phone plugged in and USB debugging on: `adb install -r android\app\build\outputs\apk\debug\app-debug.apk`

## Connect the phone to the PC

### On the same Wi-Fi

1. Put the phone and the PC on the same Wi-Fi.
2. Start the PC app with `Run-Bluey.cmd` and leave it running.
3. Open Bluey on the phone. It looks for the PC and connects by itself.
4. If it stays on "Looking for your PC…", right-click the tray icon, choose **Show connection address**, and type that `host:port` into the phone. Tap **Connect**.

### From somewhere else, with Tailscale

The computer and the phone do not have to be in the same house. [Tailscale](https://tailscale.com/) puts them on one private network. Install it on both, sign in to the same account, and leave it connected. The click-by-click version is in [Instructions.md](Instructions.md).

1. On the PC, right-click Bluey and choose **Show connection address**.
2. Use the address that starts with `100.`. It looks like `100.20.30.40:47821`.
3. Type that whole address into **PC address** on the phone and tap **Connect**.

Bluey prefers port `47821`, so the address stays the same across restarts. If that port is already taken, the window shows a different port. Type the address exactly as it is shown. The link is encrypted by Tailscale.

## Help someone at their computer

Use this when you are away and the other person is sitting at the computer. You can see their screen, talk through their speakers, and use the mouse only after they agree. Both of you need Tailscale, and Bluey must be running on their computer. If you use two Tailscale accounts, share the computer with the helper from the Tailscale admin page first. [Instructions.md](Instructions.md) has those steps.

On their computer: right-click Bluey, choose **Allow help…**, and read the short code on the bar. It works one time, for 10 minutes. The bar can be dragged or hidden. Hiding it does not end the session.

On the phone: connect with the `100.` address, tap **Help someone**, type the code, and tap **Watch**. Touches do not click yet. Two fingers still zoom. Past 120% zoom, that part of the screen becomes sharper.

Press **Hold to talk** and let go when you are done. They hear you on the computer speakers. Muting the computer's sound sends that sound to the phone and leaves the speakers quiet. Your voice still comes out of the speakers, and so do Bluey's spoken replies. While you hold the button, the computer's sound stops on your phone, so your voice does not bounce back out of their speakers.

Tap **Request control**. The bar on their computer asks them to press **Allow** or **No**. The helper cannot press this. **Allow** lasts 10 minutes. They can take the mouse back with **Take the mouse back** or **Ctrl+Alt+H**. **Stop help** closes the screen on the phone. The bar can be dragged out of the way or hidden.

### From another computer

The helper can use Bluey on their own computer instead of a phone. Both computers run Bluey. Tailscale is still how they reach each other when they are not on the same Wi-Fi. The click-by-click version is in [Instructions.md](Instructions.md).

1. On the computer that needs help, choose **Allow help…** and read the code. Choose **Show connection address** and read the address that starts with `100.`.
2. On the helper's computer, choose **Help another computer…**. Type that address and the code, then click **Connect**.
3. The window shows their screen at that computer's own resolution, JPEG quality 95, so text stays readable. Clicks do nothing until they press **Allow**.
4. **Hold to talk** speaks through their speakers. **Hear their sound** plays their computer audio here. **Request control** asks them to press **Allow**. After that, the mouse and keyboard work only while this window is in front.
5. **Disconnect** leaves. **Stop help** on their computer ends it too. The picture goes blank as soon as their side closes the connection. If the link drops on its own, this window retries for 2 minutes without a new code.

## Remote desktop

You can also open the PC screen from the phone as yourself, after you set a password.

1. On the PC, right-click Bluey and choose **Set desktop password…**. Type a password and click **Save**. Leave it empty if you do not want a password.
2. On the phone, once it is connected, tap **Remote Desktop**.
3. If a password is set, type it. Tick **Remember password on this phone** if this phone should not ask again during later visits. The same connection also remembers it for the rest of that visit.

On that screen:

- One short tap is a left click. Hold one finger still past 0.4 seconds for a right click. One finger drag moves the mouse.
- Two fingers pinch to zoom, from 1.0x to 5.0x. Two fingers drag moves around while zoomed. **+**, **−**, and the percentage button do the same. The percentage button returns to the normal view.
- Past 120% zoom, the phone tells the PC which part of the desktop is on screen. The PC sends that area at the screen's own resolution and JPEG quality 95.
- The gear opens live sliders: frames per second from 1 to 30, JPEG quality from 10% to 95%, and resolution scale from 10% to 100%. Grayscale cuts the amount of data by about three times on a slow connection.
- The keyboard button types into the focused window on the PC.
- The rotate button switches landscape and portrait. The app hides the phone's system bars.
- The sound button plays the computer's sound on the phone.
- **Back** returns to Bluey's face.

## Origin

This project started from [bluey-by-riley](https://github.com/rbrown101010/bluey-by-riley) by [Riley Brown](https://github.com/rbrown101010). That repository is the original Mac menu-bar app and iPhone app. Its commits were made by Riley Brown and co-authored with Claude. This repository is the later Windows PC app and Android phone app. Its git history begins at that port, so Riley's commits are not in this clone.

## License

[The Unlicense](LICENSE). The same dedication is in the original repository. Anyone may copy, modify, and distribute this software. It is provided as is, without warranty.
