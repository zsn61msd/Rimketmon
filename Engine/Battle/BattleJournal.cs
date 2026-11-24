using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rimketmon.Engine
{
    /// <summary>
    /// 배틀 진행 기록(세이브 중 배틀 이어 하기). 배틀의 무작위 값 전부(JournalRng)와, 배틀을 바꾸는 입력 전부를 순서대로 남긴다:
    /// A 행동(Act) · D 더블 행동 · I 아이템 · B 볼 던지기(사냥) · W 승리 선택 · L 배틀 중 기술 배우기(화면) · 상대 두뇌 결정(Foe).
    /// 같은 시작 상태로 배틀을 다시 만들고 Replay하면 저장하던 순간과 똑같은 상태가 된다(무작위 값도 기록된 것을 그대로 쓴다).
    /// </summary>
    public sealed class BattleJournal
    {
        public List<string> Entries = new List<string>();
        public List<string> Foe = new List<string>();
        public List<double> Draws = new List<double>();
        public bool Replaying;
        int foePos;

        internal BattleAction? NextFoe() { return foePos < Foe.Count ? Dec(Foe[foePos++]) : (BattleAction?)null; }

        public static string Enc(BattleAction a)
        {
            return (int)a.Kind + "," + a.Index + "," + a.Target + "," + (a.ItemId ?? "");
        }
        public static BattleAction? Dec(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "-") return null;
            var p = s.Split(new[] { ',' }, 4);
            var a = new BattleAction((ActionKind)int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]));
            if (p.Length > 3 && p[3].Length > 0) a.ItemId = p[3];
            return a;
        }

        /// <summary>저장용 문자열 묶음(무작위 값은 정밀도를 잃지 않게 "R" 형식).</summary>
        public string DrawsText() { return string.Join(";", Draws.Select(d => d.ToString("R", CultureInfo.InvariantCulture))); }
        public void SetDrawsText(string t)
        {
            Draws = string.IsNullOrEmpty(t) ? new List<double>() : t.Split(';').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();
        }

        public BattleJournal Copy() { return new BattleJournal { Entries = Entries.ToList(), Foe = Foe.ToList(), Draws = Draws.ToList() }; }

        /// <summary>
        /// 기록 재생: b는 같은 시작 상태로 새로 만든 배틀(무작위는 JournalRng(Draws)로 만들어야 한다).
        /// learn은 L 기록(파티 번호, 슬롯, 기술 id)을 실제 림켓몬에 적용하는 함수(엔진만 쓸 땐 기본 처리).
        /// 하나라도 다시 되지 않으면 false(저장 데이터와 엔진이 어긋남).
        /// </summary>
        public static bool Replay(RimketBattle b, BattleJournal saved, Action<RimketBattle, int, int, string> learn = null)
        {
            // 같은 기록 객체를 그대로 쓴다(배틀의 JournalRng도 이 객체의 Draws를 읽고, 이어서 쌓는다)
            var j = saved; var entries = j.Entries.ToList(); j.Replaying = true; j.foePos = 0;
            b.Journal = j;
            bool ok = true;
            try
            {
                foreach (var e in entries)
                {
                    var p = e.Split('|');
                    switch (p[0])
                    {
                        case "A": ok &= b.Act(Dec(p[1]).Value); break;
                        case "D": ok &= b.ActDouble(Dec(p[1]).Value, Dec(p[2])); break;
                        case "I": ok &= b.UseItem(p[2], int.Parse(p[1])); break;
                        case "B": ok &= b.ThrowBall(int.Parse(p[1])); break;
                        case "W": ok &= b.ChooseWinOption((WinChoice)int.Parse(p[1])); break;
                        case "O": ok &= b.OnlineAct(Dec(p[1]), Dec(p[2]), Dec(p[3]), Dec(p[4])); break;
                        case "E": b.ApplyEnvironment((Weather)int.Parse(p[1]), (Terrain)int.Parse(p[2])); break;
                        case "L":
                            {
                                int pi = int.Parse(p[1]), slot = int.Parse(p[2]);
                                if (learn != null) learn(b, pi, slot, p[3]); else b.ApplyLearn(pi, slot, p[3]);
                            }
                            break;
                    }
                    if (!ok) break;
                }
            }
            finally { j.Replaying = false; }
            return ok;
        }
    }

    /// <summary>배틀 무작위: 기록된 값이 있으면 그것을 순서대로 돌려주고, 다 쓰면 inner로 새로 뽑아 기록에 더한다.</summary>
    public sealed class JournalRng : IRng
    {
        readonly IRng inner; readonly BattleJournal j; int pos; readonly bool sync;
        /// <param name="syncInner">기록된 값을 돌려줄 때도 inner를 같이 굴린다(시드 난수를 이어서 같은 흐름으로 쓰려는 경우: 미리 계산 AI)</param>
        public JournalRng(IRng inner, BattleJournal journal, bool syncInner = false) { this.inner = inner; j = journal; sync = syncInner; }
        public BattleJournal Journal { get { return j; } }
        public double NextDouble()
        {
            if (pos < j.Draws.Count) { if (sync) inner.NextDouble(); return j.Draws[pos++]; }
            double v = inner.NextDouble(); j.Draws.Add(v); pos++; return v;
        }
        public int Next(int minInclusive, int maxExclusive)
        {
            if (pos < j.Draws.Count) { if (sync) inner.Next(minInclusive, maxExclusive); return (int)j.Draws[pos++]; }
            int v = inner.Next(minInclusive, maxExclusive); j.Draws.Add(v); pos++; return v;
        }
    }

    public sealed partial class RimketBattle
    {
        /// <summary>진행 기록(없으면 기록하지 않는다).</summary>
        public BattleJournal Journal;
        /// <summary>시작 상태(JournalRng로 만들었을 때만): 양쪽 파티·트레이너 본인·정착민 전투원·엔진 설정을 FlatCodec 문자열로.</summary>
        public List<string> StartPlayer, StartFoe; public string StartFoePerson, StartColonist, StartSettings;
        internal void Note(string e) { if (Journal != null && !Journal.Replaying) Journal.Entries.Add(e); }

        /// <summary>배틀 중 기술 배우기(화면에서 고른 결과)를 기록하고 적용한다. partyIndex = Player.Party 번호, slot = 바꿀 칸(-1이면 빈칸에 추가).</summary>
        public void NoteLearn(int partyIndex, int slot, string moveId) { Note("L|" + partyIndex + "|" + slot + "|" + moveId); }

        /// <summary>L 기록 기본 적용(엔진 테스트용): 그 림켓몬의 기술 칸을 바꾼다.</summary>
        public void ApplyLearn(int partyIndex, int slot, string moveId)
        {
            if (partyIndex < 0 || partyIndex >= Player.Party.Count) return;
            var c = Player.Party[partyIndex];
            Move m; try { m = MoveCatalog.Get(moveId); } catch (Exception) { return; }
            if (slot < 0) MoveLearn.TryAdd(c, m); else MoveLearn.Replace(c, slot, m);
        }
    }
}
