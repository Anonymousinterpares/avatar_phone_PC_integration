# How to use Bluey

This is the home guide. It is for the person at the computer and the person with the Android phone.

Bluey lets the phone see the computer screen and use the mouse. The computer and the phone do not have to be in the same house. Tailscale makes a private link between them over the internet.

A single installer program for the computer is planned for when this is handed to someone else. That installer is not ready yet. Until then, the computer steps below use the Bluey folder.

## The two people

- **At the computer.** This person installs Tailscale and starts Bluey. A blueberry sits at the bottom of the screen. A Bluey icon sits next to the clock.
- **With the phone.** This person installs Tailscale and the Bluey phone app, then connects to that computer.

Use the same Tailscale login on both. That puts the computer and the phone on one private network.

## 1. Install Tailscale on the computer

1. On the computer, open [https://tailscale.com/download/windows](https://tailscale.com/download/windows).
2. Download the Windows installer and open it.
3. When it finishes, sign in. Google, Microsoft, or an email address all work.
4. Leave Tailscale running. Its icon next to the clock should show that it is connected.

Tailscale is free for personal use.

## 2. Install Tailscale on the phone

1. On the Android phone, open the Play Store.
2. Search for **Tailscale**.
3. Install the app made by Tailscale.
4. Open it and sign in with the **same account** used on the computer.
5. Turn the connection on. The phone should say it is connected.

The Play Store page is [Tailscale for Android](https://play.google.com/store/apps/details?id=com.tailscale.ipn).

## 3. Start Bluey on the computer

1. Open the Bluey folder.
2. Double-click `Run-Bluey.cmd`.
3. The first time can take a minute. Leave the window alone until Bluey starts.
4. If Windows asks about the firewall, allow Bluey on private networks. The phone needs that permission.

You should then see:

- a blueberry at the bottom of the screen
- a Bluey icon next to the clock

To stop Bluey, right-click that icon and choose **Quit**.

## 4. Choose a screen password

Do this on the computer before the phone opens the screen.

1. Right-click the Bluey icon next to the clock.
2. Choose **Set desktop password…**
3. Type a password you can say to the person with the phone.
4. Click **Save**.

Tell that person the password by a call or in person. They need it to open the screen.

## 5. Install Bluey on the phone

The phone app is a file, not a store app.

1. Copy `android\app\build\outputs\apk\debug\app-debug.apk` from the Bluey folder onto the phone. Mail, a cable, or a drive all work.
2. On the phone, open that file.
3. If Android blocks the install, allow installs from the app you used to open the file. That is often Files, Chrome, or Gmail.
4. Install Bluey and open it.

## 6. Connect the phone to the computer

Tailscale must say connected on the computer and on the phone. Bluey must be running on the computer.

1. On the computer, right-click the Bluey icon.
2. Choose **Show connection address**.
3. Find the address that starts with `100.`. It looks like `100.20.30.40:54321`.
4. Copy the **whole** address. The numbers after the colon belong to it.
5. On the phone, type that address into **PC address**.
6. Tap **Connect**.
7. Wait until the phone says it is connected.

If several addresses are shown, use the one that starts with `100.`. The others are for the same house only.

The address usually ends with `:47821`. It stays the same each time Bluey starts. If that number is different, another program was already using it. Type the address exactly as it is shown.

## Help someone at their computer

Use this when you are away and the other person is sitting at the computer. You can see their screen, talk to them through their speakers, and use the mouse only after they agree.

You each need Tailscale, and Bluey must be running on their computer.

If you both sign in to Tailscale with the same account, skip the share steps. If you each have your own Tailscale account:

1. On the computer, open [https://login.tailscale.com/admin/machines](https://login.tailscale.com/admin/machines).
2. Find that computer in the list. Open its menu and choose **Share**.
3. Type the helper's email address and send the invite.
4. The helper opens the email and accepts it. Tailscale on the helper's phone must say connected.

### Let a helper watch

On the computer:

1. Right-click the Bluey icon next to the clock.
2. Choose **Allow help…**
3. A window shows a short code. Read that code to the helper. It works one time, for 10 minutes.
4. Leave that window open.

On the phone:

1. Connect to the computer with the `100.` address, the same way as in step 6 above.
2. Tap **Help someone**.
3. Type the code and tap **Watch**.

You can see the screen. Touches do not click. Two fingers still zoom the picture so you can look closer. Past 120% zoom, that part of the screen becomes sharper.

The computer window changes to say a helper is watching. **Stop help** on that window ends it. So does **Cancel** before anyone has joined.

### Talk through their speakers

On the phone, press **Hold to talk** and speak. Let go when you are done. They hear you on the computer speakers.

While you hold the button, the computer's sound stops playing on your phone. That keeps your voice from bouncing back out of their speakers.

### Use their mouse

1. On the phone, tap **Request control**.
2. On the computer, a window asks whether to allow the mouse and keyboard. The helper cannot press this for them.
3. They press **Allow** or **No**.

If they press **Allow**, you can click, drag, and type for 10 minutes. They can take the mouse back at any time:

- Press **Take the mouse back** on the Bluey help window, or
- Press **Ctrl+Alt+H** on their keyboard.

Either one returns you to watching only. **Stop help** closes the screen on the phone.

If they press **No**, you keep watching and can ask again.

## 7. Open the computer screen on the phone

1. On the phone, tap **Remote Desktop**.
2. Type the screen password from step 4.
3. Tap **Connect**.
4. Tick **Remember password on this phone** if this phone should not ask again.

The phone now shows the computer screen, and touches on the phone use that computer:

- One short tap is a left click.
- A long press is a right click.
- One finger drag moves the mouse.
- Two fingers pinch to zoom. Two fingers drag to move around while zoomed.
- The **+** and **−** buttons zoom as well. The percentage button returns to the normal view.
- Past 120% zoom, the part of the screen you are looking at becomes sharper.
- The keyboard button types on the computer.
- **Back** leaves the screen and returns to the blueberry.

The computer's sound can be played on the phone from the sound button on that screen.

## In the same house, without Tailscale

If the phone and the computer are on the same Wi-Fi, you can skip Tailscale.

1. Start Bluey on the computer.
2. Open Bluey on the phone. It looks for the computer by itself.
3. If it stays on “Looking for your PC…”, right-click the Bluey icon, choose **Show connection address**, and type one of the addresses into **PC address**. Tap **Connect**.

## When something does not work

- **The phone cannot find the computer.** Check that Bluey is still next to the clock, and that Tailscale is connected on both devices. Type the `100.` address again.
- **Wrong password.** Set it again from **Set desktop password…** on the computer, then type the new one on the phone.
- **The picture is frozen.** On the computer, right-click Bluey, choose **Quit**, then double-click `Run-Bluey.cmd` again. Connect the phone again.
- **Windows blocked the phone.** Allow Bluey on private networks when the firewall asks. If you already chose Block, open Windows Security, go to Firewall, allow Bluey, and start Bluey again.
