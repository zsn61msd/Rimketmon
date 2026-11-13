using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using Verse.Sound;

namespace Rimketmon
{
    /// <summary>배틀 BGM(반복)과 효과음. 게임이 일시정지여도 프레임마다 유지(Maintain)한다.</summary>
    public static class RimketAudio
    {
        // ---- BGM: 포켓로그 방식. 한 번 끝까지 재생한 뒤 "루프 지점"으로 돌아가 반복한다(audio-manager.ts getBgmLoopPoint 값).
        private static readonly Dictionary<string, float> LoopPoint = new Dictionary<string, float>
        {
            { "battle_wild", 12.703f }, { "battle_trainer", 13.686f },
            { "battle_kanto_gym", 13.857f }, { "battle_johto_gym", 12.911f }, { "battle_hoenn_gym", 12.379f },
            { "battle_sinnoh_gym", 13.122f }, { "battle_unova_gym", 19.145f },
            { "victory_gym", 0f }, { "victory_trainer", 0f },
            // 포켓몬 리그(포켓로그 audio-manager.ts getBgmLoopPoint)
            { "battle_kanto_champion", 13.95f }, { "battle_johto_champion", 23.498f }, { "battle_hoenn_champion_g6", 11.762f }, { "battle_sinnoh_champion", 12.235f },
            { "battle_champion_alder", 27.653f }, { "battle_champion_iris", 10.145f }, { "battle_kalos_champion", 10.38f }, { "battle_champion_kukui", 15.784f },
            { "battle_alola_champion", 13.025f }, { "battle_galar_champion", 61.635f }, { "battle_mustard", 22.442f }, { "battle_champion_geeta", 37.447f },
            { "battle_champion_nemona", 14.914f }, { "battle_champion_kieran", 7.206f },
            { "battle_hoenn_elite", 11.35f }, { "battle_unova_elite", 17.73f }, { "battle_kalos_elite", 12.34f }, { "battle_alola_elite", 19.212f },
            { "battle_galar_elite", 164.069f }, { "battle_paldea_elite", 12.77f }, { "battle_bb_elite", 19.434f },
            // 메뉴 들판(포켓로그 biome-bgm-loop-points.json)
            { "battle_kalos_gym", 44.81f }, { "battle_galar_gym", 171.262f }, { "battle_paldea_gym", 127.489f },
            { "battle_rival", 14.11f }, { "battle_rival_2", 17.714f }, { "battle_rival_3", 17.586f }, { "battle_wild_strong", 13.94f },
            { "town", 7.288f }, { "factory", 4.985f },
            { "meadow", 3.891f }, { "forest", 0.341f }, { "snowy_forest", 3.814f }, { "swamp", 0f }, { "lake", 7.215f },
        };
        /// <summary>다음 배틀 한 번만 이 곡으로(사천왕·챔피언 전용 곡). Choose가 쓰고 비운다.</summary>
        public static string ForcedTrack;
        private static readonly string[] GymTracks = { "battle_kanto_gym", "battle_johto_gym", "battle_hoenn_gym", "battle_sinnoh_gym", "battle_unova_gym", "battle_kalos_gym", "battle_galar_gym", "battle_paldea_gym" };
        private static readonly string[] TrainerTracks = { "battle_trainer", "battle_rival", "battle_rival_2", "battle_rival_3" };
        private static readonly string[] WildTracks = { "battle_wild", "battle_wild_strong" };
        private static string lastBattleTrack;

        private static string nextTrack = "battle_wild";
        private static string playing;
        private static AudioClip clip;
        private static string loadingKey;
        private static AudioSource[] srcs;
        private static int cur;
        private static double nextStart;
        private static bool started;
        private static readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();

        public static bool BgmPlaying { get { return started && srcs != null && (srcs[0].isPlaying || srcs[1].isPlaying); } }

        /// <summary>다음에 틀 배틀 곡을 고른다. 야생은 battle_wild, 트레이너(관장)는 이름으로 지역 관장 곡 중 하나.</summary>
        public static void Choose(bool trainer, string name)
        {
            // 야생은 야생 곡 중에서, 트레이너(관장·랭크·어둠의 림켓몬)는 트레이너+관장 곡 중에서 매번 골라 지루하지 않게 한다(직전 곡은 피함).
            if (ForcedTrack != null) { nextTrack = ForcedTrack; lastBattleTrack = ForcedTrack; ForcedTrack = null; return; }
            string[] pool = trainer ? TrainerTracks.Concat(GymTracks).ToArray() : WildTracks;
            string pick = pool[UnityEngine.Random.Range(0, pool.Length)];
            for (int t = 0; t < 6 && pick == lastBattleTrack && pool.Length > 1; t++) pick = pool[UnityEngine.Random.Range(0, pool.Length)];
            lastBattleTrack = pick; nextTrack = pick;
        }

        private static double launchedAt;
        private static float duckUntil;
        /// <summary>팡파르(ME) 동안 BGM을 거의 끈다.</summary>
        public static void DuckBgm(float seconds) { duckUntil = Time.realtimeSinceStartup + seconds; }
        private static float Volume { get { return Mathf.Clamp01(RimketmonMod.Settings.bgmVolume * RimketmonMod.Settings.masterVolume) * (Time.realtimeSinceStartup < duckUntil ? 0.08f : 1f) * fadeMul; } }

        // ---- 부드러운 곡 바꾸기(메뉴 들판의 계절·날씨·시간대): 지금 곡을 줄이고(1.2초) 새 곡을 키운다(1.5초)
        private static float fadeMul = 1f, fadeLast = -1f; private static string fadePending;
        public static void PlayBgmFade(string track)
        {
            if (!RimketmonMod.Settings.bgmEnabled) return;
            if (playing == track && (BgmPlaying || loadingKey == track)) { fadePending = null; return; }
            if (BgmPlaying && playing != null) { fadePending = track; return; }
            Begin(track); fadeMul = 0f; fadePending = null;
        }
        private static void StepFade()
        {
            float now = Time.realtimeSinceStartup, dt = fadeLast < 0f ? 0f : Mathf.Clamp(now - fadeLast, 0f, 0.1f); fadeLast = now;
            if (fadePending != null)
            {
                fadeMul -= dt / 1.2f;
                if (fadeMul <= 0f) { string t = fadePending; fadePending = null; Begin(t); fadeMul = 0f; }
            }
            else if (fadeMul < 1f) fadeMul = Mathf.Min(1f, fadeMul + dt / 1.5f);
        }

        /// <summary>이미 재생 중이면 다시 시작하지 않는다(화면 울렁임에서 시작해 배틀 창까지 끊김 없이 이어진다).</summary>
        public static void StartBgm()
        {
            if (BgmPlaying && playing == nextTrack) return;
            if (!RimketmonMod.Settings.bgmEnabled) return;
            Begin(nextTrack);
        }

        private static void Begin(string track)
        {
            fadeMul = 1f; fadePending = null;
            StopSources();
            playing = track; clip = null; started = false;
            EnsureSources();
            AudioClip cached;
            if (clipCache.TryGetValue(track, out cached) && cached != null) { clip = cached; Launch(); }
            else if (loadingKey != track) { loadingKey = track; Find.CameraDriver.StartCoroutine(Load(track)); }
        }

        private static void EnsureSources()
        {
            if (srcs != null && srcs[0] != null) return;
            var go = new GameObject("RimketBgm");
            UnityEngine.Object.DontDestroyOnLoad(go);
            srcs = new[] { go.AddComponent<AudioSource>(), go.AddComponent<AudioSource>() };
            foreach (var a in srcs) { a.spatialBlend = 0f; a.playOnAwake = false; a.priority = 0; a.ignoreListenerPause = true; }      // 효과음이 한꺼번에 많이 나도 BGM이 밀려나지 않게 최우선으로
        }

        private static IEnumerator Load(string track)
        {
            string path = Path.Combine(RimketmonMod.Instance.Content.RootDir, "Bgm/" + track + ".ogg");
            if (File.Exists(path))
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.OGGVORBIS))
                {
                    yield return req.SendWebRequest();
                    if (!req.isNetworkError && !req.isHttpError) clipCache[track] = DownloadHandlerAudioClip.GetContent(req);
                }
            loadingKey = null;
            AudioClip c;
            if (clipCache.TryGetValue(track, out c) && c != null && playing == track && !started) { clip = c; Launch(); }
        }

        // 림월드 1.7(새 엔진)의 PlayScheduled는 예약 시각을 무시하고 바로 튼다 → 1초 전에 예약하면 다음 구간이 1초 일찍 겹쳐 나온다.
        // 유니티가 아니면 예약하지 않고, 이어질 시각이 됐을 때(한 프레임 오차) 바로 튼다.
        static int scheduleWorks = -1;
        static bool ScheduleWorks
        {
            get
            {
                if (scheduleWorks < 0)
                {
                    try { scheduleWorks = typeof(AudioSource).Assembly.GetName().Name.StartsWith("UnityEngine") ? 1 : 0; }
                    catch (System.Exception) { scheduleWorks = 1; }
                }
                return scheduleWorks == 1;
            }
        }

        private static void Launch()
        {
            if (clip == null || srcs == null) return;
            cur = 0;
            foreach (var a in srcs) a.loop = false;     // 반복은 전부 Maintain이 다음 구간을 미리 예약해서 처리한다(루프 지점이 없는 곡은 처음부터)
            srcs[0].clip = clip; srcs[0].volume = Volume; srcs[0].time = 0f;
            double t0 = AudioSettings.dspTime + 0.05;
            nextStart = t0 + clip.length;
            srcs[0].PlayScheduled(t0);
            launchedAt = t0;
            started = true;
        }

        private static void StopSources()
        {
            if (srcs == null) return;
            foreach (var a in srcs) if (a != null) { a.Stop(); a.loop = false; a.clip = null; }
            started = false;
        }

        /// <summary>매 프레임 호출: 루프 예약(끊김 없이 다음 구간을 미리 예약) + 볼륨 + 림월드 기본 음악 잠시 끄기.</summary>
        public static void Maintain()
        {
            StepFade();
            if (started && clip != null && !VictoryPlaying && !OneShot.Contains(playing))
            {
                float lp; LoopPoint.TryGetValue(playing, out lp);
                double now = AudioSettings.dspTime;
                if (nextStart < double.MaxValue && now > nextStart - (ScheduleWorks ? 1.0 : 0.02))
                {
                    var other = srcs[1 - cur];
                    other.clip = clip; other.time = lp; other.volume = Volume;
                    other.PlayScheduled(nextStart);
                    nextStart += clip.length - lp;
                    cur = 1 - cur;
                }
                srcs[0].volume = srcs[1].volume = Volume;
                // 안전장치: 틀어 둔 곡이 어떤 이유로든 끊겼으면(효과음이 몰릴 때 소리 채널을 빼앗기는 경우 등) 반복 지점부터 다시 튼다
                if (!srcs[0].isPlaying && !srcs[1].isPlaying && now > launchedAt + 1.0)
                {
                    cur = 0; srcs[0].clip = clip; srcs[0].time = Mathf.Clamp(lp, 0f, Mathf.Max(0f, clip.length - 0.1f)); srcs[0].volume = Volume;
                    double t0 = now + 0.05; srcs[0].PlayScheduled(t0); nextStart = t0 + clip.length - srcs[0].time; launchedAt = t0;
                }
            }
            if (Find.MusicManagerPlay != null) Find.MusicManagerPlay.ForceSilenceFor(2f);
        }

        public static void StopBgm()
        {
            StopSources();
            playing = null;
        }

        /// <summary>승리 음악(트레이너전). 배틀 곡을 끄고 재생한다.</summary>
        public static void PlayVictory(bool gym)
        {
            if (!RimketmonMod.Settings.bgmEnabled) return;
            Begin(gym ? "victory_gym" : "victory_trainer");
        }

        /// <summary>한 번만 트는 곡(반복하지 않음).</summary>
        private static readonly HashSet<string> OneShot = new HashSet<string> { "evolution_fanfare", "victory_champion" };
        public static bool VictoryPlaying { get { return playing != null && playing.StartsWith("victory"); } }

        // ---- 울음소리: Cry/<종 id>.ogg (포켓로그 audio/cry 원본을 변환). 없으면 조용히 넘어간다.
        private static readonly Dictionary<string, AudioClip> cryCache = new Dictionary<string, AudioClip>();
        private static readonly HashSet<string> cryLoading = new HashSet<string>();
        private static AudioSource crySrc;

        // ---- 울음소리 크기 맞추기
        // 포켓몬 울음소리(평균 약 -15dB, 곡마다 -8~-22dB)와 림월드 동물 소리는 원래 크기가 제각각이다.
        // 소리 파일의 실효값(RMS, 소리가 나는 부분만)을 재서 모두 같은 목표 크기로 맞춘다(배틀·월드 등장/퇴장 모두 이 길을 지난다).
        public const float CryTargetRms = 0.16f;      // 약 -16dBFS
        private static readonly Dictionary<AudioClip, float> gainCache = new Dictionary<AudioClip, float>();

        // AudioClip.loadType은 림월드 1.7(새 엔진)에 없다 → 이름으로 찾아보고, 없으면 읽을 수 있다고 보고 GetData에 맡긴다.
        static System.Reflection.PropertyInfo loadTypeProp; static bool loadTypeLooked;
        static bool ClipReadable(AudioClip c)
        {
            if (!loadTypeLooked) { loadTypeLooked = true; try { loadTypeProp = typeof(AudioClip).GetProperty("loadType"); } catch (System.Exception) { loadTypeProp = null; } }
            if (loadTypeProp == null) return true;
            try { return System.Convert.ToInt32(loadTypeProp.GetValue(c, null)) == 0; }      // 0 = DecompressOnLoad
            catch (System.Exception) { return true; }
        }

        /// <summary>이 소리를 목표 크기로 맞추는 배율. 재지 못하면 1.</summary>
        public static float CryGain(AudioClip c)
        {
            if (c == null) return 1f;
            float g;
            if (gainCache.TryGetValue(c, out g)) return g;
            g = 1f;
            try
            {
                if (ClipReadable(c) && c.samples > 0)
                {
                    int n = Mathf.Min(c.samples, 44100 * 4) * c.channels;
                    var data = new float[n];
                    if (c.GetData(data, 0))
                    {
                        // 20ms 단위로 나눠 조용한 구간(앞뒤 여백)은 빼고 잰다
                        int win = Mathf.Max(64, c.frequency / 50 * c.channels);
                        double sum = 0; long cnt = 0;
                        for (int s = 0; s + win <= n; s += win)
                        {
                            double e = 0; for (int i = s; i < s + win; i++) e += data[i] * data[i];
                            if (e / win > 1e-5) { sum += e; cnt += win; }
                        }
                        if (cnt > 0) { float rms = (float)System.Math.Sqrt(sum / cnt); if (rms > 1e-4f) g = Mathf.Clamp(CryTargetRms / rms, 0.25f, 3f); }
                    }
                }
            }
            catch (System.Exception) { g = 1f; }
            gainCache[c] = g;
            return g;
        }

        /// <summary>림월드 소리(SoundDef)에서 실제 소리 파일 하나를 고른다. 못 고르면 null.</summary>
        private static AudioClip ClipOf(SoundDef def)
        {
            try
            {
                if (def == null || def.subSounds == null || def.subSounds.Count == 0) return null;
                var clips = new List<AudioClip>();
                foreach (var grain in def.subSounds[0].grains)
                    foreach (var rg in grain.GetResolvedGrains())
                    {
                        var rc = rg as ResolvedGrain_Clip;
                        if (rc != null && rc.clip != null) clips.Add(rc.clip);
                    }
                return clips.Count == 0 ? null : clips[Rand.Range(0, clips.Count)];
            }
            catch (System.Exception) { return null; }
        }

        /// <summary>림켓몬의 울음소리: 림월드 동물이면 그 동물의 소리, 아니면 포켓몬 울음소리(Cry 폴더). 크기는 목표치로 맞춘다.</summary>
        /// <param name="volume">추가 배율(월드에서 멀리 있으면 작게)</param>
        public static void PlayCryFor(Rimketmon.Engine.RimketCreature c, float volume = 1f)
        {
            if (c == null || !RimketmonMod.Settings.sfxEnabled) return;
            try
            {
                var d = string.IsNullOrEmpty(c.SpeciesId) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(c.SpeciesId);
                if (d != null && d.race != null && d.race.lifeStageAges != null && d.race.lifeStageAges.Count > 0)
                {
                    var snd = d.race.lifeStageAges[d.race.lifeStageAges.Count - 1].soundCall;
                    if (snd != null)
                    {
                        var clip = ClipOf(snd);
                        if (clip != null) { PlayCryClip(clip, volume); return; }
                        Play(snd); return;      // 파일을 못 꺼내면 림월드 방식 그대로
                    }
                }
            }
            catch (System.Exception) { }
            string path = Path.Combine(RimketmonMod.Instance.Content.RootDir, "Cry/" + c.SpeciesId + ".ogg");
            string id = File.Exists(path) || string.IsNullOrEmpty(c.Species) ? c.SpeciesId : c.Species;
            if (!File.Exists(Path.Combine(RimketmonMod.Instance.Content.RootDir, "Cry/" + id + ".ogg"))) id = Rimketmon.Engine.SpeciesCatalog.BaseOf(id);      // 폼은 원래 종족의 울음소리
            if (!File.Exists(Path.Combine(RimketmonMod.Instance.Content.RootDir, "Cry/" + id + ".ogg")) && c.RemotePic != null) { PlaySe("pb_rel"); return; }      // 내 게임에 없는 종(온라인 상대의 모드·DLC 종족 등): 기본음
            PlayCry(id, volume);
        }

        public static void PlayCry(string speciesId, float volume = 1f)
        {
            if (string.IsNullOrEmpty(speciesId) || !RimketmonMod.Settings.sfxEnabled) return;
            AudioClip c;
            if (cryCache.TryGetValue(speciesId, out c)) { PlayCryClip(c, volume); return; }
            if (cryLoading.Contains(speciesId)) return;
            cryLoading.Add(speciesId);
            Find.CameraDriver.StartCoroutine(LoadCry(speciesId, volume));
        }

        private static void PlayCryClip(AudioClip c, float volume = 1f)
        {
            if (c == null) return;
            if (crySrc == null)
            {
                var go = new GameObject("RimketCry");
                UnityEngine.Object.DontDestroyOnLoad(go);
                crySrc = go.AddComponent<AudioSource>(); crySrc.spatialBlend = 0f; crySrc.playOnAwake = false;
            }
            crySrc.PlayOneShot(c, Mathf.Clamp01(RimketmonMod.Settings.sfxVolume * RimketmonMod.Settings.masterVolume) * CryGain(c) * Mathf.Clamp01(volume));
        }

        private static IEnumerator LoadCry(string id, float volume)
        {
            string path = Path.Combine(RimketmonMod.Instance.Content.RootDir, "Cry/" + id + ".ogg");
            AudioClip clipLoaded = null;
            if (File.Exists(path))
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.OGGVORBIS))
                {
                    yield return req.SendWebRequest();
                    if (!req.isNetworkError && !req.isHttpError) clipLoaded = DownloadHandlerAudioClip.GetContent(req);
                }
            cryCache[id] = clipLoaded;
            cryLoading.Remove(id);
            PlayCryClip(clipLoaded, volume);
        }

        /// <summary>센터 같은 화면의 BGM을 튼다(이미 같은 곡이면 이어서). 반복은 Maintain이 맡는다.</summary>
        public static void PlayBgm(string track)
        {
            if (!RimketmonMod.Settings.bgmEnabled) return;
            if (BgmPlaying && playing == track) return;
            Begin(track);
        }

        // ---- 짧은 효과음/ME: Se/<이름>.ogg (팬게임 원본을 변환)
        private static readonly Dictionary<string, AudioClip> seCache = new Dictionary<string, AudioClip>();
        private static readonly HashSet<string> seLoading = new HashSet<string>();
        private static AudioSource seSrc;

        public static void PlaySe(string name)
        {
            if (string.IsNullOrEmpty(name) || !RimketmonMod.Settings.sfxEnabled) return;
            AudioClip c;
            if (seCache.TryGetValue(name, out c)) { PlaySeClip(c); return; }
            if (seLoading.Contains(name)) return;
            seLoading.Add(name);
            Find.CameraDriver.StartCoroutine(LoadSe(name));
        }

        private static void PlaySeClip(AudioClip c)
        {
            if (c == null) return;
            if (seSrc == null)
            {
                var go = new GameObject("RimketSe");
                UnityEngine.Object.DontDestroyOnLoad(go);
                seSrc = go.AddComponent<AudioSource>(); seSrc.spatialBlend = 0f; seSrc.playOnAwake = false;
            }
            seSrc.PlayOneShot(c, Mathf.Clamp01(RimketmonMod.Settings.sfxVolume * RimketmonMod.Settings.masterVolume));
        }

        private static IEnumerator LoadSe(string name)
        {
            string path = Path.Combine(RimketmonMod.Instance.Content.RootDir, "Se/" + name + ".ogg");
            AudioClip loaded = null;
            if (File.Exists(path))
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.OGGVORBIS))
                {
                    yield return req.SendWebRequest();
                    if (!req.isNetworkError && !req.isHttpError) loaded = DownloadHandlerAudioClip.GetContent(req);
                }
            seCache[name] = loaded;
            seLoading.Remove(name);
            PlaySeClip(loaded);
        }

        public static void Play(SoundDef def)
        {
            if (def == null || !RimketmonMod.Settings.sfxEnabled) return;
            SoundInfo info = SoundInfo.OnCamera();
            info.volumeFactor = Mathf.Clamp01(RimketmonMod.Settings.sfxVolume * RimketmonMod.Settings.masterVolume);
            def.PlayOneShot(info);
        }
    }
}
