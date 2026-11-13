using System.Collections.Generic;

namespace Rimketmon.Engine
{
    /// <summary>랭크배틀에 나올 일반 트레이너. 지금은 스프라이트(이미지 id)와 이름만 있고, 파티 구성(Party)은 나중에 조사해 채운다.</summary>
    public sealed class RankedTrainer
    {
        public string Id;            // 스프라이트 id(소문자 = Textures/Rimket/trainer/<id>__0001...)
        public string Name;          // 표시 이름(직업 또는 고유 인물)
        public bool Male;
        public bool Named;           // true = 고유 인물(사천왕/악당 간부 등), false = 직업 일반 트레이너
        /// <summary>파티 구성은 아직 조사 전이라 비어 있다. 채워지기 전까지 랭크배틀에 쓰지 않는다.</summary>
        public List<string[]> Slots = new List<string[]>();
        public bool HasParty { get { return Slots.Count > 0; } }
        public string SpriteId { get { return Id.ToLowerInvariant(); } }
    }

    public static class RankedTrainerCatalog
    {
        private static readonly List<RankedTrainer> all = new List<RankedTrainer>();
        private static readonly Dictionary<string, RankedTrainer> byId = new Dictionary<string, RankedTrainer>();

        static RankedTrainerCatalog()
        {
            foreach (var row in RankedTrainerData.Rows)
            {
                var f = row.Split('|');
                var t = new RankedTrainer { Id = f[0], Name = f[1], Male = f[2] == "true", Named = f[3] == "named" };
                all.Add(t); byId[t.Id] = t;
            }
        }

        public static IList<RankedTrainer> All { get { return all; } }
        public static RankedTrainer Get(string id) { RankedTrainer t; return byId.TryGetValue(id.ToUpperInvariant(), out t) ? t : null; }
    }
}
