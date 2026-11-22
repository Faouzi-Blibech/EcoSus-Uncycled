namespace Uncycled.Bots.Meeting
{
    /// <summary>What a decider chose: a phrase to say (Speak) and/or a vote; checked by Grounding before use.</summary>
    public sealed class MeetingDecision
    {
        /// <summary>True when the bot wants to say PhraseId now.</summary>
        public bool Speak { get; set; }

        /// <summary>Catalogue id of the phrase.</summary>
        public string? PhraseId { get; set; }

        /// <summary>The phrase's seat slot, if it has one.</summary>
        public byte? Seat { get; set; }

        /// <summary>The phrase's zone slot, if it has one.</summary>
        public string? Zone { get; set; }

        /// <summary>The seat whose accusation this line stands in for (a repeat turned into agree or where): the bot still votes for it.</summary>
        public byte? Announces { get; set; }

        /// <summary>The seat to vote for (voting stage).</summary>
        public byte? Vote { get; set; }

        /// <summary>True to skip the vote.</summary>
        public bool SkipVote { get; set; }

        /// <summary>Who produced it: "rule", "clef", ... (for the decision log).</summary>
        public string Source { get; set; } = "";

        /// <summary>Why the answer is not the primary decider's own (timeout, http_429, late, ...); null when it is.</summary>
        public string? Reason { get; set; }

        /// <summary>Milliseconds the primary decider took, when it ran.</summary>
        public long? LatencyMs { get; set; }

        public static MeetingDecision Quiet(string source) => new MeetingDecision { Source = source };

        public static MeetingDecision Say(string source, string phraseId, byte? seat = null, string? zone = null)
        {
            return new MeetingDecision { Speak = true, PhraseId = phraseId, Seat = seat, Zone = zone, Source = source };
        }
    }
}
