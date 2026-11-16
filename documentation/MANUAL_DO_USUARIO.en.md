# User manual — Meu Editor de Vídeo

This manual explains how to install, use and configure Meu Editor de Vídeo. It is written for people who have never edited video before. Menu and button names are the same ones shown on screen.

Text in **bold** is the name of a menu, button or option. The › symbol separates menu levels: **File › Export…** means open the File menu and choose Export.

## Getting started

Meu Editor de Vídeo runs on 64-bit Windows 11. To install it, use one of the two setup files:

- **MeuEditorDeVideo-…-Setup.exe**: full installer. Recommended for most people.
- **MeuEditorDeVideo-…-x64.msi**: Windows Installer package, for those who prefer the standard Windows installation.

Run the file, follow the screens and, at the end, open the program from the Start menu. Nothing else needs to be installed separately.

> If Windows shows the "Windows protected your PC" warning, click "More info" and then "Run anyway". This warning appears for programs without a digital signature certificate.

### Computer requirements

These are the requirements set in the product specification:

- **Minimum:** 64-bit Windows 11, Intel Core i5 or AMD Ryzen 5 processor, 16 GB of RAM, Direct3D-compatible graphics card and an SSD with at least 512 GB.
- **Recommended:** 64-bit Windows 11, Intel Core i7 or AMD Ryzen 7 processor, 32 GB of RAM or more, NVIDIA RTX or AMD Radeon graphics card with 8 GB of video memory or more and a 1 TB NVMe SSD or larger.

A dedicated graphics card is not required for the program to work, but it makes preview, export and the artificial intelligence features much faster.

### When the program opens

The first time (or if any preference is missing or empty in the Windows registry), the **Welcome to Meu Editor de Vídeos** window appears. Fill in **Full Name**, **System language**, **Theme** (Light, Dark or Follow the operating system) and **Check monthly for new updates** (Yes or No), then click **Save preferences**. All fields are required and the window only closes after saving. If you choose another language, the program restarts in it. You can change these preferences later in **Tools › Settings… › General**.

The program opens straight into the editing screen, with a new empty project called "Untitled". If there is a copy of a project that was not closed properly, the program asks whether you want to recover it (see the section Saving, autosave and recovery).

The last five projects you opened appear in the **File** menu, between separators. Click one to open it. If the file was moved or deleted, the program tells you and removes it from the list.

### Creating a project

1. Choose **File › New project…**.
2. Type a name in "Project name".
3. Choose the folder where the project will be stored in "Project folder" (use **Browse…** if you like).
4. Choose a "Project template". Templates already set the recommended resolution, aspect ratio and frame rate. There are templates for horizontal video (Full HD, 4K and cinema), vertical and square, and templates for platforms such as YouTube, Instagram, TikTok, WhatsApp and Facebook.
5. If you prefer, adjust the resolution, aspect ratio, frame rate (FPS) and color profile manually.
6. In "Media storage", choose an option:
   - **Link media (files stay where they are)**: the project stores only the file paths. It is the lightest option, but the files must stay in the same place.
   - **Package media into the project (self-contained .med)**: the media is copied into the project. Use it if you want to take the project to another computer.
7. Click **Create project**.

### Opening an existing project

There are three ways:

- Choose **File › Open…** and select the .med file.
- Click one of the recent projects in the **File** menu.
- Double-click the .med file in File Explorer. The program opens the project automatically.

## The editing screen

After you create or open a project, the editing screen appears. It is divided into areas:

- **Top bar** (at the top of the window): on the left, the **File**, **Edit**, **View**, **Project**, **Tools** and **Help** menus and the **Save**, **Undo** and **Redo** buttons; in the center, the project name; on the right, **Theme**, **Render queue**, **Export** and the buttons to minimize, maximize and close the window. Drag the bar to move the window and double-click it to maximize or restore.
- **Media library** (on the left): at the top are the tabs Media Library, Videos, Images, Audio, Graphic Elements, Texts, Transitions, Effects, Color and AI. Hover over a tab to see its full name. Imported media appears in a grid, with thumbnail and duration.
- **Preview** (center, top): shows the result of the video. At the top are the preview resolution, the Proxy button and the **Maximize monitor** button, which makes the preview cover the library and the timeline (the Properties panel stays visible; click again to restore); at the bottom, the position bar (drag to go to any point), the play, pause and frame-by-frame buttons, the volume, frame capture (saves the current frame as PNG or JPEG, at the project resolution; also in **File › Export current frame…**) and full screen.
- **Properties** (on the right): shows the information and settings of the selected item. It is divided into tabs: **Project** (resolution, frame rate, duration, number of media and clips, file), **Media**, **Video**, **Text**, **Composition**, **AI**, **Audio**, **Effects**, **Color** and **History**. In the bottom-right corner, the **User manual** and **Virtual assistant** shortcuts open the help with one click.
- **Timeline** (at the bottom): where you assemble the video. Each horizontal row is a track. There is a toolbar right above it.

> When the project name shows the "● not saved" mark, there are changes that have not been saved yet. Use **File › Save** or Ctrl+S.

### Menus

- **File**: new project, **new from template**, open, import media, import image sequence, save, save as, create backup, export, render queue, export current frame (PNG or JPEG) and the five recent projects.
- **Edit**: undo and redo; select all; split; **join cuts** (undoes a split: click the ruler over the cut point; only available when both sides continue the same media, with no different settings); cut, copy, paste at the playhead and duplicate; delete, delete and close the gap (only on the clip's track) and **delete and join** (closes the gap on all tracks, keeping them in sync; locked tracks do not move and the operation is refused if a clip on another track occupies part of the removed range).
- **View**:
  - **Layout mode**: **Default**; **Timeline** (the timeline takes the full width); **Video** (the monitor takes the right half, at full height).
  - **Playback quality**: Automatic, Full, 1/2 or 1/4.
  - **Video zoom level**: Fit (fits the monitor) or 10% to 400%; above the monitor size, scroll bars appear.
  - **Play/Pause**, **Stop** (goes back to the point where playback started) and **Loop playback range** (repeats the timeline selection or, without a selection, the whole project).
  - **Ruler**: rulers in video pixels at the top and left of the image.
- **Project**: package media into the project, **auto reframe**, **save as template**, **adjustment layer**, **compound clip**, **split screen**, **multicam** and locate missing media (see [Adjustment layers, compound clips, split screen and multicam](#adjustment-layers-compound-clips-split-screen-and-multicam)).
  - **Save as template…** stores a copy of the saved project in your templates. **File › New from template…** picks a template (yours or from a pack), asks where to save the new project and opens the copy; the template stays intact.
  - **Auto reframe…** changes the project format (9:16 for Reels, TikTok and Shorts; 1:1; 4:5; 16:9) and repositions every video and image clip to cover the new frame. In **Follow**, choose **Face**, **Full body** or **Frame center**. To follow the person, the program uses the background removal model and creates position keyframes, which you can adjust later in the Video tab. Texts stay as they are. Progress appears in the **AI** tab, and everything is undone with a single Ctrl+Z.
- **Tools**: audio mixer, subtitle editor, import and export subtitles, **text to speech**, **analyze media**, **record screen, webcam and microphone**, **content packs**, AI models and settings.
  - **Record screen, webcam and microphone…** records the desktop, a webcam in a corner of the screen and the sound of a microphone, all at the same time, into an MP4 file. With more than one monitor, choose in **Screen to record** which screen to record (or all of them). The webcam takes one tenth of the width of the recorded screen. When you click **Record**, the program is minimized and a 5, 4, 3, 2, 1 countdown appears in the center of the screen before recording starts. To stop, press **F10** (it works with any program in the foreground); the program comes back. The recording goes to the **Recordings** folder (next to the project or in Videos › Meu Editor de Vídeo), enters the library (Recordings category) and, if the option is checked, the timeline at the playhead. Without the screen, it records only the webcam.
  - **Content packs…** installs, exports and removes packs of stickers, titles and project templates (.medpack files). A pack is a ZIP with the folders `figurinhas` (PNG, GIF, WebP, JPEG or SVG), `titulos` (.medtitle) and `modelos` (.med) and, optionally, a `pacote.json` with the name (for example `{"nome": "Summer Party"}`). Items you save yourself go into the **My items** pack; use **Export…** to share it.
  - **Text to speech…** turns text into narration with the voices installed in Windows, without internet. Type the text, choose the **Voice** and the **Speed** and click **Generate and insert**: the audio goes to the library (Narration category) and is placed at the playhead, on the first free audio track or on a new track called Narration. For more voices, install them in **Windows Settings › Time & language › Speech**.
  - **Analyze media** tags all the media in the library: type, orientation (horizontal, vertical, square), resolution (HD, Full HD, 4K), duration (short, long) and sound. With the models installed, also **speech** or **no speech**, the number of **scenes** and **person** or **no people**. Tags appear when you hover over the media, and the library's **Search…** box also searches them.
- **Help**: **User manual** (F1), **Virtual assistant** (Shift+F1), **Keyboard shortcuts…**, **System information…**, **Open logs folder**, **Third-party licenses**, **About Meu Editor de Vídeo…**, **Check for updates** (asks over the internet whether there is a new version: it shows "You have the latest version!" or the **New version available** window, with the version, languages, supported systems, author and the download links on the Microsoft Store and the AplicativosIA.Online website; clicking **Close this window** stores the check date). In **About Meu Editor de Vídeo…**, the LinkedIn icon opens the author's profile in the browser.

The **User manual** opens this manual inside the program, without internet. The list on the left takes you to each topic and Ctrl+F searches for a word. The window can stay open while you edit.

**Keyboard shortcuts…** shows the shortcuts in use, including the ones you changed. **System information…** shows the version, Windows, the processor, memory, graphics card, AI models and program folders; the **Copy** button puts everything on the clipboard, useful when asking for help.

## Importing media

Importing brings into the project the files you will use, such as videos, photos, music and recordings. The original files are not changed.

### Supported formats

- **Video**: MP4, M4V, MOV, MKV, WebM, AVI, WMV, FLV, MPG and MPEG, M2TS, MTS, TS, 3GP, OGV, MXF and VOB.
- **Audio**: MP3, WAV, AAC, M4A, FLAC, OGG, OGA, Opus, WMA, AIFF, AIF, AC3 and AMR.
- **Image**: JPG and JPEG, PNG, BMP, TIF and TIFF, WebP, TGA, JFIF and SVG.
- **Animated GIF**: GIF.
- **Image sequence**: a series of numbered photos, such as photo_0001.png, photo_0002.png and so on.

Files in other formats are refused, with the message "Unsupported file format.".

### How to import

1. Click **Import Media** in the library panel, or use **File › Import media…** (shortcut Ctrl+I).
2. Choose one or more files and confirm.
3. Follow the progress. The message "Importing 3 of 10" shows how many files have been read.

You can also drag files or folders from File Explorer and drop them on the media library. When the file is over the right area, the hint "Drop to import" appears.

For an image sequence, use **File › Import image sequence…**. The program recognizes the numbering automatically.

Each imported media item gets a thumbnail. When you select a media item, the **Media** tab of the properties shows the duration, resolution, frame rate and other file data.

### Organizing the library

- Use the **Search…** box to find media by name, category or the tags created in **Tools › Analyze media** (for example "speech", "person" or "vertical").
- Right-click a media item to **Rename…**, **Set category…**, **Remove category**, **Add to timeline** or **Remove from project**.
- Removing media from the project does not delete the file from the computer.

### Missing media

If a file was moved, renamed or deleted, the project shows the warning "Some project media were not found" when opened. The warning also appears in the status bar as "Missing media", with the number of affected media. To fix it:

1. Click **Locate…** to point to the file, or **Search in folder…** for the program to search the folders and subfolders you choose.
2. If you do not want to locate them now, click **Continue without the media**. The clips are marked as missing and can be located later.

You can open this window at any time in **Project › Locate missing media…**. In a project downloaded from the internet, the program does not look for media or LUTs stored on another computer on the network, because merely contacting that computer would send it your Windows sign-in data: such media appear as missing, and such LUTs are turned off. If you trust where the project came from, locate the media in this window and turn the LUTs back on in the **Properties** panel.

To avoid this problem when taking the project to another computer, use **Project › Package media into the project…**. It copies the media into the project file.

### Proxies (light versions for editing)

A proxy is a light 540p copy of a large video, used only to make the preview smoother. Export always uses the original file.

- To create a proxy, right-click the video in the library and choose **Generate editing proxy (540p)**.
- To delete a proxy, choose **Delete proxy**.
- In the monitor, the **Use proxies in preview** option turns proxy use on or off.
- In **Tools › Settings…**, you can have proxies generated automatically for videos above 1080p.

## Editing on the timeline

The timeline is where the video is assembled from left to right. The **playhead** (a vertical line) shows the instant being seen in the preview. The pieces of media placed on the tracks are called clips.

### Placing media on the timeline

- Drag a media item from the library to a track.
- Or select the media in the library and click **Add to timeline** on the toolbar. Images and audio go in at the playhead: an image goes to a free Image track, otherwise to an empty video track; audio goes to an audio track that is free at that point. If none fits, the program creates a new track.

### Moving, trimming and splitting

- **Move**: drag the clip to another place on the track or to another track of the same type.
- **Trim the start or end**: drag the edge of the clip to shorten or lengthen the part used.
- **Split**: place the playhead at the desired point and click **Split** on the toolbar, or press S. The clip is cut in two at that point.
- **Join cuts** (Edit menu): joins the two parts of a split clip back together. Place the playhead over the cut point; it only works when both parts continue the same media, with no different settings.
- **Delete**: select the clip and press Delete. You can also use the **Delete selection** button.
- **Delete and close gap**: deletes the clip and pulls the following clips to fill the gap. Use Shift+Delete.
- **Crop and zoom**: select a video or image clip and click the crop button on the timeline button bar. In the window that opens, drag on the frame to mark the area that should stay in the video: drag inside the area to move it, use the corner and edge handles to resize it, or drag outside it to draw a new one. The time bar moves through the clip so you can pick a reference frame. Under **Aspect ratio**, choose **Free**, the project ratio, the original ratio or a fixed one (16:9, 9:16, 1:1, 4:3, 3:4, 4:5); with a fixed ratio, only the corners resize. **Size** shows the area in video pixels. When you click **Apply**, the marked area is enlarged to fill the frame, centered; if its ratio differs from the project's, bars remain. The clip's rotation and its position and scale keyframes are removed, and the project size does not change. **Reset** goes back to the whole frame. Ctrl+Z undoes it.
- **Add simple text**: the **T** button on the timeline button bar creates a simple text at the playhead and opens the **Text** properties tab so you can type and format it.
- **Drawing tools**: the shapes button opens a menu with **Rectangle**, **Arrow**, **Ellipse**, **Triangle**, **Line** and **Polygon**. The chosen shape is placed at the playhead, lasting 5 seconds, on a free graphics track, and the **Video** properties tab opens so you can adjust size, color and outline.
- **Copy, cut, paste and duplicate**: use Ctrl+C, Ctrl+X, Ctrl+V and Ctrl+D. When pasting, the clip goes to the playhead position.

### Magnet and ripple editing

- **Snapping magnet** (N key): when on, the clip "sticks" to the edges of other clips and to the playhead, which makes it easy to fit parts together without leaving gaps. The button is on the toolbar.
- **Ripple editing**: when on, moving or trimming a clip pushes the following clips, without leaving holes.

### Tracks

Tracks are the horizontal rows of the timeline. To add a track, click the **Add track** button and choose the type:

- **Video track**
- **Audio track**
- **Image track**
- **Text track**
- **Subtitle track**
- **Effects track**

Each track has three buttons in its header on the left:

- **Hide**: hides the track in the preview.
- **Mute**: silences the audio track.
- **Lock**: prevents changes to the track, so you do not disturb what is already finished.

Right-click the track name to **Rename track…** or **Remove track**. To change the track order, drag a track's name up or down (or use **Move track up** and **Move track down** in the same menu). The top track appears in front.

### Grouping and linking

- **Group** (Ctrl+G): joins several clips so they move together. Use **Ungroup** (Ctrl+Shift+G) to separate them.
- **Link audio and video**: makes a video's audio follow the video. Use **Unlink audio and video** to separate them.

### Clip speed

Right-click the clip and choose **Speed…** to make it faster or slower. In the **Video** tab of the properties, the speed list goes from 0.25× (slow motion) to 8× (fast).

### Enabling and disabling clips

Right-click the clip and choose **Enable/disable clip**. A disabled clip stays on the timeline (dimmed and with the ⊘ symbol), but it is not seen or heard in playback and export.

### Adjustment layers, compound clips, split screen and multicam

- **Adjustment layer** (**Project › Add adjustment layer**): creates a 5-second clip at the playhead on an effects track. The effects and color correction placed on it apply to **all tracks below**, for the duration of the clip. Select the layer and add effects in the **Effects** and **Color** tabs. The layer's opacity doses the effect and masks limit the area. Stretch or move the layer like any clip.
- **Compound clip** (**Project › Create compound clip…** or context menu): joins the selected clips (with their linked audio) into a single clip, which moves, trims and takes effects as one. The original tracks and positions are kept inside it. **Undo compound clip** returns the clips to the timeline, on new tracks, with only the part the compound clip showed. The compound clip's sound only plays at normal speed.
- **Split screen** (**Project › Split screen**): select 2 to 4 video or image clips, each on a different track, and choose a layout: **2 side by side**, **2 stacked**, **3 columns**, **3 rows**, **1 large and 2 small** or **4 in a grid**. Each clip (in track order, top to bottom) is enlarged to cover its part of the frame and cropped at the excess. Adjust the position and crop afterwards in the Video tab.
- **Multicam** (**Project › Multicam**): put each camera on a video track, synchronized (for example, with **Sync by audio**). During playback or when stopped, choose **Camera 1** to **Camera 4** (Alt+1 to Alt+4, with focus on the timeline) to show that camera **from the playhead on**: the program cuts all cameras at the playhead and disables the others. Camera 1 is the highest video track with a clip at the playhead. The audio does not change.

### Clip color

To identify clips visually, select one or more clips, right-click and choose **Clip color**. The color is saved in the project and can be undone with Ctrl+Z. Choose **Automatic (by type)** to return to the default color (video, image, audio or text).

Projects with clip colors use format 1.3. Earlier versions of the program open these projects with a warning and ignore the colors.

### Markers

Markers are reminders on the time ruler, useful for noting important points, such as the start of a line of dialogue.

- Place the playhead and click **Add marker** or press M.
- Right-click the marker to **Rename marker…** or **Remove marker**.

### Timeline zoom

Use the zoom buttons on the toolbar, or the shortcuts:

- Zoom in: Ctrl + plus key (or numeric keypad +)
- Zoom out: Ctrl + minus key (or numeric keypad -)
- Fit to window: Shift+Z

### Undo and redo

- **Undo** (Ctrl+Z) and **Redo** (Ctrl+Y or Ctrl+Shift+Z) step back and forward through the last actions.
- The **History** tab in the properties shows the list of actions taken. Click one to return the project to that point.

### Navigating the video

- Space: plays or pauses.
- Left and right arrows: go back or forward one frame.
- Shift with the arrows: go back or forward one second.
- Up and down: go to the previous and next cut.
- Home and End: go to the start and the end.

## Visual effects, text and transitions

To apply an effect, select the clip on the timeline and use the property tabs. You can also use the library sections, such as **Effects**, **Color** and **Transitions**: double-click an item to apply it at the playhead or to the selected clip, or use the **Apply to selected clip** button. Effects and transitions also work on images, stickers and texts, and the **Color** tab adjusts the colors of images and stickers. When you insert a clip, the properties panel opens on the right tab by itself: **Text** for texts, **Video** for videos, images and stickers, and **Audio** for audio.

### Video tab

This tab gathers the clip's motion and timing settings:

- **Transform**: X and Y position, scale, rotation and opacity. Next to each value there is a ◇ button that creates a keyframe. You can also do this directly in the monitor: with the clip selected on the timeline, drag it inside the video to move it, drag the round handle above the box to rotate it (with Shift, in 15° steps) and, on images, stickers, videos and shapes, drag a corner to resize it. Texts only move and rotate.
- **Keyframes**: a keyframe stores a value at a specific instant. When there are keyframes, the program changes the value gradually between them. Use **Previous keyframe** and **Next keyframe** to move between them, and **Interpolation** to choose the curve: Smooth (ease in and out), Linear, Ease in, Ease out or Hold (jump).
- **3D (perspective)**: X turn, Y turn and depth, to give the clip a sense of depth. The **3D card flip** button makes the clip turn like a card. It is not a full 3D model.
- **Ready-made animations**: motion effects ready to apply, such as enter from the left, zoom in, Ken Burns (slow zoom), pulse, spin, sway, bounce and fade in and out.
- **Crop**: crops the edges of the video on the left, top, right and bottom. The controls only hide the edges; the project size stays the same. For the video to really take the cropped size, click **Apply crop to video**: the project takes the size of the cropped area, in pixels of the original video (a 1920×1080 recording without 25% on the left becomes 1440×1080), the cropped area fills the frame and the export comes out with those dimensions. Texts and other clips follow. The clip cannot be rotated or have position or scale keyframes. Ctrl+Z undoes it.
- **Blend**: blend mode with other layers, such as Multiply, Screen, Overlay, Soft light, Add, Darken, Lighten and Difference.
- **Fades**: **Fade in** and **Fade out**, from 0 to 10 seconds, so the clip appears and disappears smoothly.
- **Speed**: preset speeds or another speed. Below 1×, the **Smooth slow motion** option blends neighboring frames to reduce jumps. **Play in reverse** shows the clip from end to start, with the linked audio reversed too (also in the clip's context menu; the clip gets the ◀ symbol on the timeline). It does not apply to freeze frames. On long videos, use proxies so reverse playback stays smooth.
  - **Slow motion by interpolation (optical flow)**: below 1×, creates the in-between frames following the motion, which is smoother than simple blending. It needs no model, but makes playback and export slower.
  - **Curve**: speed curve (speed ramping). Choose **Montage**, **Hero**, **Bullet**, **Jump**, **Speed up** or **Slow down** for the clip to speed up and slow down within itself, without changing the duration or the part of the video used. **Constant (no curve)** returns to normal. With a curve, the clip's audio is muted. Trimming or splitting the clip also returns it to constant speed.
  - **Motion blur**: motion trail when position, scale or rotation are animated by keyframes. At 100%, the trail covers the span of one whole frame.
- **Freeze frame**, **Repeat section**, **Fill frame**, **Rotate 90°** and **Flip** horizontally and vertically.
- **In transition**: applies a transition between this clip and the clip before it on the same track. To use it, the two clips must touch. You choose the transition and the **Transition duration**. If no clip touches it from before (a lone image, sticker or text), the transition is an entrance: it starts at the beginning of the clip and reveals it over the tracks below.

- **Camera tracking**: choose **Head** or **Body**, and a virtual camera follows that part of the person. Adjust:
  - **Auto cut (zoom)**: brings the image closer to the target.
  - **Follow movement**: how much the camera follows the target (at 0%, it stays in the center).
  - **Shake**: camera shake around the target.
  - **Blurred background**: blurs everything that is not the person.

  Tracking uses the same person cutout model as the **AI** tab (install it in **AI Models** if you do not have it yet). Without the model or without a person in the frame, the camera stays in the center and the background is not blurred. The effect also appears in the list of applied effects in the **Effects** tab.
- **Retouch**: retouching of the person's face, frame by frame. Click **Beauty**, **Makeup** or **Blur faces** to add the effect to the clip; the settings appear right below.
  - **Blur faces**: finds the face in each frame and hides it with **Blur**, **Pixelate** or **Black bar**. Adjust the **Intensity** and the **Area size**. Works with one face per frame.
  - **Beauty**: **Skin · smooth** and **Skin · brighten**, **Expression lines** and **Under-eye bags** (smooth those areas), and shape adjustments: **Face · slim**, **Eyes · enlarge**, **Nose · slim**, **Mouth · size** and **Eyebrows · raise** (negative values do the opposite).
  - **Makeup**: choose a ready-made **Look** (Natural, Glamour, Night or Rosy) or **None**, and adjust each item: **Lashes**, **Eye shadow**, **Eyeliner**, **Eyebrows**, **Lipstick**, **Blush**, **Contour**, **Highlights**, **Eye color** and **Freckles**. Eye shadow, eyebrows, lipstick, blush and iris have their own color. An item with a value above 0% uses its own value and color; at 0%, the look's setting applies.

  Retouching uses the **Face Mesh V2** model, which locates 478 points of the face. Install it in **Tools › AI Models › Download models…** (about 5 MB). If the person cutout model is installed, it helps find the head in the frame. Without the Face Mesh model or without a visible face, the frame stays as it is. Retouching works best with the face looking forward and taking up a good part of the image.

The available transitions are: Hard cut, Dissolve, Fade to black and back, Zoom, Slide (left, right, up and down), Push (left and right), Wipe (left, right, up and down), Circle, Diamond, Clock and Blinds.

### Effects

The **Effects** tab has two parts. At the top, a grid with a thumbnail of each effect, showing the result on a sample landscape: click one to add the effect to the clip. At the bottom, the **Applied effects** list, in the order in which the effects are processed. Each effect in the list has sliders for its settings, and the **Move up**, **Move down** and **Remove effect** buttons. The arrow to the left of the name shows or hides the settings. Double-click a slider to return to the default value. In color settings, a colored strip under the slider shows the effect of the value (for example, blue to orange for temperature).

Available visual effects:

- **Blur**, **Sharpen**, **Vignette**, **Black and white**, **Sepia**, **Negative**, **Pixelate**, **Grain**, **Posterize** and **Soft glow**.
- **Chroma key**: removes a green, blue or other chosen background. It has tolerance, transition softness, edge smoothing, color spill removal and the option to make the background transparent or a solid color.
- **Refine edges**: shrinks or expands the edge of the transparency, smooths it and cleans the color of the edges. Use it after chroma key or a mask.
- **360° reframe**: turns a 360° video (equirectangular, with width equal to twice the height) into a regular view. Adjust the **Direction**, **Tilt**, **Roll** and **Field of view** and choose the **View aspect ratio** (16:9, 9:16, 1:1, 4:3 or 4:5). Create keyframes on these values for the virtual camera to move through the video.
- **Lens correction**: corrects the curvature of straight lines caused by wide-angle lenses and action cameras (negative values) or by telephoto lenses (positive). **Zoom to hide edges** enlarges the image to remove the empty corners.
- **Video noise reduction**: smooths the grain of low-light recordings. **Preserve detail** keeps more texture.
- **Audio visualizer**: bars (normal or mirrored) or a wave that dance with the sound of a song. When adding it, choose the song from the library; it must be on the timeline. Adjust the number of bars, height, color and opacity. Use it on a video or image clip (for example, a background).

In the **Effects** and **Color** tabs, the **Compare before/after in the monitor** option shows, to the left of a line in the monitor, the video without effects and without color correction, so you can compare. Drag the line's handle right or left to change the split.

The cards of options already applied to the clip have a highlighted border. Double-clicking an applied card removes the option: it removes the effect or, for a color preset, the adjustments it created (as long as you have not changed them).

### Color

The **Color** tab starts with the **Presets**: Black and white, Noir, Cinema, Vivid, Warm, Cool, Faded, Retro and Light and airy. Click a thumbnail to apply the style to the clip. The preset enters the list of applied corrections as regular adjustments, which you can change or remove; **Undo** (Ctrl+Z) reverts everything at once.

Below, in **Color tools** (and in the **Color** section of the library), are the color correction tools:

- **Color adjustments**: exposure, brightness, contrast, saturation, temperature, tint (green or magenta), hue, shadows and highlights.
- **White balance**: indicate the color that should be neutral gray, and the program corrects the cast.
- **Curves**: tone curves for the whole video and per channel (red, green and blue).
- **Color wheels**: separate adjustment of shadows, midtones and highlights.
- **3D LUT (.cube)**: applies a color table in .cube format, widely used to give a video a style. Choose the intensity with the slider.
- **HSL**: adjusts hue, saturation and luminance of each color range (red, orange, yellow, green, cyan, blue, purple and magenta) without touching the others, for example only the blue of the sky. Grays, black and white do not change.
- **HDR**: HDR look, which recovers shadows and highlights and strengthens details. It has intensity, shadows, highlights, detail and vibrance.
- **Match color**: matches the color and contrast of one clip to another's. Select the clip and use **Match color with previous clip** in the **AI** tab: the program compares three frames of each clip and creates the effect, with gain and offset per channel. Adjust the **Intensity** for a middle ground.

### Masks, tracking and stabilization

These features are in the **Composition** tab. They work for videos, images, texts and graphic elements.

- **Masks**: create a shape with **+ Rectangle**, **+ Ellipse** or **+ Freehand** (points, with Ctrl+click in the monitor). The mask crops what is visible of the clip. You can add, subtract or intersect masks, invert the mask and feather the outline.
- **Color mask (effects only)**: when checked, the mask does not crop the clip; it limits color correction and effects to its area (for example, brighten only the face or blur only a license plate). The feathered edge of the mask makes the transition.
- **Shape key**: with the **◇ Shape key** button, the mask changes shape over time.
- **Motion tracking**: mark an object in the monitor, click **Track** and the program follows that object. Another clip can then **Follow** the motion, with the **Follow** option. Use **Release** to turn it off.
- **Stabilization**: **Analyze and stabilize** measures the camera motion and compensates for shakes. Adjust the **Smoothness** and the image **Zoom**. Use **Remove stabilization** to undo.

### Graphic elements and shapes

In the **Graphic Elements** section of the library, double-click a shape to insert it at the playhead. The available shapes are: Rectangle, Rounded rectangle, Circle, Line, Arrow, Star, Hexagon, Speech bubble and Heart. You can adjust the fill and outline color in the **Composition** tab.

You can also import a vector graphic with the **Import SVG…** button.

**Stickers** shows the stickers from installed packs. The program comes with the **Basic** pack, with 96 stickers: common ones (arrows, highlight circle, check and cross, speech bubbles, heart, star, emojis, Subscribe and Like buttons, badges such as New and Sale, among others), geometric shapes (circle, square, rectangle, triangle, diamond, pentagon and hexagon), map pins with a square, round, hexagonal, diamond, star, heart or triangle top, and city point-of-interest pins (airport, bus, train and metro stations, hospital, police, fire station, school, museum, park, restaurant, hotel, among others). It cannot be removed, but it can be exported. Click one to insert it at the playhead for 3 seconds, at 40% size (adjust in the Video tab), on a free image track (or a new track called Stickers). The image also enters the library, in the Stickers category.

### Texts and subtitles

### Inserting texts

In the **Texts** section of the library, double-click a template to insert it at the playhead:

- **Simple text**: bold text with a soft shadow.
- **Title**: large title in the center, with a zoom entrance.
- **Lower third**: name and role in the lower left corner, with a box and accent bar.
- **Rolling credits**: list of credits that scrolls up the screen during the clip.
- **Subtitle**: subtitle on the subtitle track, with a dark box at the bottom of the frame.

Select the text on the timeline to edit it. The **Text** tab of the properties has:

- **Content**: the text. Press Enter to break the line.
- **Font**: Windows fonts or fonts added to the program with **+ Font** (.ttf or .otf files).
- **Bold**, **Italic** and **Underline**.
- **Colors** of the text, outline, background box and shadow. Use the #RRGGBB or #AARRGGBB format to set the color.
- **Alignment** and **Anchor point**.
- **In animation** and **Out animation**: for example, fade, slide or zoom.

The text's position, scale, rotation and opacity are in the **Video** tab.

To reuse a finished text (style, animations and duration), select it and use **Save selected text as title…** in the Texts section (or in the clip's context menu). It appears in **Saved titles**; choose it and click **Insert saved title** to place it at the playhead. Titles from installed packs appear in the same list.

### Subtitles

Subtitles are timed texts, organized on the subtitle track. To work with them:

- **Tools › Import subtitles (SRT/VTT)…**: brings in a ready-made subtitle file.
- **Tools › Export subtitles (SRT/VTT)…**: saves the project's subtitles as an SRT or VTT file.
- **Tools › Subtitle editor**: opens a window to see and edit all the subtitles. You can add, remove and edit the text and times, go to a specific subtitle and shift all of them at once, in seconds.
- **Generate subtitles**: creates subtitles automatically from speech. See the Artificial intelligence section.

When exporting the video, you can choose between saving a subtitle file next to the video or drawing the subtitles on the video itself (burned-in subtitles).

## Audio

### Audio mixer

Open the mixer in **Tools › Audio mixer** or with the toolbar button. It shows a column for each audio track, with:

- **Volume** (fader) and **pan** (left and right). Double-click the fader or the pan to return to the default.
- **M** (mute): silences the track.
- **S** (solo): leaves only the solo tracks playing.
- Level meters, which move during playback.

### Clip Audio tab

Select an audio clip, or a video with sound, and use the **Audio** tab:

- **Volume**: 100% is the original volume. With the ◇ button, you create a volume keyframe. This way you can lower the music during speech, for example.
- **Fade in** and **Fade out**: fades from 0 to 10 seconds, so the sound starts and ends smoothly.
- **Normalize loudness**: adjusts the volume to a loudness target (EBU R128 standard), without exceeding a safe peak. Choose the **Loudness target** before applying.
- **Remove silences**: cuts the silent parts of the clip. Adjust the **Threshold**, below which sound is considered silence, and the **Minimum duration** of the silence.
- **Beats**: with the music selected, **Mark beats** creates a marker on each beat and **Cut videos on beats** splits the video and image clips that play along with it at each beat (for rhythm montages). Adjust the **Sensitivity**: higher finds more beats.
- **Duck under speech**: with the music selected, click **Duck under speech** and the program lowers its volume where there is speech on the other tracks (narration, interview). Choose the **Reduction** (default −12 dB) and the **Transition** (duration of the fall and rise). The result is volume keyframes, which you can adjust; **Remove ducking** deletes this animation. With the speech detection model installed, detection is more precise; without it, the program uses the sound level.
- **Sync by audio**: if you recorded the same moment with two cameras or with a sound recorder, select both clips and click **Sync**. The program aligns the two sounds. The clip on the higher track stays still, and the other is moved.
- **Keep pitch with speed**: when the clip is sped up or slowed down, the voice keeps a natural pitch.

### Audio effects

In the same tab, under **Add audio effect**, are the effects:

- **Equalizer**: bass, mid and treble, with low cut.
- **Compressor**: brings loud and soft passages closer, with threshold, ratio, attack, release and makeup gain.
- **Noise reduction**: reduces hiss and constant noise, such as a fan or air conditioning. It has intensity and sensitivity.
- **Echo**: delayed repetitions of the sound.
- **Reverb**: simulates the sound of a room.
- **Telephone / radio**: the sound of a telephone or old radio.
- **Distortion**: sound saturation.
- **Change voice**: ready-made voices (**Deep**, **High**, **Chipmunk**, **Monster** and **Robot**) or, in **Custom**, the **Pitch** in semitones and the **Robot** intensity. The speed does not change.

The effects are applied in the order in which they appear in the **Applied audio effects** list.

## Artificial intelligence

The program's artificial intelligence features run only on your computer. No video, audio, image or text is sent to the internet.

The features are in the **AI** tab of the properties, when you select a clip on the timeline. The **AI** section of the library summarizes the features and has the **Manage AI models…** button, which opens the model manager.

### Required models

Each feature uses an AI model. The **Remove background** model comes with the program. The others need to be installed, only once, with the **Download AI models** tool. When a feature's model is not installed, the button is disabled and the message "Unavailable — install the model in Tools › AI Models." appears.

| Feature | Model | Needs installing | Runs on |
|---|---|---|---|
| Remove background | MODNet | No, comes with the program | CPU or GPU |
| Compute masks in the background | MODNet | No, comes with the program | CPU or GPU |
| Track person | MODNet | No, comes with the program | CPU or GPU |
| Generate subtitles | Whisper large-v3 turbo | Yes, about 550 MB | CPU only |
| Isolate voice | GTCRN | Yes, about 0.5 MB | CPU only |
| AI noise reduction | GTCRN | Yes, about 0.5 MB | CPU only |
| Remove parts without speech | Silero VAD 5 | Yes, about 2.3 MB | CPU only |
| Detect scenes | TransNetV2 | Yes, about 31 MB | CPU or GPU |
| Enhance image | Swin2SR lightweight 2× | Yes, about 8 MB | CPU or GPU |
| Retouch (beauty and makeup) and blur faces | Face Mesh V2 | Yes, about 5 MB | CPU only |
| Virtual assistant (natural-language answers) | Phi-4-mini-instruct | Yes, about 3.2 GB | CPU or GPU |

Scene detection works even without the TransNetV2 model, by comparing the video's images. In that case, accuracy is lower.

Speech recognition (subtitles) uses Silero VAD to find the parts with speech. When you install Whisper, Silero VAD is installed with it, automatically.

> The models in the table take up disk space and also memory when used. Whisper, for example, needs about 1.5 GB of free memory.

### Remove background

Separates the person from the background and makes the background transparent, or replaces it with something else. Use it in the **AI** tab, with **Remove background with AI**.

- **New background**: choose **Transparent**, **Solid color**, **Blur** or **Image or video from the library**. In the last case, choose the file in **Background media**.
- **Keep**: keep the person (foreground) or the background, to remove the person.
- **Settings**: the mask threshold, edge shrink or expansion, edge smoothing and smoothing between frames are in the **Effects** tab, under **Applied effects**. Smoothing between frames reduces edge flicker from one frame to the next.

The model was trained mainly for portraits. In scenes with objects that look like people, it may keep parts of the background.

Use **Compute masks in the background** to process the whole clip in advance. This makes playback and export faster. The result is kept in the cache.

Use **Track person with AI** to create a tracking of the person. Then, in the **Composition** tab, you can attach texts or graphic elements to it.

### Generate subtitles

Creates subtitles automatically from the clip's speech.

1. Select the clip and go to the **AI** tab.
2. In **Speech language**, choose Portuguese, English, Spanish or automatic language.
3. Click **Generate subtitles with AI**.
4. The subtitles appear on the subtitle track. Review them in the **Subtitle editor**.

Recognition runs on the processor and takes about 4 minutes for a 10-minute video. Follow the progress in the tab's progress bar.

### Isolate voice and reduce noise

- **Isolate voice with AI**: separates the voice from the ambience. The program creates two new tracks, one with the voice and another with the ambience.
- **Reduce noise with AI**: reduces background noise, with the chosen **Noise reduction intensity**. Useful for recordings with hiss or hum.

These operations use the clip's audio as it sounds on the timeline. The **Mute the original audio** option avoids hearing the old sound together with the new one.

### Remove parts without speech

Finds the parts without speech and removes them from the clip. Use **Minimum pause** to set the minimum pause length to be cut, then click **Remove parts without speech with AI**. This feature needs the Silero VAD model installed; without it, the button is unavailable. To cut silences without AI, use the **Audio** tab (**Remove silences**).

### Detect scenes

Searches for scene changes in the video and creates a marker on each one.

1. Select the clip and go to the **AI** tab.
2. Adjust the scene **Sensitivity**. The higher it is, the more scenes are found.
3. Check **Split the clip at each scene** if you want the clip to be cut at each scene.
4. Click **Detect scenes**.

### Enhance image

Enlarges a photo 2× by reconstructing details. Select the image, go to the **AI** tab and click **Enhance image with AI**. The clip starts using the new image. To return to the original, use **Undo**.

### Virtual assistant

Answers questions about the program and about video editing based on this manual, in the program's language and without internet. Open it from **Help › Virtual assistant** (Shift+F1): the panel appears on the right of the editor. Type the question and press Enter (Shift+Enter starts a new line); with no conversation, the panel suggests a few questions.

- With the **Phi-4-mini** language model installed, the assistant writes a short answer using only the manual excerpts it found and shows the **Source in the manual**: click it to open the manual at that section. The stop button interrupts the answer.
- Without the model, the assistant shows the manual sections about the subject, with an excerpt of each and the **Open in the manual** button. The notice at the top of the panel installs the model (about 3.2 GB, downloaded once from the official sources) without leaving the editor; **Not now** hides the notice.
- The model uses the graphics card when the **AI device** is **GPU (DirectML)** (in **Settings › Performance**), otherwise the processor. The first question takes a few seconds longer because the model is loaded; closing the panel frees it from memory.
- **New conversation** clears the conversation. Answers may contain mistakes: check the section shown.

### Cancelling processing

While an AI feature is running, a progress bar appears. Click **Cancel AI processing** to stop it.

## Exporting the video

Exporting generates the final video file. Click **Export** in the top right corner of the window, or use **File › Export…** (shortcut Ctrl+E).

### Export templates

In the list on the left of the export window, choose a template. Each template already has the right settings for a platform:

- YouTube
- YouTube Shorts
- Instagram Feed
- Instagram Reels
- TikTok
- WhatsApp Status
- WhatsApp sharing
- Facebook Feed
- Facebook Reels
- Presentations
- Computers and laptops
- Custom export

Adjust the parameters and, if you like, click **Save as new…** to keep your settings. Your own templates show the "My template" badge and can be edited, duplicated or deleted. Default templates cannot be deleted, but you can use **Restore default** to return a template to its original settings, or **Restore all**.

### Parameters

- **File format**: MP4, MOV (QuickTime), MKV, WebM, AVI, MPEG-2 (.mpg) and animated GIF. Not every format accepts every codec. If the combination is not possible, the window warns you.
- **Encoder**: **Automatic (graphics card when available)**, **Graphics card** or **Software (most compatible)**. Software mode is the safest for files that need to open on any device.
- **Video codec**: H.264 (AVC), H.265 (HEVC), AV1, VP9, Apple ProRes, FFV1, QuickTime Animation, Motion JPEG, MPEG-2 or GIF. For most cases, choose H.264.
- **Profile**: the codec's Default, High, Main or Baseline. Keep Default if unsure.
- **Rate control**: **Variable bitrate (VBR)**, **Constant bitrate (CBR)** or **Constant quality**. With constant quality, use values between 18 and 23, where lower numbers mean better quality.
- **Video bitrate (kbps)**: the higher, the better the quality and the larger the file. For the internet, use the value suggested by the template.
- **Resolution**: check **Use the project resolution** or enter the width and height in pixels.
- **Frame rate**: **From project**, or one of the standard rates (23.976, 24, 25, 29.97, 30, 50, 59.94 and 60 FPS).
- **Audio codec**, **Sample rate** (44.1 kHz, 48 kHz or 96 kHz), **Channels** (Mono, Stereo or 5.1) and **Audio bitrate (kbps)**.
- **Export transparency (alpha channel)**: keeps the background transparent. Use with ProRes 4444, QuickTime Animation, FFV1, VP9 or GIF.

### Range and file splitting

- **Range**: choose **Entire sequence** to export the whole project, or **Custom range** to export only a part. Set the **Start** and **End**, or use **Start at playhead**.
- **Split into files**: splits the export into parts up to a given duration in seconds, or with a size limit in MB. Useful for platforms with a size limit.

### Subtitles in the export

In **Subtitles**, choose **None**, **SubRip (.srt)** or **WebVTT (.vtt)** to save a subtitle file next to the video. Check **Draw subtitles on the video (burned-in subtitles)** for the subtitles to be recorded into the image.

### Destination and start

1. Choose the **Destination folder** and the **File name**.
2. Check **Render in the background** if you want to keep editing while the video is generated.
3. Click **Export** to start.

### Render queue

Every export enters the render queue. Open the queue with the **Render queue** button or in **File › Render queue…**. For each item, the queue shows the status, progress, speed and remaining time:

- **Queued**: waiting its turn.
- **Rendering**: in progress.
- **Completed**: done. Use **Open exported file** or **Show in folder**.
- **Interrupted**: can be continued with **Resume**. When resuming, the program reuses the blocks already generated.
- **Failed**: the message explains the reason.
- **Cancelled**: stopped by the user.

Use **Cancel export** to stop an item, **Remove from queue** to take it off the list and **Clear completed** to empty the finished items.

## Saving, autosave and recovery

### Saving the project

- **File › Save** (Ctrl+S): writes the changes to the project file.
- **File › Save as…** (Ctrl+Shift+S): writes a copy of the project with another name or in another folder. The project takes the file's name, which appears in the window title.
- **File › Create backup**: creates a backup of the project at the current moment. The program keeps up to 10 copies of each project, in the application's backup folder.

The project file has the .med extension. The program does not change the original media files.

### Autosave

By default, the program saves a recovery copy every 5 minutes. You can turn this option off, or change the interval, in **Tools › Settings…**, on the **General** tab.

The recovery copy does not replace the project file. It is only offered when the program is opened after an unexpected shutdown, such as a power failure or a crash.

### Recovering a project

If the program was closed without saving, when you open it again the question "Recover project" appears:

- **Yes**: opens the project with the latest changes.
- **No**: opens an empty project; the copy stays stored and is offered again next time.

> It is recommended to save manually with Ctrl+S often, even with autosave on.

## Settings and performance

Open the settings in **Tools › Settings…** (shortcut Ctrl+,). The settings are divided into four tabs. Nothing is saved until you click **Save**.

### General tab

- **User › Full name**: your name.
- **System language › Language**: German, Spanish, French, English, Portuguese (Brazil) or Portuguese (Portugal). On first run, the program uses the Windows language if it is one of these, otherwise English. When you save, the program restarts in the new language: it first asks about unsaved changes and reopens a saved project. The manual also opens in the chosen language.
- **Theme**: **Dark**, **Light** or **Same as Windows**. You can also switch with the **Theme** button on the top bar.
- **Check for updates › Check monthly**: **Yes** (default) or **No**. The date of the last check appears alongside. With **Yes**, the program checks for a new version by itself when it opens and more than 30 days have passed since the last check. The request only sends the interface language; without internet, nothing appears.
- **Open the last project on startup**.
- **Default folders**: the projects folder and the exports folder.
- **Autosave**: turn the recovery copy on or off and set the interval.

The name, language, theme and update preference are stored in the Windows Registry, under `HKEY_CURRENT_USER\Software\Meu Editor de Vídeo`.

### Performance tab

- **Detected hardware**: shows the processor, memory, graphics cards, disks and whether the graphics card can decode and encode video. The program also shows the computer's limitations and a usage recommendation.
- **Preview**: the **Default preview quality** can be **Automatic**, **Full**, **1/2 resolution** or **1/4 resolution**. With Automatic, the program chooses according to the computer and the project, and lowers the resolution during playback if the computer cannot keep up. **Memory for preview frames** sets how much space to use for storing ready frames.
- **Proxies**: **Use proxies in preview when available** and **Automatically generate proxies for videos above 1080p**.
- **Acceleration**: **Use the graphics card to decode and encode video** and **Render the export queue in the background**.
- **Artificial intelligence**: the **AI device** can be **GPU (DirectML), with fallback to CPU**, or **CPU only**. With the GPU option, if the card or driver fails, the model is loaded on the processor automatically. The **Manage models…** button opens the AI models window.

### Cache tab

The cache stores thumbnails, waveforms, proxies and AI masks, so they do not need to be recalculated. In this tab you see how much space each area uses, set the **Cache limit** in GB (default 4 GB), and can use **Apply the limit now** or **Clear cache**.

When the limit is exceeded, the least recently used items are deleted. Projects and original media are never deleted by cache cleanup.

### Shortcuts tab

This tab shows all the program's actions, grouped by category. To change a shortcut:

1. Click the shortcut field.
2. Press the new key combination.
3. Click **Save**.

If the combination is already used by another action, the program warns you and does not let you save. Use **Default** on an action to return to the original shortcut, or **Restore all defaults** to undo all changes. Menus always show the shortcut in use.

## Keyboard shortcuts

These are the default shortcuts. You can change any of them in **Tools › Settings… › Shortcuts**.

| Action | Shortcut |
|---|---|
| New project | Ctrl+N |
| Open project | Ctrl+O |
| Save | Ctrl+S |
| Save as | Ctrl+Shift+S |
| Import media | Ctrl+I |
| Export | Ctrl+E |
| Undo | Ctrl+Z |
| Redo | Ctrl+Y or Ctrl+Shift+Z |
| Settings | Ctrl+, |
| User manual | F1 |
| Virtual assistant | Shift+F1 |
| Play / pause | Space |
| Full screen | F11 (Esc or F11 to exit) |
| Previous frame | ← |
| Next frame | → |
| Back 1 second | Shift+← |
| Forward 1 second | Shift+→ |
| Go to start | Home |
| Go to end | End |
| Previous cut | ↑ |
| Next cut | ↓ |
| Split clip at playhead | S or Ctrl+K |
| Delete | Delete or Backspace |
| Delete and close gap | Shift+Delete |
| Copy | Ctrl+C |
| Cut | Ctrl+X |
| Paste | Ctrl+V |
| Duplicate | Ctrl+D |
| Select all | Ctrl+A |
| Group | Ctrl+G |
| Ungroup | Ctrl+Shift+G |
| Add marker | M |
| Magnet on/off | N |
| Zoom in timeline | Ctrl++ (plus key) |
| Zoom out timeline | Ctrl+- (minus key) |
| Fit to window | Shift+Z |
| Multicam: camera 1 to 4 | Alt+1 to Alt+4 |

## Privacy

Meu Editor de Vídeo was made to work on your computer:

- Your videos, photos, audio, texts and projects are processed only on your computer.
- The artificial intelligence features also run on your computer. No content is sent to the internet.
- The program does not collect usage data and does not send telemetry.
- The program does not update itself over the internet.

There is a single exception. When you click **Install** in the **Download AI models** tool, the program downloads the AI model files from their authors' official sites, which are huggingface.co and github.com. Only the chosen models are downloaded, and no data from your project is sent. Once installed, the models work without internet. You can also install already-downloaded model packages, with **Install package…**, without using the internet.

To see the licenses and third-party components used, open **Help › About Meu Editor de Vídeo…**. The full text of the licenses is in **Help › Third-party licenses**.

## Troubleshooting

### The AI feature is "Unavailable"

The feature's model has not been installed yet. Go to **Tools › AI Models…**, click **Download models…**, choose the models and click **Install**. The tool installs the dependencies first, if any. For the Remove background feature, no installation is needed.

### A model installation was interrupted

Open **Download models…** again and click **Install**. The download continues where it stopped. If the downloaded file is incomplete or altered, it is discarded and downloaded again.

### The preview stutters or is slow

- At the top of the monitor, in **Preview resolution**, choose **1/2** or **1/4**.
- Turn on proxies, creating proxies of large videos with **Generate editing proxy (540p)**.
- Close other heavy programs while editing.
- In **Settings › Performance**, check that graphics card acceleration is on.

Export always uses the original resolution, so a video may stutter in the preview and still export correctly.

### A media item appears as missing

The file was moved, renamed or deleted. Use **Project › Locate missing media…** to point to the new path. To avoid this, use **Package media into the project** before moving the project.

### "Unsupported file format"

The program cannot read that format. Convert the file to a common format, such as MP4 (video) or MP3 and WAV (audio), with another conversion program, and import it again.

### The export failed

- Read the message in the render queue. It states the reason.
- If the message says the codec is incompatible with the format, choose another codec or another format.
- If the error involves the graphics card, choose the **Software (most compatible)** encoder and export again.
- If the file does not open on other devices, use the **Computers and laptops** template or the MP4 format with H.264.

### I lost changes after a crash

When opening the program, answer **Yes** to the question "Recover project". If it does not appear, look for the project backup, which is in the application's backup folder, inside `%LOCALAPPDATA%\MeuEditorDeVideo\backups`.

### The program does not open or shows errors

The operation logs are in the `%LOCALAPPDATA%\MeuEditorDeVideo\logs` folder. To open it, use **Help › Open logs folder** (or press Windows + R, paste that path and press Enter). When asking for help, send the most recent log file together with the text of **Help › System information…**. The logs do not contain the content of your videos.

### Questions about the installed version

Open **Help › About Meu Editor de Vídeo…** to see the program version, the license, the privacy information and the list of components and AI models used.
