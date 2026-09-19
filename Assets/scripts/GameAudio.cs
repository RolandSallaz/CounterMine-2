using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Bounded spatial voices; clips are loaded once, never instantiated per shot.</summary>
public sealed class GameAudio : MonoBehaviour
{
    public const string BackgroundMusicCredit = "LOOP BOX #2 - Of Far Different Nature\nCC BY 4.0 | fardifferent.carrd.co";
    private static GameAudio instance;
    private readonly AudioSource[] voices = new AudioSource[32];
    private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    private static readonly Dictionary<string, WeaponAudioProfile> profiles = new Dictionary<string, WeaponAudioProfile>();
    private AudioSource music;
    private AudioSource nextMusic;
    private Coroutine playlist;
    private readonly System.Random musicRandom = new System.Random();
    private int cursor;
    private readonly Dictionary<AudioSource, int> leases = new Dictionary<AudioSource, int>();
    private int nextLease;
    public static int Lease(AudioSource source) => source != null && Instance.leases.TryGetValue(source, out int token) ? token : 0;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; profiles.Clear(); }
    private static GameAudio Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new GameObject("Game Audio").AddComponent<GameAudio>();
                DontDestroyOnLoad(instance.gameObject);
                for (int i = 0; i < instance.voices.Length; i++)
                {
                    var go = new GameObject("Voice " + i);
                    go.transform.SetParent(instance.transform);
                    var source = go.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.dopplerLevel = 0;
                    source.rolloffMode = AudioRolloffMode.Linear;
                    instance.voices[i] = source;
                }
            }
            return instance;
        }
    }
    public static WeaponAudioProfile Profile(string id)
    {
        if (string.IsNullOrEmpty(id)) id = "ak74";
        if (!profiles.TryGetValue(id, out var profile))
        {
            profile = Resources.Load<WeaponAudioProfile>("Audio/Weapons/" + id);
            profiles[id] = profile;
        }
        return profile;
    }
    public static string WeaponName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "Unknown";
        var profile = Profile(id);
        return profile != null ? profile.displayName : string.IsNullOrEmpty(id) ? "Unknown" : id.ToUpperInvariant();
    }
    public static AudioSource Play(AudioClip clip, Vector3 position, float volume = .6f, float range = 25f,
        bool local = false, float pitch = 1f, float offset = 0f)
    {
        if (clip == null || offset >= clip.length) return null;
        var audio = Instance;
        AudioSource voice = null;
        for (int i = 0; i < audio.voices.Length; i++)
        {
            int index = (audio.cursor + i) % audio.voices.Length;
            if (audio.voices[index].isPlaying) continue;
            voice = audio.voices[index]; audio.cursor = (index + 1) % audio.voices.Length; break;
        }
        if (voice == null) { voice = audio.voices[audio.cursor]; audio.cursor = (audio.cursor + 1) % audio.voices.Length; }
        voice.Stop(); voice.transform.position = position;
        voice.clip = clip; voice.volume = Mathf.Clamp01(volume); voice.pitch = Mathf.Clamp(pitch, .25f, 3f);
        voice.spatialBlend = local ? 0f : 1f; voice.minDistance = 2f; voice.maxDistance = Mathf.Max(3f, range);
        voice.time = Mathf.Max(0f, offset); voice.Play();
        audio.leases[voice] = ++audio.nextLease;
        return voice;
    }
    public static void Effect(string id, Vector3 position, float volume = .5f, float range = 25f, bool local = false)
    {
        var audio = Instance;
        if (!audio.clips.TryGetValue(id, out var clip))
        { clip = Resources.Load<AudioClip>("Audio/" + id); audio.clips[id] = clip; }
        Play(clip, position, volume, range, local);
    }
    public static void Shot(WeaponAudioProfile profile, Vector3 position, int sequence, bool local)
    {
        if (profile == null || profile.shots == null || profile.shots.Length == 0) return;
        uint seed = unchecked((uint)sequence * 2654435761u);
        Play(profile.shots[seed % (uint)profile.shots.Length], position, profile.shotVolume, profile.shotRange, local,
            1f + ((seed % 101) / 50f - 1f) * profile.pitchVariation);
    }
    /// <summary>Seamless looping background music on a dedicated 2D channel. Safe to call repeatedly.</summary>
    public static void PlayMusic(string id, float volume = .35f)
    {
        var audio = Instance;
        if (audio.playlist != null)
        {
            audio.StopCoroutine(audio.playlist);
            audio.playlist = null;
            audio.ReleaseMusic(audio.nextMusic);
        }
        if (audio.music == null)
        {
            var go = new GameObject("Music");
            go.transform.SetParent(audio.transform, false);
            audio.music = go.AddComponent<AudioSource>();
            audio.music.playOnAwake = false;
            audio.music.dopplerLevel = 0;
            audio.music.rolloffMode = AudioRolloffMode.Linear;
        }
        var clip = Resources.Load<AudioClip>("Audio/" + id);
        if (clip == null) return;
        if (audio.music.clip == clip && audio.music.isPlaying) return;
        audio.music.Stop();
        audio.music.clip = clip;
        audio.music.loop = true;
        audio.music.volume = Mathf.Clamp01(volume);
        audio.music.pitch = 1f;
        audio.music.spatialBlend = 0f;
        audio.music.minDistance = 2f;
        audio.music.maxDistance = 100f;
        audio.music.Play();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartBackgroundMusic()
    {
        var audio = Instance;
        if (audio.playlist == null) audio.playlist = audio.StartCoroutine(audio.RunPlaylist());
    }

    private void ReleaseMusic(AudioSource source)
    {
        if (source == null) return;
        var clip = source.clip;
        source.Stop();
        source.clip = null;
        if (clip != null) Resources.UnloadAsset(clip);
    }

    private AudioSource CreateMusicChannel(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        return source;
    }

    private IEnumerator RunPlaylist()
    {
        var manifest = Resources.Load<TextAsset>("Audio/MusicPlaylist");
        if (manifest == null) yield break;
        var tracks = new List<string>();
        foreach (var line in manifest.text.Split('\n'))
        {
            string path = line.Trim();
            if (path.Length > 0 && !tracks.Contains(path)) tracks.Add(path);
        }
        if (tracks.Count == 0) yield break;
        if (music == null) music = CreateMusicChannel("Music A");
        if (nextMusic == null) nextMusic = CreateMusicChannel("Music B");
        string previous = null;
        while (true)
        {
            for (int i = tracks.Count - 1; i > 0; i--)
            {
                int j = musicRandom.Next(i + 1);
                string swap = tracks[i]; tracks[i] = tracks[j]; tracks[j] = swap;
            }
            if (tracks.Count > 1 && tracks[0] == previous)
            {
                string swap = tracks[0]; tracks[0] = tracks[1]; tracks[1] = swap;
            }
            foreach (string entry in tracks)
            {
                string[] parts = entry.Split('|');
                string path = parts[0];
                float gain = 1f;
                if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float parsedGain)) gain = Mathf.Clamp01(parsedGain);
                var request = Resources.LoadAsync<AudioClip>("Audio/" + path);
                yield return request;
                var clip = request.asset as AudioClip;
                if (clip == null) continue;
                clip.LoadAudioData();
                while (clip.loadState == AudioDataLoadState.Loading) yield return null;
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    Resources.UnloadAsset(clip);
                    continue;
                }
                nextMusic.clip = clip;
                nextMusic.volume = 0f;
                nextMusic.Play();
                float elapsed = 0f;
                float outgoingVolume = music.volume;
                while (elapsed < 4f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float blend = Mathf.Clamp01(elapsed / 4f);
                    music.volume = outgoingVolume * (1f - blend);
                    nextMusic.volume = .1f * gain * blend;
                    yield return null;
                }
                ReleaseMusic(music);
                var swap = music; music = nextMusic; nextMusic = swap;
                previous = entry;
                yield return new WaitForSecondsRealtime(musicRandom.Next(90, 151));
            }
            yield return new WaitForSecondsRealtime(1f);
        }
    }
}
