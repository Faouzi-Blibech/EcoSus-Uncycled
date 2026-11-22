using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Bots.Meeting
{
    /// <summary>
    /// The public chat of one meeting as structured acts. Fed from the engine's ChatEvents (the same
    /// events every client sees), so it holds nothing a player could not read. Cleared per meeting.
    /// </summary>
    public sealed class MeetingLog
    {
        private const string AccuseKind = "accuse";

        private readonly PhraseCatalogue _catalogue;
        private readonly ChatWords? _words;
        private readonly List<ChatAct> _acts = new List<ChatAct>();

        public MeetingLog(PhraseCatalogue catalogue, ChatWords? words = null)
        {
            _catalogue = catalogue;
            _words = words;
        }

        public IReadOnlyList<ChatAct> Acts => _acts;

        /// <summary>
        /// The display names of the seats a typed line may name (the living seats, the speaker included), read when a line
        /// arrives. Null: typed lines are not read.
        /// </summary>
        public Func<IReadOnlyDictionary<byte, string>>? LivingNames { get; set; }

        /// <summary>Adds a chat line; a phrase id missing from the catalogue is ignored. Ghost lines (out seats talking among themselves)
        /// never enter. A typed line is read by TextReader into the acts of the phrases it says (nothing when the log has no words or
        /// no names, or the line is not clear).</summary>
        public void Add(ChatEvent e, float matchTime)
        {
            if (e.Ghost)
            {
                return;
            }

            if (e.PhraseId == null)
            {
                if (e.Text != null && _words != null && LivingNames != null)
                {
                    foreach (ChatAct act in TextReader.Read(e.Text, e.Seat, LivingNames(), _words, matchTime))
                    {
                        _acts.Add(act);
                    }
                }

                return;
            }

            if (!_catalogue.TryGet(e.PhraseId, out PhraseInfo info))
            {
                return;
            }

            byte? seat = null;
            string? zone = null;
            foreach (ChatArg arg in e.Args)
            {
                if (arg.Kind == ChatArgKind.Seat && seat == null)
                {
                    seat = arg.Seat;
                }
                else if (arg.Kind == ChatArgKind.Zone && zone == null)
                {
                    zone = arg.Zone;
                }
            }

            _acts.Add(new ChatAct(e.Seat, info.Id, info.Kind, seat, zone, matchTime));
        }

        /// <summary>
        /// How many different seats accused this seat (accuse-kind phrases naming it). Distinct
        /// speakers, so repeating a line does not raise it and the count stays below the player count.
        /// </summary>
        public int AccusationsAgainst(byte seat)
        {
            var accusers = new HashSet<byte>();
            foreach (ChatAct a in _acts)
            {
                if (a.Kind == AccuseKind && a.Seat == seat)
                {
                    accusers.Add(a.Speaker);
                }
            }

            return accusers.Count;
        }

        /// <summary>Adds an accusation that was not a chat line (a scripted bot's vote): an accuse act naming the target, from the first accuse-kind phrase of the catalogue.</summary>
        public void AddAccusation(byte speaker, byte target, float matchTime)
        {
            string id = AccuseKind;
            foreach (PhraseInfo info in _catalogue.All)
            {
                if (info.Kind == AccuseKind)
                {
                    id = info.Id;
                    break;
                }
            }

            _acts.Add(new ChatAct(speaker, id, AccuseKind, target, null, matchTime));
        }

        /// <summary>Each (accuser, target) pair once, in the order first said.</summary>
        public IReadOnlyList<(byte Accuser, byte Target)> DistinctAccusations()
        {
            var pairs = new List<(byte, byte)>();
            foreach (ChatAct a in _acts)
            {
                if (a.Kind == AccuseKind && a.Seat != null && !pairs.Contains((a.Speaker, a.Seat.Value)))
                {
                    pairs.Add((a.Speaker, a.Seat.Value));
                }
            }

            return pairs;
        }

        public bool WasAccusedBy(byte target, byte by)
        {
            foreach (ChatAct a in _acts)
            {
                if (a.Kind == AccuseKind && a.Seat == target && a.Speaker == by)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Counts the clears: a reader that remembers how far it has read starts again from the first act when this changed.</summary>
        public int Generation { get; private set; }

        public void Clear()
        {
            _acts.Clear();
            Generation++;
        }
    }
}
