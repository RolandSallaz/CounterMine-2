# Background playlist

All 26 LOOP BOX #2 tracks are listed in `Assets/Resources/Audio/MusicPlaylist.txt`.
Each line contains a Resources path and playback gain, separated by `|`.
The original OGG files are retained; Force Field uses the previously prepared WAV.
Credits and modification details are in CREDITS.txt and the built MusicCredits resource.

GameAudio shuffles the full playlist without adjacent repeats between passes.
Tracks loop for 90–150 seconds between four-second crossfades. Two dedicated
2D sources play at most two tracks; outgoing clips are released after the fade.
Per-track gain targets RMS 0.12 before the background volume of 0.1.

Validation: all 25 added OGG files decoded successfully; 26 unique playlist
entries; C# build passed. Audio transitions have not been auditioned in Unity.
