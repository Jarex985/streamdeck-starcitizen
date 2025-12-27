# streamdeck-starcitizen (Updated Fork)

**Elgato Stream Deck button plugin for Star Citizen**

> 🔗 **This is an updated fork of [mhwlng/streamdeck-starcitizen](https://github.com/mhwlng/streamdeck-starcitizen)**  
> The original project is archived. This fork adds improved auto-detection, search functionality, and bug fixes.

## What's New in This Fork

- **Automatic RSI Launcher Detection** - (Hopefully) No more manual path configuration! The plugin reads your RSI Launcher settings automatically.
- **Search Functionality** - Quickly find keybindings with the new search box in the Property Inspector.
- **Simplified Configuration** - Only need to set `SCBasePath` if auto-detection fails (instead of SCData_p4k and SCClientProfilePath).
- **Bug Fixes & Improvements** - Various fixes to improve stability and usability.

See [CHANGELOG.md](CHANGELOG.md) for full details.

## Cosmetic Key (Visual Only)
 The **Cosmetic Key** action is a visual-only tile for your Stream Deck.
		
### What it does
  - Appears in the Stream Deck action list under the **Star Citizen** category.
  - **Does not send any keys or actions** when pressed.
  - Useful for:
  - Labels
  - Images / icons / separators
  - Visual layout / organization on profiles
  ### Notes
  - This is intentionally cosmetic and does not interact with Star Citizen.

## State Memory (Soft Sync Toggle)
The **State Memory** button is designed for toggleable systems in Star Citizen that have no reliable API feedback.
		
### How it works
  - **Short Press:** Sends the selected Star Citizen keybind and flips the Stream Deck indicator (ON ↔ OFF).
  - **Long Press:** Flips only the Stream Deck indicator (no key sent), allowing manual re-sync if the in-game state changed externally.
  - Optional **Short Press Sound** and **Long Press Sound** provide tactile feedback.

### Key characteristics
 - Remembers its last state across presses
 - Manual “soft sync” via long press to correct mismatches
 - Ideal for:
 - Landing gear
 - Ship lights
 - VTOL
 - Doors or power toggles

 ### Example use case
  If you turn off ship lights in-game with a cockpit switch, the Stream Deck may show “ON.”  
  Hold the button (long press) to resync it — no key is sent, but your deck stays visually aligned.


## Momentary Button
  The Momentary Button is designed for immersive, one-shot actions in Star Citizen where visual feedback matters, but no persistent state is required. Unlike a toggle button, the Momentary Button does not maintain an on/off state.

  ### How it works
  - The user selects a Star Citizen function (same system as the Static Button).
  - Two images are configured directly in the Stream Deck UI:
  - **Idle Image** – default appearance.
  - **Active Image** – shown temporarily when the button is pressed.
  - When pressed:
  - The configured Star Citizen keybind is sent once.
  - The button image immediately switches to the Active Image.
  - After a user-defined delay (in milliseconds):
  - The button automatically reverts to the Idle Image.
  - No second press is required.

  ### Key characteristics
  - One-shot action (non-toggle)
  - Temporary visual feedback only
  - Fully configurable delay
  - Uses the same function selector and search system as the Static Button
  - Supports drag-and-drop images via the Stream Deck UI

  ### Example use cases
  - Engine start
  - Landing gear activation
  - Powering ship systems
  - System priming or initialization
  - Any action where a brief visual cue improves immersion




---
# Original README:

**At least streamdeck software version 6 is required.**

This plugin gets the key bindings from the Star Citizen game files.

The bindings in the streamdeck plugin are automatically updated when changing bindings in Star Citizen options screen.

The bound key is shown in the dropdown, localised for the current keyboard language (So: US keyboard shows WSAD, French keyboard shows ZSQD)

**The plugin does not contain any button images or ready made streamdeck profiles.**

Credit goes to https://github.com/SCToolsfactory/SCJMapper-V2 and https://github.com/dolkensp/unp4k for all the code to get the `defaultProfile.xml` from the p4k file etc.

The static button works in a similar way, to the streamdeck 'Hotkey' button type.
So, there is only one image and there is no game state feedback for these buttons.
The differences with the 'Hotkey' buttons are, that it gets the keyboard binding from the game.
When the stream deck button is pushed, the 'key down' event is sent to the keyboard
and only after the stream deck button is released, the 'key up' event is sent to the keyboard.

The plugin's multi-action button behaviour is different : when the stream deck button is pushed, the 'key down' event is sent to the keyboard.
After a user-definable delay (default = 40 ms) the 'key up' event is sent to the keyboard. 
Nothing happens when the streamdeck button is released.

Both the static- and multi-action buttons can be used inside the streamdeck built-in multi-action button's action list.

The multi-action button can also be used as a regular streamdeck button, in case a fixed user-definable delay is required between the key down and key up events.

A sound can be played when pressing a button.

**You can clear the sound path, by clicking on the label in front of the file picker edit box.**

You can, for example, use the 'multi-action switch' function, that is built into the streamdeck software, to set up a toggle function.
You can add the relevant function of this plugin to both the ON-and OFF-action of the 'multi-action switch' function.
You can then set up different images for each toggle state.
The disadvantage is: that if you would press e.g. the gear up/down toggle button while the ship is still on the ground/powered off, 
then the button image would be out of sync.

The plugin also has a Dial button for use with the 4 dials on the Streamdeck+ model.

There are 5 bindings (They must be keyboard bindings, you can't bind the mouse wheel!) :

- Dial Clockwise
- Dial Counter-Clockwise
- Dial Press
- Touch screen press
- Touch screen long press

![Dial Image](https://i.imgur.com/MjcGque.png)

When a dial is rotated, the 'key down' event is sent to the keyboard once. 
When you let go of the dial for at least 100ms : the 'key up' event is sent to the keyboard. 

When a dial button is pushed, the 'key down' event is sent to the keyboard. 
When a dial button is released, the 'key up' event is sent to the keyboard. 

When the touch screen is pressed or long-pressed, the behaviour is like the multi-action button :
The 'key down' event is sent to the keyboard. After a user-definable delay (default = 40 ms) the 'key up' event is sent to the keyboard. 

After you install the plugin in the streamdeck software, then there will be new button types in the streamdeck software.

Choose a button in the streamdeck software (drag and drop), then choose a Star Citizen function for that button 
(that must have a keyboard binding in Star Citizen. **A mouse, gamepad or joystick binding won't work!**) 
and then choose any picture for that button.

Add an image to a button in this way:

![Button Image](https://i.imgur.com/xkgy7uZ.png)

Animated gif files are supported. Dial images are 200x50

When the plugin is first started, it finds and opens the game file :

`C:\Program Files\Roberts Space Industries\StarCitizen\LIVE\Data.p4k`

and extracts `defaultProfile.xml` and also english text resources. This could take more than 10 seconds.

**The plugin automatically detects Star Citizen installations using multiple methods:**

1. **Registry-based detection** - Scans Windows registry for launcher installation paths
2. **Common path scanning** - Checks standard installation directories
3. **Steam integration** - Detects Steam installations
4. **Manual configuration** - User can specify paths in config file

The plugin logs its path detection process in `%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin\pluginlog.log`.

## Manual Configuration

If automatic detection fails, edit `%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin\appsettings.config`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<appSettings>
  <!-- Base Star Citizen installation directory -->
  <add key="SCBasePath" value="C:\Program Files\Roberts Space Industries\StarCitizen" />

  <!-- Path to Data.p4k file (contains game assets and default keybindings) -->
  <add key="SCData_p4k" value="C:\Program Files\Roberts Space Industries\StarCitizen\StarCitizen\LIVE\Data.p4k" />

  <!-- Path to user profile directory (contains custom keybindings) -->
  <add key="SCClientProfilePath" value="C:\Program Files\Roberts Space Industries\StarCitizen\StarCitizen\LIVE\USER\Client\0\Profiles\default" />

  <!-- Set to true for PTU (Public Test Universe) instead of LIVE -->
  <add key="UsePTU" value="false" />
</appSettings>
```

Compressed versions (files ending in .scj) are cached in the plugin directory and should be automatically refreshed, the next time Star Citizen is updated to a new version AND the plugin is also restarted.

You can also delete the .scj files and restart the plugin, to extract the files from the p4k file again.

For easier debugging, installation and testing, `defaultProfile.xml`, `keybindings.csv`, `joystickbindings.csv`, `mousebindings.csv`, `unboundactions.csv` and `PropertyInspector\StarCitizen\Static.html` files are created in the plugin directory.

The plugin uses all the active keyboard bindings from `defaultProfile.xml` and then overrules some of the bindings, with any custom keyboard bindings from this file :

`C:\Program Files\Roberts Space Industries\StarCitizen\LIVE\USER\Client\0\Profiles\default\actionmaps.xml`

The `PropertyInspector\StarCitizen\Static.html` file is dynamically updated, in case more custom keyboard bindings were added to `actionmaps.xml`, 
that didn't have any corresponding keyboard bindings in `defaultProfile.xml`.

If nothing happens, when pressing streamdeck buttons: you could try to start streamdeck.exe as administrator.

The plugin installer is here: https://github.com/mhwlng/streamdeck-starcitizen/releases

To install the plugin, double click the file `com.mhwlng.starcitizen.streamDeckPlugin` which should install the plugin.

(This only works, if the plugin not already installed. Otherwise you will need to uninstall or remove the plugin first.)

This .streamDeckPlugin file is a zip file and the contents are simply copied to :

`%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin`

To update to a new version :

Stop the Stream Deck application:

`c:\Program Files\Elgato\StreamDeck\StreamDeck.exe`

Then delete the `%appdata%\Elgato\StreamDeck\Plugins\com.mhwlng.starcitizen.sdPlugin` directory. (make a backup copy first)

Then start the streamdeck software again.

Then double click the file `com.mhwlng.starcitizen.streamDeckPlugin` as usual.

MAKE SURE that you save any images, profiles etc. that you put in these directories yourself, BEFORE deleting the directory.
And put them back after the installation.
The plugin installer doesn't come with button images.

Also, the plugin can be uninstalled and the directory completely deleted by right-clicking on any button and selecting uninstall (make a backup copy first) :

![Button Image](https://i.imgur.com/BAkEuEq.png)

The button configurations are not stored in the plugin directory.

After uninstalling and re-installing the plugin, all the button definition should still be there.

The com.mhwlng.starcitizen.sdPlugin directory contains a pluginlog.log file, which may be useful for troubleshooting.

Thanks to :

https://github.com/BarRaider/streamdeck-tools

https://github.com/SCToolsfactory/SCJMapper-V2

https://github.com/dolkensp/unp4k

https://github.com/ishaaniMittal/inputsimulator

https://nerdordie.com/product/stream-deck-key-icons/
