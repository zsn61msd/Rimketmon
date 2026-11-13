using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rimketmon.Engine;

namespace Rimketmon
{
    internal sealed partial class Window_RimketMenu
    {
        // ------------------------------------------------------------------ 메인 메뉴 왼쪽 들판: 파티 림켓몬이 자유롭게 돌아다닌다(허기 등은 멈춘 상태, 모양만)

        private sealed class Walker
        {
            public RimketCreature C; public bool Fly; public float X, Y, TX, TY, Speed, Wait, Phase, Alt, AltT; public bool Right; public int Dir = 2;
            // 나는 림켓몬: 0 땅(쉬기·걷기) / 1 날기 / 2 내려앉기. 속도 벡터(VX, VY: 장면 픽셀/초)를 조금씩 꺾으며 날아다닌다
            public int State; public float StateT, VX, VY, FlapT, Glide, Bank;
            public string Sheet; public bool SheetTried;      // 포켓몬 모습이면 걷는 그림(오버월드 4방향×4프레임)
        }

        private static readonly List<Walker> walkers = new List<Walker>();
        private static float meadowLast = -1f; private static int meadowSig = -1;      // 창을 닫았다 열어도(체육관 등) 들판 동물 위치 유지

        private static bool CanFly(RimketCreature c)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(c.SpeciesId);
            if (def == null) return false;
            if (RimSprite.CanFly(c)) return true;
            return def.race != null && (def.race.canFlyIntoMap || def.race.Animal && def.race.canFlyInVacuum);
        }

        /// <summary>
        /// 나는 림켓몬의 움직임: 땅에서 쉬거나 종종걸음 → 날아오름(날갯짓 빨라짐) → 속도 벡터를 조금씩 꺾으며 곡선으로 날아다님
        /// (가끔 날개를 펴고 활공, 높이도 천천히 오르내림) → 속도를 줄이며 내려앉음 → 다시 쉬기.
        /// </summary>
        /// <summary>들판 림켓몬 머리 위 이름표(작은 반투명 띠 + 이름).</summary>
        private static void NameTag(Canvas c, Walker wk, float topY)
        {
            string n = wk.C.Name ?? "";
            float tw = c.Measure(n, 5.5f) + 4f, ty = topY - 8f;
            c.Fill(wk.X - tw / 2f, ty, tw, 7f, new Color(0f, 0f, 0f, 0.45f));
            c.Label(n, wk.X, ty + 0.3f, 5.5f, Color.white, new Color(0f, 0f, 0f, 0.6f), TextAnchor.UpperCenter);
        }

        private static void FlyStep(Walker wk, float dt, float w, float depth)
        {
            wk.StateT -= dt;
            float flapRate = 1f;
            switch (wk.State)
            {
                case 0:      // 땅: 쉬다가 가끔 몇 걸음, 시간이 되면 날아오른다
                    wk.Alt = Mathf.MoveTowards(wk.Alt, 0f, dt * 1.5f);
                    wk.Glide = 0f; wk.Bank = Mathf.MoveTowards(wk.Bank, 0f, dt * 40f);
                    if (wk.Speed > 0f)
                    {
                        float gy0 = wk.Y * depth, gy1 = wk.TY * depth, dx = wk.TX - wk.X, dy = gy1 - gy0;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy), step = wk.Speed * dt;
                        if (dist <= step) { wk.X = wk.TX; wk.Y = wk.TY; wk.Speed = 0f; }
                        else { wk.X += dx / dist * step; wk.Y = Mathf.Clamp01(wk.Y + dy / dist * step / depth); if (Mathf.Abs(dx) > 0.5f) wk.Right = dx > 0f; wk.Dir = Mathf.Abs(dx) >= Mathf.Abs(dy) ? (dx > 0f ? 1 : 3) : (dy < 0f ? 0 : 2); }
                    }
                    else if (UnityEngine.Random.value < dt * 0.35f)
                    {
                        wk.TX = Mathf.Clamp(wk.X + (UnityEngine.Random.value - 0.5f) * 30f, 10f, w - 10f);
                        wk.TY = Mathf.Clamp(wk.Y + (UnityEngine.Random.value - 0.5f) * 0.3f, 0.1f, 1f);
                        wk.Speed = 6f + UnityEngine.Random.value * 5f;
                    }
                    if (wk.StateT <= 0f)
                    {
                        wk.State = 1; wk.StateT = 5f + UnityEngine.Random.value * 9f; wk.Speed = 0f;
                        float a = UnityEngine.Random.value * Mathf.PI * 2f;
                        wk.VX = Mathf.Cos(a) * 18f; wk.VY = Mathf.Sin(a) * 6f;
                        wk.AltT = 0.45f + UnityEngine.Random.value * 0.55f;
                        wk.TX = 10f + UnityEngine.Random.value * (w - 20f); wk.TY = 0.05f + UnityEngine.Random.value * 0.95f;
                    }
                    break;

                case 1:      // 날기
                {
                    // 목표점 쪽으로 방향을 조금씩 꺾는다(급회전 없이 곡선). 가까워지거나 가끔 새 목표
                    float gy = wk.Y * depth, ty = wk.TY * depth;
                    float dx = wk.TX - wk.X, dy = ty - gy, dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist < 12f || UnityEngine.Random.value < dt * 0.25f) { wk.TX = 10f + UnityEngine.Random.value * (w - 20f); wk.TY = 0.05f + UnityEngine.Random.value * 0.95f; }
                    float speed = Mathf.Sqrt(wk.VX * wk.VX + wk.VY * wk.VY);
                    float want = wk.Glide > 0f ? 20f : 30f;
                    float heading = Mathf.Atan2(wk.VY, wk.VX), target = Mathf.Atan2(dy, dx);
                    float turn = Mathf.DeltaAngle(heading * Mathf.Rad2Deg, target * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    float maxTurn = 1.8f * dt;
                    float dh = Mathf.Clamp(turn, -maxTurn, maxTurn);
                    heading += dh;
                    speed = Mathf.MoveTowards(speed, want, dt * 14f);
                    wk.VX = Mathf.Cos(heading) * speed; wk.VY = Mathf.Sin(heading) * speed;
                    wk.X += wk.VX * dt;
                    wk.Y = Mathf.Clamp(wk.Y + wk.VY * 0.55f * dt / depth, 0.03f, 1f);      // 깊이 방향은 덜 움직인다(원근)
                    if (wk.Y <= 0.031f || wk.Y >= 0.999f) wk.VY = -wk.VY * 0.5f;
                    if (wk.X < 6f) { wk.X = 6f; wk.VX = Mathf.Abs(wk.VX); } else if (wk.X > w - 6f) { wk.X = w - 6f; wk.VX = -Mathf.Abs(wk.VX); }
                    if (Mathf.Abs(wk.VX) > 1f) wk.Right = wk.VX > 0f;
                    wk.Dir = wk.Right ? 1 : 3;
                    // 꺾는 쪽으로 기울기(오른쪽을 볼 때와 왼쪽을 볼 때 부호가 반대)
                    wk.Bank = Mathf.Lerp(wk.Bank, (dh / Mathf.Max(0.0001f, dt)) * 9f * (wk.Right ? 1f : -1f), dt * 4f);
                    // 높이: 가끔 목표 높이를 바꾸고 천천히 따라간다. 내려갈 땐 활공, 오를 땐 힘차게
                    if (UnityEngine.Random.value < dt * 0.3f) wk.AltT = 0.35f + UnityEngine.Random.value * 0.65f;
                    float climb = wk.AltT - wk.Alt;
                    wk.Alt = Mathf.MoveTowards(wk.Alt, wk.AltT, dt * (climb > 0f ? 0.5f : 0.3f));
                    if (wk.Glide > 0f) wk.Glide -= dt;
                    else if (climb < -0.1f && UnityEngine.Random.value < dt * 0.8f) wk.Glide = 0.8f + UnityEngine.Random.value * 1.4f;
                    flapRate = wk.Glide > 0f ? 0f : (climb > 0.05f ? 1.35f : 1f);
                    if (wk.StateT <= 0f) { wk.State = 2; wk.StateT = 4f; wk.TX = Mathf.Clamp(wk.X + wk.VX * 1.2f, 10f, w - 10f); wk.TY = Mathf.Clamp(wk.Y + 0.15f * (UnityEngine.Random.value - 0.5f), 0.1f, 1f); }
                    break;
                }

                case 2:      // 내려앉기: 앞으로 미끄러지며 속도와 높이를 줄인다. 땅 직전에 날갯짓 몇 번
                {
                    wk.VX = Mathf.MoveTowards(wk.VX, 0f, dt * 9f); wk.VY = Mathf.MoveTowards(wk.VY, 0f, dt * 5f);
                    wk.X = Mathf.Clamp(wk.X + wk.VX * dt, 6f, w - 6f);
                    wk.Y = Mathf.Clamp(wk.Y + wk.VY * 0.55f * dt / depth, 0.05f, 1f);
                    if (Mathf.Abs(wk.VX) > 1f) wk.Right = wk.VX > 0f;
                    wk.Alt = Mathf.MoveTowards(wk.Alt, 0f, dt * 0.35f);
                    wk.Bank = Mathf.MoveTowards(wk.Bank, -6f, dt * 20f);
                    wk.Glide = wk.Alt > 0.15f ? 1f : 0f;
                    flapRate = wk.Alt > 0.15f ? 0f : 1.6f;
                    if (wk.Alt <= 0.001f || wk.StateT <= 0f) { wk.Alt = 0f; wk.State = 0; wk.StateT = 2f + UnityEngine.Random.value * 5f; wk.Speed = 0f; wk.Glide = 0f; wk.Bank = 0f; wk.Dir = wk.Right ? 1 : 3; }
                    break;
                }
            }
            wk.FlapT += dt * flapRate;
        }

        // ---- 들판 환경: 림켓링크를 쓴 정착민이 있는 지도(없으면 지금 지도·본진)의 계절·날씨·시각을 따른다(2초마다 다시 읽음)
        internal enum MSeason { Spring, Summer, Fall, Winter }
        internal enum MWeather { Clear, Rain, Snow, Fog }
        internal struct MeadowEnv { public MSeason Season; public MWeather Weather; public float Hour; public bool Valid; }
        private static MeadowEnv menv; private static float menvAt = -10f;

        internal static MeadowEnv Env()
        {
            if (Time.realtimeSinceStartup - menvAt < 2f) return menv;
            menvAt = Time.realtimeSinceStartup;
            var e = new MeadowEnv { Season = MSeason.Summer, Weather = MWeather.Clear, Hour = 12f };
            try
            {
                var link = RimketLink.FindLinkPawn();
                Map map = link != null && link.MapHeld != null ? link.MapHeld : Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                if (map != null)
                {
                    e.Valid = true;
                    e.Hour = GenLocalDate.HourFloat(map);
                    var se = GenLocalDate.Season(map);
                    e.Season = se == RimWorld.Season.Spring ? MSeason.Spring : se == RimWorld.Season.Fall ? MSeason.Fall
                        : se == RimWorld.Season.Winter || se == RimWorld.Season.PermanentWinter ? MSeason.Winter : MSeason.Summer;
                    var wm = map.weatherManager; var wd = wm != null ? wm.curWeather : null;
                    if (wd != null)
                    {
                        string dn = wd.defName ?? "";
                        if (wd.snowRate > 0.01f || dn.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0) e.Weather = MWeather.Snow;
                        else if (wd.rainRate > 0.01f || dn.IndexOf("Rain", StringComparison.OrdinalIgnoreCase) >= 0) e.Weather = MWeather.Rain;
                        else if (dn.IndexOf("Fog", StringComparison.OrdinalIgnoreCase) >= 0) e.Weather = MWeather.Fog;
                    }
                }
            }
            catch (Exception) { }
            return menv = e;
        }

        /// <summary>들판(메뉴) BGM: 겨울·눈 = 눈숲, 비·안개 = 습지, 밤 = 호수, 가을 = 숲, 그 밖 = 초원(포켓로그 바이옴 곡).</summary>
        internal static string MeadowTrack()
        {
            var e = Env();
            if (!e.Valid) return "meadow";
            bool night = e.Hour < 5.5f || e.Hour >= 19f;
            if (e.Season == MSeason.Winter || e.Weather == MWeather.Snow) return "snowy_forest";
            if (e.Weather == MWeather.Rain || e.Weather == MWeather.Fog) return "swamp";
            if (night) return "lake";
            if (e.Season == MSeason.Fall) return "forest";
            return "meadow";
        }

        /// <summary>시각에 따른 하늘색(위·지평선)과 장면 어둡기(0 낮 ~ 0.55 한밤).</summary>
        private static void SkyOf(float hour, out Color top, out Color low, out float dark)
        {
            Color dayT = new Color(0.55f, 0.76f, 0.93f), dayL = new Color(0.68f, 0.84f, 0.96f);
            Color duskT = new Color(0.42f, 0.42f, 0.7f), duskL = new Color(0.98f, 0.62f, 0.4f);
            Color nightT = new Color(0.05f, 0.07f, 0.18f), nightL = new Color(0.12f, 0.16f, 0.32f);
            float k;
            if (hour >= 7f && hour < 17f) { top = dayT; low = dayL; dark = 0f; return; }
            if (hour >= 17f && hour < 19f) { k = (hour - 17f) / 2f; top = Color.Lerp(dayT, duskT, k); low = Color.Lerp(dayL, duskL, k); dark = 0.2f * k; return; }
            if (hour >= 19f && hour < 20.5f) { k = (hour - 19f) / 1.5f; top = Color.Lerp(duskT, nightT, k); low = Color.Lerp(duskL, nightL, k); dark = Mathf.Lerp(0.2f, 0.55f, k); return; }
            if (hour >= 5f && hour < 7f) { k = (hour - 5f) / 2f; top = Color.Lerp(nightT, dayT, k); low = Color.Lerp(new Color(0.95f, 0.7f, 0.55f), dayL, k); dark = Mathf.Lerp(0.45f, 0f, k); return; }
            top = nightT; low = nightL; dark = 0.55f;
        }

        private void DrawMeadow(Canvas g, PartyComponent pc, float x, float y, float w, float h)
        {
            float now = Time.realtimeSinceStartup;
            float dt = meadowLast < 0f ? 0f : Mathf.Clamp(now - meadowLast, 0f, 0.05f);
            meadowLast = now;

            var party = pc.ActiveParty();
            int sig = party.Count; foreach (var c in party) sig = sig * 31 + c.GetHashCode();
            if (sig != meadowSig)
            {
                meadowSig = sig;
                var old = walkers.ToDictionary(k => k.C, k => k);
                walkers.Clear();
                var rnd = new System.Random(sig);
                foreach (var c in party)
                {
                    Walker wk;
                    if (!old.TryGetValue(c, out wk))
                    {
                        wk = new Walker { C = c, Fly = CanFly(c), X = 12f + (float)rnd.NextDouble() * (w - 24f), Y = 0.3f + (float)rnd.NextDouble() * 0.7f, Phase = (float)rnd.NextDouble() * 6f };
                        wk.TX = wk.X; wk.TY = wk.Y; wk.Wait = (float)rnd.NextDouble() * 2f; wk.StateT = 0.5f + (float)rnd.NextDouble() * 4f;
                    }
                    walkers.Add(wk);
                }
            }

            const float horizon = 24f;     // 장면 안에서 하늘이 끝나는 높이
            g.Clip(x, y, w, h, outer =>
            {
                // Clip 안의 좌표는 부모 기준 → 장면의 (0,0)이 구역 왼쪽 위가 되도록 옮긴다
                var c = new Canvas(new Rect(outer.Area.x + x * outer.S, outer.Area.y + y * outer.S, outer.Area.width, outer.Area.height), outer.S);
                // 하늘(시각) + 별(밤) + 구름(날씨)
                var env = Env();
                Color skyT, skyL; float dark; SkyOf(env.Hour, out skyT, out skyL, out dark);
                bool overcast = env.Weather != MWeather.Clear;
                if (overcast) { var grey = new Color(0.6f, 0.63f, 0.68f); skyT = Color.Lerp(skyT, grey * (1f - dark), 0.6f); skyL = Color.Lerp(skyL, grey * (1f - dark * 0.8f), 0.6f); }
                c.Fill(0f, 0f, w, horizon + 2f, skyT);
                c.Fill(0f, horizon - 8f, w, 10f, skyL);
                if (dark > 0.3f && !overcast)
                {
                    var sr = new System.Random(11);
                    for (int i = 0; i < 18; i++) { float tw = 0.5f + 0.5f * Mathf.Sin(now * (1.3f + i * 0.17f) + i); c.Fill((float)sr.NextDouble() * w, (float)sr.NextDouble() * (horizon - 9f), 1f, 1f, new Color(1f, 1f, 0.9f, (dark - 0.3f) * 3f * tw)); }
                }
                Color cloud = overcast ? new Color(0.78f, 0.8f, 0.84f, 0.9f) : new Color(1f, 1f, 1f, 0.85f);
                cloud = new Color(cloud.r * (1f - dark * 0.8f), cloud.g * (1f - dark * 0.8f), cloud.b * (1f - dark * 0.7f), cloud.a);
                for (int i = 0; i < (overcast ? 6 : 3); i++)
                {
                    float cx = ((now * (2.2f + i * 0.9f) + i * 83f) % (w + 60f)) - 30f, cy = 4f + (i % 3) * 6f;
                    c.Fill(cx, cy, 26f, 5f, cloud); c.Fill(cx + 5f, cy - 3f, 14f, 4f, cloud);
                }
                // 먼 언덕 + 들판(가로 띠로 원근감). 계절마다 풀빛이 바뀐다(겨울은 눈밭)
                Color hill, nearC, farC, tuft;
                switch (env.Season)
                {
                    case MSeason.Spring: hill = new Color(0.4f, 0.66f, 0.4f); farC = new Color(0.5f, 0.76f, 0.34f); nearC = new Color(0.36f, 0.6f, 0.25f); tuft = new Color(0.28f, 0.55f, 0.2f); break;
                    case MSeason.Fall: hill = new Color(0.6f, 0.45f, 0.25f); farC = new Color(0.72f, 0.58f, 0.3f); nearC = new Color(0.55f, 0.4f, 0.2f); tuft = new Color(0.6f, 0.32f, 0.12f); break;
                    case MSeason.Winter: hill = new Color(0.82f, 0.86f, 0.9f); farC = new Color(0.93f, 0.95f, 0.97f); nearC = new Color(0.8f, 0.84f, 0.88f); tuft = new Color(0.62f, 0.68f, 0.72f); break;
                    default: hill = new Color(0.36f, 0.6f, 0.38f); farC = new Color(0.42f, 0.68f, 0.3f); nearC = new Color(0.3f, 0.52f, 0.22f); tuft = new Color(0.25f, 0.5f, 0.18f); break;
                }
                c.Fill(0f, horizon - 5f, w, 7f, hill);
                for (int i = 0; i < 8; i++)
                {
                    float t = i / 7f;
                    c.Fill(0f, horizon + 2f + i * ((h - horizon - 2f) / 8f), w, (h - horizon - 2f) / 8f + 1f, Color.Lerp(farC, nearC, t));
                }
                // 풀포기와 꽃(고정 배치). 봄엔 꽃이 많고, 가을엔 낙엽, 겨울엔 꽃이 없다
                var fr = new System.Random(7);
                for (int i = 0; i < 46; i++)
                {
                    float gx = (float)fr.NextDouble() * w, gy = horizon + 4f + (float)fr.NextDouble() * (h - horizon - 6f);
                    int kind = fr.Next(5);
                    if (env.Season == MSeason.Spring && kind == 2) kind = 4;
                    if (kind < 3 || env.Season == MSeason.Winter) { c.Fill(gx, gy, 1f, 3f, tuft); c.Fill(gx + 2f, gy + 1f, 1f, 2f, tuft); }
                    else if (env.Season == MSeason.Fall) c.Fill(gx, gy, 2f, 1f, kind == 3 ? new Color(0.85f, 0.45f, 0.15f) : new Color(0.75f, 0.25f, 0.12f));
                    else c.Fill(gx, gy, 2f, 2f, kind == 3 ? new Color(0.98f, 0.9f, 0.4f) : env.Season == MSeason.Spring && fr.Next(2) == 0 ? new Color(0.98f, 0.62f, 0.78f) : new Color(0.97f, 0.97f, 0.97f));
                }

                // 림켓몬: 깊이 순(아래쪽이 앞)으로 그린다
                float depth = h - horizon - 12f;
                foreach (var wk in walkers.OrderBy(k => k.Y))
                {
                    // 행동
                    if (dt > 0f && wk.Fly) FlyStep(wk, dt, w, depth);
                    else if (dt > 0f)
                    {
                        if (wk.Speed <= 0f)
                        {
                            wk.Wait -= dt;
                            if (wk.Wait <= 0f)
                            {
                                wk.TX = 10f + UnityEngine.Random.value * (w - 20f);
                                wk.TY = wk.Fly ? 0.1f + UnityEngine.Random.value * 0.9f : 0.2f + UnityEngine.Random.value * 0.8f;
                                wk.Speed = wk.Fly ? 20f + UnityEngine.Random.value * 10f : 7f + UnityEngine.Random.value * 8f;
                                wk.AltT = wk.Fly ? UnityEngine.Random.value : 0f;
                            }
                        }
                        else
                        {
                            float gy0 = horizon + 8f + wk.Y * (h - horizon - 12f), gy1 = horizon + 8f + wk.TY * (h - horizon - 12f);
                            float dx = wk.TX - wk.X, dy = gy1 - gy0;
                            float dist = Mathf.Sqrt(dx * dx + dy * dy), step = wk.Speed * dt;
                            if (dist <= step) { wk.X = wk.TX; wk.Y = wk.TY; wk.Speed = 0f; wk.Wait = wk.Fly ? 0.3f + UnityEngine.Random.value * 1.2f : 1f + UnityEngine.Random.value * 3.5f; }
                            else
                            {
                                wk.X += dx / dist * step;
                                wk.Y = Mathf.Clamp01(wk.Y + dy / dist * step / (h - horizon - 12f));
                                if (Mathf.Abs(dx) > 0.5f) wk.Right = dx > 0f;
                                // 움직이는 방향에 맞는 그림: 가로가 크면 좌우, 세로가 크면 위/아래
                                wk.Dir = Mathf.Abs(dx) >= Mathf.Abs(dy) ? (dx > 0f ? 1 : 3) : (dy < 0f ? 0 : 2);
                            }
                        }
                    }
                    bool moving = wk.Fly ? wk.State != 0 || wk.Speed > 0f : wk.Speed > 0f;
                    bool airborne = wk.Fly && (wk.State != 0 || wk.Alt > 0.02f);
                    float footY = horizon + 8f + wk.Y * (h - horizon - 12f);
                    float persp = 0.78f + 0.3f * wk.Y;
                    var pawnArt = wk.C.PawnId != 0 ? PersonArt.PawnOf(wk.C) : null;
                    bool glitch = RimSprite.Glitched(wk.C);      // ?티어: 원래 모습 위에 미싱노(순수 ?티어는 효과만)
                    var pic = RimSprite.GetDir(wk.C, wk.Dir);
                    var fpic = airborne ? RimSprite.GetFlying(wk.C, wk.FlapT + wk.Phase, !wk.Right) : default(MonPic);     // 날고 있으면 날개짓 프레임(활공 중엔 멈춤)
                    if (pawnArt != null)
                    {
                        float ps = 30f * persp;
                        c.Fill(wk.X - 5f * persp, footY - 1f, 10f * persp, 2f, new Color(0f, 0.1f, 0f, 0.3f));
                        PersonArt.Draw(c, pawnArt, wk.X - ps * 0.35f, footY - ps * 0.74f - (moving ? Mathf.Abs(Mathf.Sin(now * 9f + wk.Phase)) * 1.4f : 0f), ps * 0.7f, ps, wk.Dir == 0 ? Rot4.North : wk.Dir == 1 ? Rot4.East : wk.Dir == 3 ? Rot4.West : Rot4.South, false);
                        NameTag(c, wk, footY - ps * 0.74f);
                        if (glitch) Window_RimketBattle.DrawGlitch(c, wk.X, footY, 1f, ps * 0.7f, ps);
                        continue;
                    }
                    // 포켓몬 모습: 배틀 그림이 아니라 걷는 그림(오버월드, 아래·왼쪽·오른쪽·위 × 4프레임)
                    if (!wk.SheetTried)
                    {
                        wk.SheetTried = true;
                        if (DefDatabase<ThingDef>.GetNamedSilentFail(wk.C.SpeciesId) == null) wk.Sheet = PokeBody.SheetId(wk.C);
                    }
                    var ftex = wk.Sheet != null ? PR.Tex(wk.Sheet) : null;
                    if (ftex != null)
                    {
                        int fw = ftex.width / 4, fh = ftex.height / 4;
                        int row = wk.Dir == 2 ? 0 : wk.Dir == 3 ? 1 : wk.Dir == 1 ? 2 : 3;
                        int col = moving ? (int)((now + wk.Phase) * 7f) % 4 : (((int)((now + wk.Phase) * 1.8f)) % 2 == 0 ? 0 : 1);      // 걸을 땐 4프레임, 쉴 땐 제자리 들썩
                        float fsc = PokeBody.SizeOf(wk.Sheet.Substring(wk.Sheet.IndexOf('/') + 1)) * 15f * 1.3f * persp / Mathf.Max(1, fw);      // 걷는 그림 한 칸엔 여백이 많아 조금 키운다
                        c.Fill(wk.X - 7f * persp, footY - 1f, 14f * persp, 2f, new Color(0f, 0.1f, 0f, 0.3f));
                        c.ImgRegion(ftex, col * fw, row * fh, fw, fh, wk.X, footY, 0.5f, 0.94f, fsc, 1f, 0f);      // 걷는 그림의 발은 칸 아래에서 6% 위(60/64)
                        float topFrac = Mathf.Clamp01(RimSprite.Bounds(ftex)[1] / (float)fh);
                        NameTag(c, wk, footY - (0.94f - topFrac) * fh * fsc);
                        if (glitch) Window_RimketBattle.DrawGlitch(c, wk.X, footY, 1f, 22f * persp, 24f * persp);
                        continue;
                    }
                    // 그림자
                    float flyH = airborne ? wk.Alt * 30f * persp : 0f;            // 나는 높이: 높을수록 그림자는 작고 옅다
                    float sw = 14f * persp * (airborne ? Mathf.Lerp(1f, 0.55f, flyH / 30f) : 1f);
                    c.Fill(wk.X - sw / 2f, footY - 1f, sw, 2f, new Color(0f, 0.1f, 0f, airborne ? 0.28f - 0.13f * flyH / 30f : 0.3f));
                    if (!pic.Valid)
                    {
                        if (glitch) { Window_RimketBattle.DrawGlitch(c, wk.X, footY, 1f, 20f * persp, 22f * persp); NameTag(c, wk, footY - 22f * persp); }
                        else c.Fill(wk.X - 3f, footY - 8f, 6f, 8f, new Color(0.9f, 0.4f, 0.4f));
                        continue;
                    }
                    // 림월드에서 차지하는 실제 크기(칸)에 비례해 그린다: 한 칸 = 15칸. 크기를 모르면(팬게임 종) 1.3칸으로 본다
                    float cells = pic.DrawCells > 0f ? pic.DrawCells : 1.3f;
                    float sc = cells * 15f * persp / Mathf.Max(1, pic.Tex.width);
                    float dyOff = pic.Scale > 0f ? pic.Pad / pic.Scale * sc : 0f, sy = 1f;
                    if (fpic.Valid)
                    {
                        float mult = (fpic.Scale * fpic.Tex.width) / Mathf.Max(0.01f, pic.Scale * pic.Tex.width);     // 날 때 그림 크기 배율
                        sc = cells * 15f * persp * mult / Mathf.Max(1, fpic.Tex.width);
                        dyOff = 0f;
                    }
                    float lift = 0f, ang = 0f;
                    if (airborne)
                    {
                        // 날갯짓 한 번마다 몸이 살짝 떠오르고(활공 중엔 잔잔), 방향을 꺾을 때는 그쪽으로 기운다
                        float flap = wk.Glide > 0f ? 0f : Mathf.Sin(wk.FlapT * 9f);
                        lift = flyH + flap * 1.3f * Mathf.Clamp01(wk.Alt * 3f);
                        sy = fpic.Valid ? 1f : 1f + 0.12f * flap;        // 프레임이 없으면 눌림으로 날갯짓 흉내
                        ang = Mathf.Clamp(wk.Bank, -14f, 14f) * Mathf.Clamp01(wk.Alt * 2f);
                    }
                    else if (moving) lift = Mathf.Abs(Mathf.Sin(now * 9f + wk.Phase)) * 1.6f;   // 걸을 때 통통
                    else sy = 1f + 0.03f * Mathf.Sin(now * 3f + wk.Phase);                       // 숨쉬기
                    // 원본은 왼쪽을 본다: 오른쪽으로 갈 때만 뒤집는다
                    bool side = wk.Dir == 1 || wk.Dir == 3;
                    bool flip = side ? (pic.Flip ^ (wk.Dir == 1)) : pic.Flip;
                    if (airborne && wk.Fly) { flip = pic.Flip ^ wk.Right; if (!side) { var sp2 = RimSprite.GetDir(wk.C, wk.Right ? 1 : 3); if (sp2.Valid) pic = sp2; } }      // 나는 동안은 항상 옆모습
                    if (fpic.Valid) { c.Tint = fpic.Tint; c.Img(fpic.Tex, wk.X, footY - lift, 0.5f, 0.6f, sc, 1f, fpic.Flip, ang, sy); NameTag(c, wk, footY - lift - fpic.Tex.height * sc * 0.6f); }
                    else
                    {
                        c.Tint = pic.Tint; c.Img(pic.Tex, wk.X, footY + dyOff - lift, 0.5f, 1f, sc, 1f, flip, ang, sy);
                        var bnd = RimSprite.Bounds(pic.Tex);
                        NameTag(c, wk, footY + dyOff - lift - (pic.Tex.height - bnd[1]) * sc * sy);
                    }
                    c.Tint = Color.white;
                    if (glitch) Window_RimketBattle.DrawGlitch(c, wk.X, footY - lift, 1f, 22f * persp, 24f * persp);
                }
                // 날씨(비 줄기·눈송이·안개)와 밤 어둠은 림켓몬 위에 덮는다
                if (env.Weather == MWeather.Rain)
                    for (int i = 0; i < 40; i++) { float rx = (i * 37.3f + now * 40f) % (w + 20f) - 10f, ry = (i * 23.7f + now * 160f) % (h + 10f) - 10f; c.Fill(rx, ry, 0.6f, 4f, new Color(0.75f, 0.82f, 0.95f, 0.55f)); }
                else if (env.Weather == MWeather.Snow)
                    for (int i = 0; i < 34; i++) { float sx = (i * 41.1f + Mathf.Sin(now * 0.9f + i) * 6f) % w, sy = (i * 19.3f + now * 14f) % (h + 4f) - 2f; c.Fill(sx, sy, 1.2f, 1.2f, new Color(1f, 1f, 1f, 0.85f)); }
                else if (env.Weather == MWeather.Fog)
                    c.Fill(0f, 0f, w, h, new Color(0.85f, 0.87f, 0.9f, 0.35f));
                if (dark > 0f) c.Fill(0f, horizon - 6f, w, h - horizon + 6f, new Color(0.02f, 0.03f, 0.12f, dark * 0.75f));
                // 테두리
                Color edge = new Color(0.1f, 0.1f, 0.14f);
                c.Fill(0f, 0f, w, 1f, edge); c.Fill(0f, h - 1f, w, 1f, edge); c.Fill(0f, 0f, 1f, h, edge); c.Fill(w - 1f, 0f, 1f, h, edge);
            });
        }
    }
}
