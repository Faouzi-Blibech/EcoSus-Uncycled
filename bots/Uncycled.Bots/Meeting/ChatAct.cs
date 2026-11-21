namespace Uncycled.Bots.Meeting
{
    /// <summary>One chat line as a structured act: who said which catalogue phrase about whom and where.</summary>
    public sealed class ChatAct
    {
        public ChatAct(byte speaker, string phraseId, string kind, byte? seat, string? zone, float time)
        {
            Speaker = speaker;
            PhraseId = phraseId;
            Kind = kind;
            Seat = seat;
            Zone = zone;
            Time = time;
        }

        public byte Speaker { get; }

        public string PhraseId { get; }

        /// <summary>accuse, alibi, question, react, vote or filler (the catalogue kind).</summary>
        public string Kind { get; }

        /// <summary>First seat slot of the phrase, if any.</summary>
        public byte? Seat { get; }

        /// <summary>First zone slot of the phrase, if any.</summary>
        public string? Zone { get; }

        public float Time { get; }
    }
}
