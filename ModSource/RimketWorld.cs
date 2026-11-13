using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Rimketmon.Engine;

namespace Rimketmon
{
    /// <summary>
    /// 림월드 지도 위의 림켓몬:
    ///  (1) 따라다니기 — 림켓링크 착용자가 '림켓몬 꺼내기'로 파티 맨 앞 림켓몬을 실제 동물로 꺼내 뒤에 데리고 다닌다.
    ///  (2) 방문자끼리의 승부 — 방문자 두 명이 지도 위에서 실제 엔진으로 림켓몬 배틀을 벌인다(구경용).
    /// 지도에 꺼낸 동물은 '몸'일 뿐이다: 다치거나 죽어도 림켓몬 자체(파티 데이터)에는 영향이 없다.
    /// </summary>
    public class RimketWorldComponent : GameComponent
    {
        public static RimketWorldComponent Instance { get { return Current.Game != null ? Current.Game.GetComponent<RimketWorldComponent>() : null; } }
        public RimketWorldComponent(Game game) { }

        // ---- 저장: 따라다니는 림켓몬과, 승부 중에 꺼낸 임시 몸들(불러오면 치운다)
        private Pawn master; private bool followOn;     // 따라다니는 림켓몬(그림만, 폰 없음)의 주인과 켜짐 여부
        private List<Pawn> tempBodies = new List<Pawn>();
        private WorldBattle fight;                      // 진행 중인 방문자 승부(저장하지 않는다)
        private int lastAutoRoll;

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving && tempBodies != null) tempBodies.RemoveAll(p => p == null || p.Destroyed);
            Scribe_References.Look(ref master, "rkFollowMaster");
            Scribe_Values.Look(ref followOn, "rkFollowOn", false);
            Scribe_Collections.Look(ref tempBodies, "rkTempBodies", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (tempBodies == null) tempBodies = new List<Pawn>();
                // 승부는 저장되지 않으므로, 저장 당시 나와 있던 몸은 치운다
                foreach (var p in tempBodies.ToList()) Vanish(p, false);
                tempBodies.Clear();
                PokeBody.Clear();
                if (master == null) followOn = false;
            }
        }

        // ------------------------------------------------------------------ 공통: 림켓몬의 몸

        public const string BodyDefName = "RimketPokemonBody";
        /// <summary>개발자용: 켜면 동물 몸이 있어도 포켓몬 모습으로 꺼낸다(걷는 그림 시험).</summary>
        public static bool DebugPokeOnly;
        /// <summary>모드가 지도에 꺼내 둔 몸인가(따라다니는 림켓몬·승부 중인 림켓몬). 포획·배틀 대상에서 뺀다.</summary>
        public static bool IsBody(Pawn p)
        {
            if (p == null) return false;
            if (p.def != null && p.def.defName == BodyDefName) return true;
            var c = Instance; return c != null && c.tempBodies.Contains(p);
        }

        /// <summary>이 림켓몬의 몸이 될 림월드 동물 종류. 동물 몸이 없으면(포켓몬 종족) 걷는 그림이 있을 때 '포켓몬의 몸'을 쓴다. 둘 다 없으면 null.</summary>
        public static PawnKindDef KindFor(RimketCreature c)
        {
            if (c == null || c.PawnId != 0) return null;
            if (!DebugPokeOnly && !string.IsNullOrEmpty(c.SpeciesId))
            {
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(c.SpeciesId);
                if (d != null && d.race != null && !d.race.Humanlike && d.defName != BodyDefName)
                {
                    var k = DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(x => x.race == d);
                    if (k != null) return k;
                }
            }
            return PokeBody.SheetId(c) != null ? DefDatabase<PawnKindDef>.GetNamedSilentFail(BodyDefName) : null;
        }

        public static void BallBurst(IntVec3 cell, Map map)
        {
            if (map == null) return;
            Vector3 c = cell.ToVector3Shifted();
            FleckMaker.Static(c, map, FleckDefOf.ExplosionFlash, 2.5f);
            FleckMaker.ThrowLightningGlow(c, map, 1.6f);
            for (int i = 0; i < 8; i++) FleckMaker.ThrowMicroSparks(c, map);
            for (int i = 0; i < 3; i++) FleckMaker.ThrowDustPuff(c, map, 1.0f);
            AddBurst(c, map);
        }

        // ---- 볼이 열릴 때 퍼지는 빛 알갱이(전투 화면과 같은 그림을 지도 위에 그린다)
        struct Burst { public Vector3 Pos; public Map Map; public float Start; }
        static readonly List<Burst> bursts = new List<Burst>();
        public static void AddBurst(Vector3 pos, Map map)
        {
            if (map == null) return;
            float now = Time.realtimeSinceStartup;
            for (int i = bursts.Count - 1; i >= 0; i--) if (now - bursts[i].Start < 0.1f && bursts[i].Map == map && (bursts[i].Pos - pos).sqrMagnitude < 0.5f) return;     // 같은 자리에 겹쳐 그리지 않는다
            bursts.Add(new Burst { Pos = pos, Map = map, Start = now });
        }

        static void DrawBursts()
        {
            if (bursts.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            bursts.RemoveAll(b => now - b.Start > 1.2f);
            if (RimWorld.Planet.WorldRendererUtility.WorldRendered) return;
            var tex = PR.Tex("ui/pb_particles");
            if (tex == null) return;
            int[][] fr = { new[] { 0, 8, 8 }, new[] { 8, 4, 8 }, new[] { 12, 8, 8 }, new[] { 20, 8, 4 } };
            foreach (var b in bursts)
            {
                if (b.Map != Find.CurrentMap) continue;
                Vector2 c0 = b.Pos.MapToUIPosition(), c1 = (b.Pos + new Vector3(1f, 0f, 0f)).MapToUIPosition();
                float cellPx = Mathf.Abs(c1.x - c0.x); if (cellPx < 4f) continue;
                float s = cellPx / 32f;      // 반지름 48 → 한 칸 반
                var g = new Canvas(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight), s);
                float since = now - b.Start;
                for (int i = 1; i <= 17; i++)
                {
                    float t = since - 0.02f * (i - 1);
                    if (t < 0f || t > 0.575f) continue;
                    float k = t / 0.575f, ang = i * 45f * Mathf.Deg2Rad;
                    float alpha = t > 0.5f ? 1f - (t - 0.5f) / 0.075f : 1f;
                    var f = fr[((i + 3) % 4 + Mathf.FloorToInt(t * 16f)) % 4];
                    g.ImgRegion(tex, f[0], 0, f[1], f[2], c0.x / s + 48f * k * Mathf.Cos(ang), c0.y / s + 48f * k * Mathf.Sin(ang), 0.5f, 0.5f, 1f, Mathf.Clamp01(alpha), 0f);
                }
            }
            GUI.color = Color.white;
        }

        public static Pawn SpawnBody(RimketCreature c, Faction f, IntVec3 cell, Map map)
        {
            var kind = KindFor(c);
            if (kind == null || map == null) return null;
            Pawn p = PawnGenerator.GeneratePawn(kind, f);
            try { if (!string.IsNullOrEmpty(c.Name)) p.Name = new NameSingle(c.Name); } catch (Exception) { }
            if (kind.defName == BodyDefName) PokeBody.Register(p, PokeBody.SheetId(c));
            GenSpawn.Spawn(p, cell, map);
            BallBurst(cell, map);
            RimketAudio.Play(RimketmonDefOf.Rimket_Enter);
            Cry(c, cell, map);
            return p;
        }

        private static void Vanish(Pawn p, bool effect)
        {
            if (p == null || p.Destroyed) return;
            try
            {
                if (effect && p.Spawned) { for (int i = 0; i < 3; i++) FleckMaker.ThrowDustPuff(p.DrawPos, p.Map, 1.0f); FleckMaker.ThrowLightningGlow(p.DrawPos, p.Map, 1.2f); }
                PawnRemoval.Remove(p);
            }
            catch (Exception ex) { Log.Warning("[림켓몬] 몸 치우기: " + ex.Message); }
        }

        // ------------------------------------------------------------------ (1) 따라다니기
        // 실제 폰을 만들지 않는다: 그림만 주인 뒤를 따라간다(틱을 쓰지 않고, 화면을 그릴 때만 계산한다).
        // 보여 주는 것은 항상 "지금 파티 맨 앞"의 림켓몬이다.

        public bool HasFollower(Pawn of) { return followOn && master == of; }

        // ---- 교감(놀아 주기 등) 중의 움직임
        private int interactKind = -1;
        public float InteractProgress;
        private Vector3 flyFrom, flyTo;      // 발로 차기·들어서 던지기: 날아갈 출발·도착 자리
        public void BeginInteract(int kind, Pawn by = null)
        {
            interactKind = kind; InteractProgress = 0f; fVel = Vector3.zero;
            flyFrom = flyTo = fPos;
            if ((kind == RimketInteract.Kick || kind == RimketInteract.Throw) && master != null && master.Map != null)
            {
                Vector3 away = fPos - master.DrawPos; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -master.Rotation.FacingCell.ToVector3();
                away.Normalize();
                flyTo = LandingSpot(master.Map, fPos, away, kind == RimketInteract.Kick ? 5.5f : 3f);
            }
        }
        public void EndInteract()
        {
            if ((interactKind == RimketInteract.Kick || interactKind == RimketInteract.Throw) && InteractProgress > 0.5f) { fPos = flyTo; fVel = Vector3.zero; }
            interactKind = -1; InteractProgress = 0f;
        }

        /// <summary>from에서 dir 쪽으로 최대 dist칸: 벽·물체(지나갈 수 없는 칸)·지도 끝에 걸리면 그 앞에서 멈춘다.</summary>
        static Vector3 LandingSpot(Map map, Vector3 from, Vector3 dir, float dist)
        {
            Vector3 last = from;
            for (float d = 0.5f; d <= dist; d += 0.5f)
            {
                var p = from + dir * d; var c = p.ToIntVec3();
                if (!c.InBounds(map) || !c.Walkable(map) || c.GetEdifice(map) != null && c.GetEdifice(map).def.passability != Traversability.Standable) break;
                last = p;
            }
            last.y = 0f;
            return last;
        }

        /// <summary>림켓몬이 맞는 연출(때리기): 막치기(POUND) 기술 연출을 림켓몬 위에 재생하고 하얗게 번쩍인다(효과음 없이).</summary>
        public void PlayHit() { hitAnim = WorldAnim.Of("POUND"); if (hitAnim != null) hitAnim.Mute = true; hitFlash = Time.realtimeSinceStartup; }
        private WorldAnim hitAnim; private float hitFlash = -10f;
        public bool FollowerPos(out Vector3 pos) { pos = fPos; return followOn && fPosValid; }

        /// <summary>교감 종류와 진행도(0~1)에 따른 그림 위치·방향.</summary>
        private Vector3 InteractDraw(Vector3 basePos, ref int dir)
        {
            if (interactKind < 0 || master == null) return basePos;
            float p = InteractProgress, tt = Time.realtimeSinceStartup;
            Vector3 toMaster = master.DrawPos - basePos; toMaster.y = 0f;
            int faceMaster = Mathf.Abs(toMaster.x) > Mathf.Abs(toMaster.z) ? (toMaster.x > 0f ? 1 : 3) : (toMaster.z > 0f ? 0 : 2);
            FollowDraw.Spin = 0f;
            switch (interactKind)
            {
                case RimketInteract.Play:      // 놀아 주기: 좌우로 방향을 바꿔 가며 깡충깡충
                    dir = ((int)(p * 8f)) % 2 == 0 ? 1 : 3;
                    return basePos + new Vector3(Mathf.Sin(p * Mathf.PI * 4f) * 0.25f, 0f, Mathf.Abs(Mathf.Sin(p * Mathf.PI * 8f)) * 0.35f);
                case RimketInteract.Hug:      // 안아 주기: 주인 품으로 들어가 안겼다가 내려온다
                    {
                        float k = p < 0.2f ? p / 0.2f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                        dir = 2;
                        var hold = master.DrawPos + new Vector3(0f, 0f, 0.28f); hold.y = 0f;
                        return Vector3.Lerp(basePos, hold, PR.CubicOut(k)) + new Vector3(0f, 0f, Mathf.Sin(tt * 4f) * 0.02f * k);
                    }
                case RimketInteract.Snack:      // 간식: 고개를 콕콕
                    dir = faceMaster;
                    return basePos + new Vector3(0f, 0f, -Mathf.Abs(Mathf.Sin(p * Mathf.PI * 10f)) * 0.08f);
                case RimketInteract.Brush:      // 털 빗기: 기분 좋게 좌우로
                    dir = faceMaster;
                    return basePos + new Vector3(Mathf.Sin(tt * 5f) * 0.05f, 0f, 0f);
                case RimketInteract.Hit:      // 때리기: 뒤로 밀려나며 부들부들
                    {
                        dir = faceMaster;
                        float k = p < 0.33f ? 0f : p < 0.55f ? (p - 0.33f) / 0.22f : 1f;
                        Vector3 away = -toMaster.normalized * 0.55f * PR.CubicOut(k);
                        float shake = p > 0.33f && p < 0.8f ? Mathf.Sin(tt * 60f) * 0.05f : 0f;
                        return basePos + away + new Vector3(shake, 0f, 0f);
                    }
                case RimketInteract.Kick:      // 발로 차기: 공처럼 포물선으로 날아가 떨어진 뒤 데굴데굴 구르다 멈춘다
                    {
                        dir = faceMaster;
                        const float k0 = 0.18f, k1 = 0.6f, k2 = 0.85f;
                        if (p < k0) return flyFrom;
                        Vector3 dirv = flyTo - flyFrom; float len = dirv.magnitude; Vector3 n = len > 0.01f ? dirv / len : Vector3.zero;
                        Vector3 bounceEnd = flyTo, flightEnd = flyTo - n * Mathf.Min(0.8f, len * 0.2f);
                        if (p < k1)
                        {
                            float u = (p - k0) / (k1 - k0);
                            FollowDraw.Spin = u * 720f * (n.x >= 0f ? -1f : 1f);
                            return Vector3.Lerp(flyFrom, flightEnd, u) + new Vector3(0f, 0f, Mathf.Sin(u * Mathf.PI) * Mathf.Clamp(len * 0.35f, 0.6f, 1.6f));
                        }
                        if (p < k2)
                        {
                            float u = (p - k1) / (k2 - k1);
                            FollowDraw.Spin = (720f + u * 360f) * (n.x >= 0f ? -1f : 1f);
                            return Vector3.Lerp(flightEnd, bounceEnd, PR.CubicOut(u)) + new Vector3(0f, 0f, Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2f)) * 0.25f * (1f - u));
                        }
                        float w = (p - k2) / (1f - k2);
                        return bounceEnd + new Vector3(w < 0.6f ? Mathf.Sin(tt * 50f) * 0.04f : 0f, 0f, 0f);      // 어질어질
                    }
                case RimketInteract.Tail:      // 꼬리 잡아당기기: 주인 쪽으로 질질 끌려가다 놓이면 휙 돌아본다
                    {
                        Vector3 toward = toMaster.normalized;
                        if (p < 0.15f) { dir = (faceMaster + 2) % 4; return basePos; }
                        if (p < 0.6f) { dir = (faceMaster + 2) % 4; float u = (p - 0.15f) / 0.45f; return basePos + toward * 0.45f * u + new Vector3(Mathf.Sin(tt * 40f) * 0.03f, 0f, 0f); }
                        float v = Mathf.Min(1f, (p - 0.6f) / 0.12f);
                        dir = faceMaster;
                        return basePos + toward * 0.45f * (1f - PR.CubicOut(v)) + new Vector3(0f, 0f, p < 0.75f ? Mathf.Sin(v * Mathf.PI) * 0.3f : 0f);
                    }
                case RimketInteract.Throw:      // 들어서 던지기: 들어 올려 앞으로 던지면 뒤집힌 채 떨어졌다가 잠시 뒤 벌떡 일어난다
                    {
                        const float a0 = 0.3f, a1 = 0.62f, a2 = 0.88f;
                        var hold = master.DrawPos + new Vector3(0f, 0f, 0.75f); hold.y = 0f;
                        if (p < a0) { dir = 2; float u = p / a0; return Vector3.Lerp(flyFrom, hold, PR.CubicOut(u)); }
                        if (p < a1)
                        {
                            float u = (p - a0) / (a1 - a0);
                            FollowDraw.Spin = 180f * u;
                            return Vector3.Lerp(hold, flyTo, u) + new Vector3(0f, 0f, Mathf.Sin(u * Mathf.PI) * 1.1f);
                        }
                        if (p < a2) { FollowDraw.Spin = 180f; return flyTo + new Vector3(Mathf.Sin(tt * 30f) * 0.03f, 0f, 0f); }      // 뒤집힌 채 버둥버둥
                        float w = (p - a2) / (1f - a2);
                        FollowDraw.Spin = w < 0.5f ? 180f * (1f - w * 2f) : 0f;
                        dir = faceMaster;
                        return flyTo + new Vector3(0f, 0f, Mathf.Sin(Mathf.Min(1f, w * 2f) * Mathf.PI) * 0.35f);
                    }
            }
            return basePos;
        }
        public bool AnyFollower { get { return followOn; } }
        private Vector3 fPos, fVel; private int fDir = 2; private bool fMoving, fPosValid; private float fStep;
        public const float FollowGap = 1.3f;              // 주인 등 뒤로 몇 칸
        const float FollowWalk = 2.6f, FollowRun = 7f;    // 칸/게임초: 가까이에선 걷는 속도로(방향을 바꿔도 휙 옮기지 않게), 멀어지면 빨리

        static RimketCreature Front() { var pc = PartyComponent.Instance; return pc != null ? pc.PartyList().FirstOrDefault() : null; }

        /// <summary>볼이 떨어진 자리에 파티 맨 앞 림켓몬을 꺼낸다.</summary>
        public void Summon(Pawn owner, IntVec3 cell)
        {
            if (owner == null || owner.Map == null) return;
            var first = Front();
            if (first == null) { Messages.Message("꺼낼 림켓몬이 없다.", MessageTypeDefOf.RejectInput, false); return; }
            if (!FollowDraw.CanShow(first)) { Messages.Message(first.Name + "은(는) 지도에 꺼낼 수 있는 모습이 없다.", MessageTypeDefOf.RejectInput, false); return; }
            if (!cell.InBounds(owner.Map)) cell = owner.Position;
            followOn = true; master = owner; fPos = cell.ToVector3Shifted(); fPosValid = true; fDir = 2;
            BallBurst(cell, owner.Map);
            Cry(first, cell, owner.Map);
            MoteMaker.ThrowText(fPos, owner.Map, "가라! " + first.Name + "!", Color.white, 2.5f);
        }

        public void Recall(bool effect = true)
        {
            if (followOn && effect && master != null && master.Spawned && fPosValid)
            {
                MoteMaker.ThrowText(fPos, master.Map, "돌아와!", Color.white, 2f);
                for (int i = 0; i < 3; i++) FleckMaker.ThrowDustPuff(fPos, master.Map, 1.0f);
                FleckMaker.ThrowLightningGlow(fPos, master.Map, 1.2f);
            }
            followOn = false; master = null; fPosValid = false;
        }

        /// <summary>등장 울음소리: 림월드 동물은 그 동물의 소리, 포켓몬은 포켓몬 울음소리.</summary>
        public static void Cry(RimketCreature c, IntVec3 cell, Map map)
        {
            try
            {
                if (c == null || map == null || map != Find.CurrentMap) return;
                // 배틀 화면과 같은 크기 맞추기를 거친다. 화면 밖으로 멀어질수록 작아지고, 많이 멀리 당겨 볼수록 조금 작아진다
                var view = Find.CameraDriver.CurrentViewRect;
                float dx = Mathf.Max(0f, Mathf.Max(view.minX - cell.x, cell.x - view.maxX)), dz = Mathf.Max(0f, Mathf.Max(view.minZ - cell.z, cell.z - view.maxZ));
                float vol = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz) / 30f) * Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(20f, 60f, Find.CameraDriver.RootSize));
                if (vol <= 0.02f) return;
                if (DebugPokeOnly && !string.IsNullOrEmpty(c.Species)) { RimketAudio.PlayCry(c.Species, vol); return; }
                RimketAudio.PlayCryFor(c, vol);
            }
            catch (Exception) { }
        }

        /// <summary>화면을 그릴 때마다: 주인 등 뒤 자리로 부드럽게 따라가고 그 자리에 그림을 그린다.</summary>
        private void FollowerUpdate()
        {
            if (!followOn) return;
            if (master == null || master.Dead || !master.Spawned) { Recall(false); return; }
            var map = master.Map;
            Vector3 back = master.Rotation.FacingCell.ToVector3();
            Vector3 target = master.DrawPos - back * FollowGap; target.y = 0f;
            if (!fPosValid) { fPos = target; fPosValid = true; fVel = Vector3.zero; }
            fPos.y = 0f;
            float dt = Time.deltaTime * (Find.TickManager.Paused ? 0f : Find.TickManager.TickRateMultiplier);
            if (dt > 0.1f) dt = 0.1f;
            if (interactKind >= 0) target = fPos;      // 교감 중에는 제자리
            Vector3 delta = target - fPos; float dist = delta.magnitude;
            if (dist > 12f) { fPos = target; fVel = Vector3.zero; dist = 0f; delta = Vector3.zero; }      // 너무 멀어지면(순간이동 등) 바로 옆으로
            if (dt > 0f)
            {
                // 부드럽게 따라가기: 목표 쪽 속도를 천천히 바꾼다(가속 제한). 가까우면 걷는 속도, 멀면 뛰는 속도까지.
                // 예전에는 매 프레임 '움직임/멈춤'과 방향이 번갈아 바뀌어 그림이 바르르 떨렸다 → 속도와 문턱(이력)으로 막는다
                float maxSpeed = Mathf.Lerp(FollowWalk, FollowRun, Mathf.InverseLerp(1.2f, 5f, dist));
                Vector3 want = dist > 0.02f ? delta / dist * Mathf.Min(maxSpeed, dist * 2.2f) : Vector3.zero;
                fVel = Vector3.MoveTowards(fVel, want, 12f * dt);
                fPos += fVel * dt;
                float spd = fVel.magnitude;
                if (!fMoving && spd > 0.6f) fMoving = true;
                else if (fMoving && spd < 0.25f && dist < 0.15f) fMoving = false;
                if (fMoving)
                {
                    fStep += dt * Mathf.Lerp(5f, 9f, Mathf.InverseLerp(FollowWalk, FollowRun, spd));
                    if (spd > 0.4f)
                    {
                        float ax = Mathf.Abs(fVel.x), az = Mathf.Abs(fVel.z);
                        bool horiz = fDir == 1 || fDir == 3;
                        if (horiz ? az > ax * 1.3f : ax > az * 1.3f) horiz = !horiz;      // 축은 확실히 바뀔 때만 바꾼다(대각선에서 깜빡이지 않게)
                        fDir = horiz ? (fVel.x > 0f ? 1 : 3) : (fVel.z > 0f ? 0 : 2);
                    }
                }
            }
            if (!fMoving) fDir = master.Rotation == Rot4.North ? 0 : master.Rotation == Rot4.East ? 1 : master.Rotation == Rot4.South ? 2 : 3;
            if (map != Find.CurrentMap || RimWorld.Planet.WorldRendererUtility.WorldRendered) return;
            var c = Front(); if (c == null) { Recall(false); return; }
            if (fPos.ToIntVec3().InBounds(map) && fPos.ToIntVec3().Fogged(map)) return;
            int drawDir = fDir;
            FollowDraw.Spin = 0f;
            var drawPos = InteractDraw(fPos, ref drawDir);
            float since = Time.realtimeSinceStartup - hitFlash;
            if (since >= 0f && since < 0.35f && ((int)(since / 0.07f)) % 2 == 0) drawPos += new Vector3(Mathf.Sin(since * 90f) * 0.06f, 0f, 0f);      // 맞은 순간 깜빡이며 흔들림(배틀 피격과 같은 느낌)
            else FollowDraw.Draw(c, drawPos, drawDir, fMoving && interactKind < 0, fStep);
            FollowDraw.Spin = 0f;
            if (RimSprite.Glitched(c)) GlitchDraw.DrawOn(drawPos, 7);
            if (hitAnim != null) { if (hitAnim.Done) hitAnim = null; else hitAnim.Draw(drawPos + new Vector3(0f, 0f, 0.45f)); }
        }

        // ------------------------------------------------------------------ (2) 방문자끼리의 승부

        public bool FightActive { get { return fight != null; } }

        public static bool Eligible(Pawn p)
        {
            return p != null && p.Spawned && !p.Dead && !p.Downed && p.RaceProps.Humanlike && !p.InMentalState && p.Awake() && !p.Drafted;
        }

        public bool StartFight(Pawn a, Pawn b)
        {
            if (fight != null || !Eligible(a) || !Eligible(b) || a == b || a.Map != b.Map) return false;
            var f = WorldBattle.TryCreate(a, b, this);
            if (f == null) return false;
            fight = f;
            return true;
        }

        internal void Track(Pawn body) { if (body != null) tempBodies.Add(body); }
        internal void Untrack(Pawn body, bool effect) { tempBodies.Remove(body); Vanish(body, effect); }

        private void AutoFight()
        {
            if (fight != null) return;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome || GenHostility.AnyHostileActiveThreatToPlayer(map)) continue;
                foreach (var lord in map.lordManager.lords)
                {
                    if (!(lord.LordJob is LordJob_VisitColony) || lord.faction == null || lord.faction.HostileTo(Faction.OfPlayer)) continue;
                    var cand = lord.ownedPawns.Where(Eligible).ToList();
                    if (cand.Count < 2 || !Rand.Chance(RimketmonMod.Settings.visitorFightChance)) continue;
                    var a = cand.RandomElement(); var b = cand.Where(x => x != a).RandomElement();
                    if (StartFight(a, b)) return;
                }
            }
        }

        /// <summary>모드 제거: 진행 중인 방문자 승부를 끝내고 잠깐 꺼낸 몸을 없앤다.</summary>
        public void UninstallCleanup()
        {
            if (fight != null) { try { fight.Cleanup(false); } catch (Exception) { } fight = null; }
            foreach (var b in tempBodies.ToList()) { try { if (b != null && !b.Destroyed) b.Destroy(); } catch (Exception) { } }
            tempBodies.Clear();
        }

        public override void GameComponentTick()
        {
            if (RimketUninstall.Removed) return;
            int t = Find.TickManager.TicksGame;
            if (fight != null)
            {
                try { if (!fight.Tick()) fight = null; }
                catch (Exception ex) { Log.Warning("[림켓몬] 방문자 승부: " + ex); try { fight.Cleanup(false); } catch (Exception) { } fight = null; }
            }
            else if (t - lastAutoRoll >= 2500) { lastAutoRoll = t; try { AutoFight(); } catch (Exception ex) { Log.Warning("[림켓몬] 방문자 승부 시작: " + ex.Message); } }
        }

        public override void GameComponentUpdate()
        {
            if (RimketUninstall.Removed) return;
            try { PokeBody.DrawAll(); } catch (Exception) { }
            try { GlitchDraw.DrawAll(); } catch (Exception) { }
            try { FollowerUpdate(); } catch (Exception ex) { Log.WarningOnce("[림켓몬] 따라다니기 그리기: " + ex, 99117731); }
        }

        public override void GameComponentOnGUI()
        {
            if (Event.current.type != EventType.Repaint || RimketUninstall.Removed) return;
            try { DrawBursts(); } catch (Exception) { }
            if (fight == null) return;
            try { fight.DrawGui(); } catch (Exception) { }
        }
    }

    /// <summary>지도 위에서 벌어지는 한 판(방문자 A 대 방문자 B). 실제 엔진으로 진행하고, 로그를 한 줄씩 띄우며 사건에 맞춰 몸을 꺼내고 넣는다.</summary>
    internal sealed class WorldBattle
    {
        const int LineTicks = 110, ApproachLimit = 1800, EndTicks = 300, Gap = 8;
        readonly RimketWorldComponent comp;
        readonly Pawn[] who = new Pawn[2];
        readonly IntVec3[] spot = new IntVec3[2], monCell = new IntVec3[2];
        readonly Pawn[] body = new Pawn[2];
        readonly Fighter[] bodyOf = new Fighter[2];
        readonly Map map;
        readonly RimketBattle battle;
        readonly EngineSettings settings;
        readonly int startTick;
        int shown, evIndex, nextLine, endAt = -1; bool arrived, over;
        MoveAnimPlayer anim; Pawn animUser, animTarget;
        bool closed;      // 정리된 뒤에는 날아가던 볼에서 림켓몬이 나오지 않는다

        WorldBattle(Pawn a, Pawn b, IntVec3 sa, IntVec3 sb, RimketBattle battle, EngineSettings s, RimketWorldComponent comp)
        {
            who[0] = a; who[1] = b; spot[0] = sa; spot[1] = sb; map = a.Map; this.battle = battle; settings = s; this.comp = comp;
            var dir = (sb - sa); int dx = Math.Sign(dir.x), dz = Math.Sign(dir.z);
            monCell[0] = new IntVec3(sa.x + dx * 2, 0, sa.z + dz * 2); monCell[1] = new IntVec3(sb.x - dx * 2, 0, sb.z - dz * 2);
            startTick = Find.TickManager.TicksGame;
        }

        public static WorldBattle TryCreate(Pawn a, Pawn b, RimketWorldComponent comp)
        {
            var map = a.Map; IntVec3 sa = a.Position, sb = IntVec3.Invalid;
            // 상대가 설 자리: A에게서 가로나 세로로 6칸, 사이가 전부 비어 있어야 한다
            foreach (var d in new[] { new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1) }.InRandomOrder())
            {
                bool ok = true;
                for (int i = 1; i <= Gap && ok; i++) { var c = sa + d * i; ok = c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null || (i == Gap && c.InBounds(map) && c.Standable(map)); }
                var end = sa + d * Gap;
                if (ok && b.CanReach(end, PathEndMode.OnCell, Danger.Some)) { sb = end; break; }
            }
            if (!sb.IsValid) return null;
            var pc = PartyComponent.Instance; int badges = pc != null ? pc.Badges : 0;
            var s = RimketmonMod.Settings.ToEngine();
            var pa = ChallengeBuilder.BuildParty(badges, new GameRng(), s, 3, 3, true).Where(c => RimketWorldComponent.KindFor(c) != null).ToList();
            var pb = ChallengeBuilder.BuildParty(badges, new GameRng(), s, 3, 3, true).Where(c => RimketWorldComponent.KindFor(c) != null).ToList();
            if (pa.Count == 0 || pb.Count == 0) return null;
            var battle = RimketBattle.OnlineBattle(new GameRng(), s, a.LabelShortCap, pa, b.LabelShortCap, pb);
            if (RimketmonMod.Settings.envField)
            {
                var env = BattleEnv.Compute(map, new IntVec3((sa.x + sb.x) / 2, 0, (sa.z + sb.z) / 2));      // 두 사람 사이의 날씨·지형
                if (env.W != Weather.None || env.T != Rimketmon.Engine.Terrain.None) battle.ApplyEnvironment(env.W, env.T);
            }
            var f = new WorldBattle(a, b, sa, sb, battle, s, comp);
            MoteMaker.ThrowText(a.DrawPos + new Vector3(0f, 0f, 0.8f), map, b.LabelShortCap + ", 승부다!", new Color(1f, 0.9f, 0.4f), 3f);
            var go = JobMaker.MakeJob(JobDefOf.Goto, sb); go.locomotionUrgency = LocomotionUrgency.Jog;
            b.jobs.StartJob(go, JobCondition.InterruptForced);
            f.Hold(a, 0);
            return f;
        }

        void Hold(Pawn p, int side)
        {
            if (p == null || !p.Spawned) return;
            StandFacing(p, Facing(spot[side], spot[1 - side]));
        }

        static Rot4 Facing(IntVec3 from, IntVec3 to)
        {
            int dx = to.x - from.x, dz = to.z - from.z;
            return Math.Abs(dx) >= Math.Abs(dz) ? (dx >= 0 ? Rot4.East : Rot4.West) : (dz >= 0 ? Rot4.North : Rot4.South);
        }

        /// <summary>
        /// 제자리에 서서 한 방향만 보게 한다. 림월드의 '대기' 일은 주변을 두리번거리게 하므로, 일 자체에 바라볼 방향을 지정한다(overrideFacing).
        /// 방문자의 원래 일(무리 행동)이 끼어들지 않도록 대기 일을 길게 주고, 다른 일로 바뀌었을 때만 다시 준다.
        /// </summary>
        static void StandFacing(Pawn p, Rot4 face)
        {
            var cur = p.CurJob;
            if (cur == null || cur.def != JobDefOf.Wait || cur.overrideFacing != face)
            {
                var j = JobMaker.MakeJob(JobDefOf.Wait); j.expiryInterval = 5000; j.overrideFacing = face; j.checkOverrideOnExpire = false;
                p.jobs.StartJob(j, JobCondition.InterruptForced);
            }
            if (p.Rotation != face) p.Rotation = face;
        }

        bool Harmed(Pawn p) { return p == null || p.Dead || p.Downed || !p.Spawned || p.Map != map || p.InMentalState || (p.mindState != null && p.mindState.lastHarmTick > startTick) || p.IsBurning(); }

        string AbortReason()
        {
            for (int i = 0; i < 2; i++)
            {
                if (Harmed(who[i])) return who[i] != null ? who[i].LabelShortCap + "에게 일이 생겼다" : "상대가 사라졌다";
                if (body[i] != null && Harmed(body[i])) return "림켓몬이 다쳤다";
                if (who[i].HostileTo(who[1 - i]) || who[i].Drafted) return "더는 승부할 상황이 아니다";
            }
            if (GenHostility.AnyHostileActiveThreatToPlayer(map)) return "위험이 닥쳤다";
            return null;
        }

        Vector3 Mid { get { return (spot[0].ToVector3Shifted() + spot[1].ToVector3Shifted()) * 0.5f + new Vector3(0f, 0f, 1.6f); } }
        void Say(string text, Color c) { MoteMaker.ThrowText(Mid, map, text, c, 2.2f); }

        /// <summary>한 틱 진행. 끝났으면 false.</summary>
        public bool Tick()
        {
            int t = Find.TickManager.TicksGame;
            if (endAt >= 0) { if (t >= endAt) { Cleanup(true); return false; } if (t % 60 == 0) for (int i = 0; i < 2; i++) Hold(who[i], i); return true; }
            if (t % 30 == 0)
            {
                string why = AbortReason();
                if (why != null) { Say("승부가 중단되었다! (" + why + ")", new Color(1f, 0.5f, 0.4f)); Cleanup(true); return false; }
            }
            if (!arrived)
            {
                if (who[1].Position == spot[1]) { arrived = true; nextLine = t + 60; }
                else if (t - startTick > ApproachLimit) { Cleanup(false); return false; }
                else
                {
                    if (t % 120 == 0 && (who[1].CurJob == null || who[1].CurJob.def != JobDefOf.Goto))
                    { var go = JobMaker.MakeJob(JobDefOf.Goto, spot[1]); who[1].jobs.StartJob(go, JobCondition.InterruptForced); }
                    if (t % 60 == 0) Hold(who[0], 0);
                    return true;
                }
            }
            if (t % 60 == 0) for (int i = 0; i < 2; i++) { Hold(who[i], i); if (body[i] != null) HoldBody(i); }
            if (t < nextLine || (anim != null && !anim.Done(Time.realtimeSinceStartup))) return true;

            if (shown < battle.Log.Count)
            {
                shown++; nextLine = t + LineTicks;
                string line = battle.Log[shown - 1];
                int cut = line.IndexOf(" (HP"); if (cut > 0) line = line.Substring(0, cut);
                Say(line, Color.white);
                while (evIndex < battle.Events.Count && battle.Events[evIndex].LogIndex < shown) Apply(battle.Events[evIndex++]);
                return true;
            }
            while (evIndex < battle.Events.Count) Apply(battle.Events[evIndex++]);
            if (battle.IsOver)
            {
                if (!over)
                {
                    over = true;
                    string win = battle.Draw ? "무승부!" : (battle.Result.End == BattleEnd.Victory ? who[0] : who[1]).LabelShortCap + "의 승리!";
                    Say(win, new Color(1f, 0.9f, 0.4f));
                    endAt = t + EndTicks;
                }
                return true;
            }
            // 다음 수: 양쪽 모두 간단한 판단(가장 아픈 기술)으로 고른다
            BattleAction? pa = null, fa = null;
            if (!battle.PendingFoeSwitch || battle.PendingSwitch) pa = Pick(battle.Player, battle.LegalActions(), battle.Foe);
            if (!battle.PendingSwitch || battle.PendingFoeSwitch) fa = Pick(battle.Foe, battle.FoeLegalActions(), battle.Player);
            if (!battle.OnlineAct(pa, fa) || battle.Turn > 60) { Say("승부가 흐지부지 끝났다.", Color.white); Cleanup(true); return false; }
            nextLine = t + 40;
            return true;
        }

        BattleAction? Pick(BattleSide side, IList<BattleAction> legal, BattleSide other)
        {
            BattleAction? best = null; double bv = -1;
            foreach (var a in legal)
            {
                if (a.Kind == ActionKind.Disconnect) continue;
                double v = 0;
                if (a.Kind == ActionKind.UseMove)
                {
                    if (battle.MoveBlockedFor(side, a.Index) != null) continue;
                    var m = side.Active.C.Moves[a.Index].Move;
                    v = 1 + (other.Active != null ? RankedAI.Damage(side.Active.C, m, other.Active.C, 1, 1, settings) : 0) + Rand.Value * 8f;
                }
                else if (a.Kind == ActionKind.Switch) v = (side == battle.Player ? battle.PendingSwitch : battle.PendingFoeSwitch) ? 5 + Rand.Value : 0.01;
                else v = 0.5;
                if (v > bv) { bv = v; best = a; }
            }
            return best;
        }

        void HoldBody(int i)
        {
            var p = body[i]; if (p == null || !p.Spawned) return;
            if (p.needs != null && p.needs.food != null) p.needs.food.CurLevelPercentage = 1f;
            StandFacing(p, Facing(monCell[i], monCell[1 - i]));
        }

        void Apply(BattleEvent e)
        {
            int i = e.PlayerSide ? 0 : 1;
            switch (e.Kind)
            {
                case BattleEventKind.SendOut:
                    if (body[i] != null) { comp.Untrack(body[i], true); body[i] = null; }
                    var cell = monCell[i]; if (!cell.Standable(map)) cell = CellFinder.RandomClosewalkCellNear(cell, map, 1);
                    bodyOf[i] = e.Who;
                    {
                        // 트레이너가 볼을 실제로 던지고, 볼이 떨어져 열린 자리에서 림켓몬이 나온다
                        int idx = i; var fighter = e.Who; var at = cell;
                        Projectile_RimketBall.ThrowThen(who[i], cell, () =>
                        {
                            if (closed || bodyOf[idx] != fighter || body[idx] != null || who[idx] == null || !who[idx].Spawned) return;
                            body[idx] = RimketWorldComponent.SpawnBody(fighter.C, who[idx].Faction, at, map);
                            if (body[idx] != null) { comp.Track(body[idx]); HoldBody(idx); }
                        });
                        nextLine = Math.Max(nextLine, Find.TickManager.TicksGame + 70);      // 볼이 날아가 열릴 시간
                    }
                    break;
                case BattleEventKind.Faint:
                    if (e.Who != null && !e.Who.IsPerson) RimketWorldComponent.Cry(e.Who.C, body[i] != null && body[i].Spawned ? body[i].Position : monCell[i], map);      // 퇴장(쓰러짐) 울음소리
                    if (body[i] != null && bodyOf[i] == e.Who) { comp.Untrack(body[i], true); body[i] = null; }
                    if (bodyOf[i] == e.Who) bodyOf[i] = null;      // 아직 날아가는 볼이 있어도 나오지 않게
                    break;
                case BattleEventKind.Hit:
                    if (body[i] != null && body[i].Spawned)
                    {
                        for (int k = 0; k < 4; k++) FleckMaker.ThrowMicroSparks(body[i].DrawPos, map);
                        if (e.Value > 0) MoteMaker.ThrowText(body[i].DrawPos + new Vector3(0f, 0f, 0.5f), map, "-" + e.Value, new Color(1f, 0.45f, 0.4f), 1.2f);
                    }
                    break;
                case BattleEventKind.Heal:
                    if (body[i] != null && body[i].Spawned && e.Value > 0) MoteMaker.ThrowText(body[i].DrawPos + new Vector3(0f, 0f, 0.5f), map, "+" + e.Value, new Color(0.5f, 1f, 0.55f), 1.2f);
                    break;
                case BattleEventKind.MoveUsed:
                    if (!RimketmonMod.Settings.moveAnimations || e.Value == 1 || body[i] == null || body[1 - i] == null) break;
                    var def = MoveAnimLib.Get(e.MoveId) ?? MoveAnimLib.Fallback(e.MoveId);
                    if (def == null) break;
                    animUser = body[i]; animTarget = e.Target == e.Who ? body[i] : body[1 - i];
                    // 좌표는 그릴 때마다 화면 위치에서 다시 잡는다(카메라가 움직여도 따라가게) → 여기서는 자리만 만든다
                    anim = new MoveAnimPlayer(def, new AnimActor(), new AnimActor(), animUser == animTarget, Time.realtimeSinceStartup, 1f) { WorldMode = true };
                    animActors = new[] { GetActor(anim, true), GetActor(anim, false) };
                    break;
            }
        }

        // MoveAnimPlayer는 생성 때 받은 AnimActor 객체를 그대로 들고 있으므로, 같은 객체의 좌표만 매 프레임 고쳐 준다
        AnimActor[] animActors;
        static AnimActor GetActor(MoveAnimPlayer p, bool user)
        {
            var f = typeof(MoveAnimPlayer).GetField(user ? "user" : "target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (AnimActor)f.GetValue(p);
        }

        /// <summary>기술 연출을 지도 위(두 림켓몬의 화면 위치)에 그린다.</summary>
        public void DrawGui()
        {
            if (anim == null || Find.CurrentMap != map || RimWorld.Planet.WorldRendererUtility.WorldRendered) return;
            float now = Time.realtimeSinceStartup;
            if (anim.Done(now)) { anim = null; return; }
            if (animUser == null || animTarget == null || !animUser.Spawned || !animTarget.Spawned) { anim = null; return; }
            // 가상 캔버스 1칸 = 화면의 (지도 한 칸 / 48) → 연출 그림(64px 기준)이 동물 한두 칸 크기에 맞는다
            Vector2 a0 = animUser.DrawPos.MapToUIPosition(), a1 = (animUser.DrawPos + new Vector3(1f, 0f, 0f)).MapToUIPosition();
            float cellPx = Mathf.Abs(a1.x - a0.x); if (cellPx < 4f) return;
            float s = cellPx / 48f;
            Vector2 u = (animUser.DrawPos + new Vector3(0f, 0f, -0.4f)).MapToUIPosition(), tg = (animTarget.DrawPos + new Vector3(0f, 0f, -0.4f)).MapToUIPosition();
            animActors[0].X = u.x / s; animActors[0].Y = u.y / s; animActors[1].X = tg.x / s; animActors[1].Y = tg.y / s;
            var g = new Canvas(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight), s);
            anim.Draw(g, now);
            GUI.color = Color.white;
        }

        public void Cleanup(bool effect)
        {
            closed = true;
            for (int i = 0; i < 2; i++)
            {
                if (body[i] != null) { comp.Untrack(body[i], effect); body[i] = null; }
                var p = who[i];
                try { if (p != null && p.Spawned && !p.Dead && p.CurJob != null && (p.CurJob.def == JobDefOf.Wait || p.CurJob.def == JobDefOf.Goto)) p.jobs.EndCurrentJob(JobCondition.InterruptForced); } catch (Exception) { }
            }
            anim = null;
        }
    }

    /// <summary>따라다니는 림켓몬(그림만) 그리기: 림월드 동물은 그 동물의 실제 그래픽(개체의 색·무늬 그대로), 포켓몬은 걷는 그림.</summary>
    internal static class FollowDraw
    {
        /// <summary>이번에 그릴 그림을 화면에서 몇 도 돌릴지(발로 차서 구를 때, 던져서 뒤집혔을 때). 그린 뒤 0으로 돌려놓는다.</summary>
        public static float Spin;
        static Quaternion SpinQ { get { return Spin == 0f ? Quaternion.identity : Quaternion.AngleAxis(Spin, Vector3.up); } }
        static readonly Dictionary<string, Graphic> graphics = new Dictionary<string, Graphic>();

        static PawnKindDef AnimalKind(RimketCreature c)
        {
            if (RimketWorldComponent.DebugPokeOnly || c == null || c.PawnId != 0 || string.IsNullOrEmpty(c.SpeciesId)) return null;
            var d = DefDatabase<ThingDef>.GetNamedSilentFail(c.SpeciesId);
            if (d == null || d.race == null || d.race.Humanlike || d.defName == RimketWorldComponent.BodyDefName) return null;
            return DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(x => x.race == d);
        }

        /// <summary>지도에 꺼낼 수 있는가: 포획한 사람(초상 그림), 림월드 동물·메카노이드(그 종의 그래픽), 포켓몬(걷는 그림).</summary>
        public static bool CanShow(RimketCreature c)
        {
            if (c == null) return false;
            if (c.PawnId != 0) return PersonArt.PawnOf(c) != null;
            var kind = AnimalKind(c);
            return (kind != null && GraphicFor(c, kind) != null) || PokeBody.SheetId(c) != null;
        }

        // ---- 포획한 사람: 실제 폰을 지도에 놓지 않고, 그 사람의 초상(4방향)을 지도 위 판에 그린다
        static readonly Dictionary<int, Material> personMats = new Dictionary<int, Material>();
        const float PersonQuad = 2f;      // 초상 카메라(확대 1배)가 담는 범위 = 가로세로 2칸 → 같은 크기의 판에 그리면 실제 폰 크기

        static bool DrawPerson(Pawn p, Vector3 pos, int dir, bool moving, float step)
        {
            if (p == null) return false;
            try
            {
                var rot = dir == 0 ? Rot4.North : dir == 1 ? Rot4.East : dir == 2 ? Rot4.South : Rot4.West;
                var tex = PortraitsCache.Get(p, new Vector2(256f, 256f), rot, default(Vector3), 1f);
                if (tex == null) return false;
                int key = p.thingIDNumber * 4 + dir;
                Material m;
                if (!personMats.TryGetValue(key, out m) || m == null) { m = new Material(ShaderDatabase.Transparent); personMats[key] = m; }
                m.mainTexture = tex;      // 초상 캐시가 그림을 새로 만들 수 있으므로 매번 다시 건다
                if (moving) pos.z += Mathf.Abs(Mathf.Sin(step)) * 0.05f;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, SpinQ, new Vector3(PersonQuad, 1f, PersonQuad)), m, 0);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>이 개체의 그래픽: 잡을 때 적어 둔 그림 경로·색(Look)이 있으면 그것으로, 없으면 종 기본 그래픽.</summary>
        static Graphic GraphicFor(RimketCreature c, PawnKindDef kind)
        {
            RimketLook.EnsureLook(c);      // 무늬가 안 적힌 개체는 여기서 정해진다(키에 들어가기 전에)
            string key = kind.defName + "|" + (c.Look ?? "");
            Graphic g;
            if (graphics.TryGetValue(key, out g)) return g;
            try
            {
                GraphicData data = null;      // 다 자란 단계부터 거슬러 올라가며 그림이 있는 단계를 쓴다(메카노이드는 단계가 하나)
                for (int i = kind.lifeStages.Count - 1; i >= 0 && data == null; i--) data = kind.lifeStages[i].bodyGraphicData;
                if (data == null) { graphics[key] = null; return null; }
                g = data.Graphic;
                Color c1, c2; string path = RimketLook.Path(c.Look);
                bool hasCol = RimketLook.TryParse(c.Look, out c1);
                if (path != null || hasCol)
                {
                    if (!hasCol) c1 = g.Color;
                    if (!RimketLook.TryParseTwo(c.Look, out c2)) c2 = g.ColorTwo;
                    g = GraphicDatabase.Get<Graphic_Multi>(path ?? data.texPath, g.Shader, data.drawSize, c1, c2);
                }
            }
            catch (Exception) { g = null; }
            graphics[key] = g;
            return g;
        }

        public static void Draw(RimketCreature c, Vector3 pos, int dir, bool moving, float step)
        {
            pos.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
            if (c.PawnId != 0) { DrawPerson(PersonArt.PawnOf(c), pos, dir, moving, step); return; }
            var kind = AnimalKind(c);
            var g = kind != null ? GraphicFor(c, kind) : null;
            if (g != null)
            {
                var rot = dir == 0 ? Rot4.North : dir == 1 ? Rot4.East : dir == 2 ? Rot4.South : Rot4.West;
                if (moving) pos.z += Mathf.Abs(Mathf.Sin(step)) * 0.06f;          // 걸을 때 살짝 들썩인다(림월드 동물 그림은 걷는 동작이 없다)
                Graphics.DrawMesh(g.MeshAt(rot), pos, SpinQ, g.MatAt(rot), 0);
                return;
            }
            string sheet = PokeBody.SheetId(c); if (sheet == null) return;
            int row = dir == 2 ? 0 : dir == 3 ? 1 : dir == 1 ? 2 : 3;
            PokeBody.DrawSheet(sheet, pos, row, moving ? ((int)step) % 4 : 0, Spin);
        }
    }

    /// <summary>
    /// 포켓몬의 몸 그리기. 몸 자체(투명한 동물)는 림월드가 움직이고, 겉모습은 종족별 걷는 그림(4방향 × 4걸음, Textures/Rimket/follow)을
    /// 매 프레임 그 위치에 그린다. 그림 한 장 = 가로세로 4칸씩: 줄은 아래·왼쪽·오른쪽·위, 칸은 걸음.
    /// </summary>
    internal static class PokeBody
    {
        static readonly Dictionary<Pawn, string> bodies = new Dictionary<Pawn, string>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Dictionary<string, float> heights;
        static Mesh[] meshes;

        public static void Clear() { bodies.Clear(); }
        public static void Register(Pawn p, string sheet) { if (p != null && sheet != null) bodies[p] = sheet; }

        /// <summary>이 림켓몬의 걷는 그림 이름(없으면 null). ?티어처럼 그림이 없는 종족은 꺼낼 수 없다.</summary>
        public static string SheetId(RimketCreature c)
        {
            if (c == null || string.IsNullOrEmpty(c.Species)) return null;
            if (MysteryGen.IsMystery(c.Species))       // ?쐐기: 원래 모습으로 걷는다(순수 ?티어는 그림 없음)
            {
                if (string.IsNullOrEmpty(c.SpeciesId) || MysteryGen.IsMystery(c.SpeciesId)) return null;
                c = c.Clone(); c.Species = c.SpeciesId;
            }
            // 이로치면 이로치 걷기 그림, 폼에 따로 그림이 없으면 원래 종족 그림
            if (c.Shiny > 0 && ContentFinder<Texture2D>.Get("Rimket/follow_s/" + c.Species, false) != null) return "follow_s/" + c.Species;
            if (ContentFinder<Texture2D>.Get("Rimket/follow/" + c.Species, false) != null) return "follow/" + c.Species;
            string b = SpeciesCatalog.BaseOf(c.Species);
            if (b != c.Species && ContentFinder<Texture2D>.Get("Rimket/follow/" + b, false) != null) return "follow/" + b;
            return null;
        }

        static Material Mat(string id)
        {
            Material m;
            if (mats.TryGetValue(id, out m)) return m;
            var tex = ContentFinder<Texture2D>.Get("Rimket/" + (id.Contains("/") ? id : "follow/" + id), false);
            if (tex != null) { tex.filterMode = FilterMode.Point; m = MaterialPool.MatFrom(tex, ShaderDatabase.Cutout, Color.white); }
            mats[id] = m;
            return m;
        }

        /// <summary>지도에서의 크기(칸): 키 0.3m ≈ 1.2칸, 1m ≈ 1.45칸, 2m ≈ 1.7칸, 최대 2.8칸(그림 한 칸에는 여백이 있다).</summary>
        public static float SizeOf(string id) { return Size(id); }

        static float Size(string id)
        {
            if (heights == null)
            {
                heights = new Dictionary<string, float>();
                try
                {
                    string path = System.IO.Path.Combine(RimketmonMod.Instance.Content.RootDir, "Data/pokemon_height.txt");
                    if (System.IO.File.Exists(path))
                        foreach (var line in System.IO.File.ReadAllLines(path))
                        {
                            var t = line.Split('\t'); float h;
                            if (t.Length >= 2 && float.TryParse(t[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h)) heights[t[0]] = h;
                        }
                }
                catch (Exception) { }
            }
            float m; if (!heights.TryGetValue(id, out m)) m = 1f;
            return Mathf.Clamp(0.85f + 0.6f * Mathf.Sqrt(Mathf.Max(0.05f, m)), 1.0f, 2.8f);
        }

        static Mesh FrameMesh(int row, int col)
        {
            if (meshes == null) meshes = new Mesh[16];
            int i = row * 4 + col;
            if (meshes[i] != null) return meshes[i];
            float u0 = col * 0.25f, u1 = u0 + 0.25f, v1 = 1f - row * 0.25f, v0 = v1 - 0.25f;
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return meshes[i] = mesh;
        }

        public static void DrawSheet(string id, Vector3 pos, int row, int col, float spin = 0f)
        {
            var mat = Mat(id); if (mat == null) return;
            float size = Size(id);
            pos.z += size * 0.22f;      // 발이 칸 가운데쯤에 오도록 위로 조금 올린다
            var q = spin == 0f ? Quaternion.identity : Quaternion.AngleAxis(spin, Vector3.up);
            Graphics.DrawMesh(FrameMesh(row, col), Matrix4x4.TRS(pos, q, new Vector3(size, 1f, size)), mat, 0);
        }

        static readonly List<Pawn> dead = new List<Pawn>();
        public static void DrawAll()
        {
            if (bodies.Count == 0) return;
            var map = Find.CurrentMap; if (map == null || RimWorld.Planet.WorldRendererUtility.WorldRendered) return;
            int tick = Find.TickManager.TicksGame;
            dead.Clear();
            foreach (var kv in bodies)
            {
                var p = kv.Key;
                if (p == null || p.Destroyed) { dead.Add(p); continue; }
                if (!p.Spawned || p.Map != map || p.Position.Fogged(map)) continue;
                var mat = Mat(kv.Value); if (mat == null) continue;
                int row = p.Rotation == Rot4.South ? 0 : p.Rotation == Rot4.West ? 1 : p.Rotation == Rot4.East ? 2 : 3;
                bool moving = p.pather != null && p.pather.MovingNow;
                int col = moving ? (tick / 8) % 4 : 0;
                Vector3 pos = p.DrawPos; pos.y = AltitudeLayer.Pawn.AltitudeFor();
                DrawSheet(kv.Value, pos, row, col);
            }
            foreach (var p in dead) if (p != null) bodies.Remove(p);
        }
    }

    // ---- '림켓몬 꺼내기' 능력: 가리킨 자리에 볼을 던져 파티 맨 앞 림켓몬을 꺼낸다. 이미 나와 있으면 불러들인다.
    public class CompProperties_AbilityRimketSummon : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityRimketSummon() { compClass = typeof(CompAbilityEffect_RimketSummon); }
    }

    public class CompAbilityEffect_RimketSummon : CompAbilityEffect
    {
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            var caster = parent.pawn; var comp = RimketWorldComponent.Instance;
            if (caster == null || comp == null || caster.Map == null) return;
            if (comp.AnyFollower) { comp.Recall(); return; }
            Projectile_RimketBall.ThrowSummon(caster, target.Cell);
        }
    }
}
