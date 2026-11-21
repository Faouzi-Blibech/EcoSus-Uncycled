using System;
using System.Collections.Generic;
using System.Text.Json;
using Sim.Core;

namespace Sim.Match
{
    /// <summary>One chat phrase: id, kind tag for the bot brain, and its ordered slot types.</summary>
    public sealed class PhraseInfo
    {
        public PhraseInfo(string id, string kind, IReadOnlyList<ChatArgKind> slots)
        {
            Id = id;
            Kind = kind;
            Slots = slots;
        }

        public string Id { get; }

        public string Kind { get; }

        public IReadOnlyList<ChatArgKind> Slots { get; }
    }

    /// <summary>The shared phrase catalogue (content/phrases.json).</summary>
    public sealed class PhraseCatalogue
    {
        private readonly List<PhraseInfo> _all;
        private readonly Dictionary<string, PhraseInfo> _byId;

        private PhraseCatalogue(List<PhraseInfo> all)
        {
            _all = all;
            _byId = new Dictionary<string, PhraseInfo>(StringComparer.Ordinal);
            foreach (PhraseInfo p in all)
            {
                _byId[p.Id] = p;
            }
        }

        public IReadOnlyList<PhraseInfo> All => _all;

        /// <summary>Reads {"version":1,"phrases":[{"id","kind","slots":["seat"|"zone",...]}]}.</summary>
        public static PhraseCatalogue Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            var all = new List<PhraseInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement e in doc.RootElement.GetProperty("phrases").EnumerateArray())
            {
                string id = e.GetProperty("id").GetString() ?? "";
                if (id.Length == 0 || !seen.Add(id))
                {
                    throw new FormatException("Phrase id missing or duplicated: '" + id + "'");
                }

                string kind = e.GetProperty("kind").GetString() ?? "";
                var slots = new List<ChatArgKind>();
                foreach (JsonElement s in e.GetProperty("slots").EnumerateArray())
                {
                    switch (s.GetString())
                    {
                        case "seat": slots.Add(ChatArgKind.Seat); break;
                        case "zone": slots.Add(ChatArgKind.Zone); break;
                        default: throw new FormatException("Phrase " + id + " has an unknown slot type: " + s);
                    }
                }

                all.Add(new PhraseInfo(id, kind, slots));
            }

            return new PhraseCatalogue(all);
        }

        public bool TryGet(string id, out PhraseInfo info)
        {
            return _byId.TryGetValue(id ?? "", out info!);
        }
    }
}
