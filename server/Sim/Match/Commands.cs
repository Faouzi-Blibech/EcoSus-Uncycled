using System;
using System.Collections.Generic;
using Sim.Core;

namespace Sim.Match
{
    /// <summary>A request from a seat. Bots and humans send the same commands.</summary>
    public interface ICommand
    {
    }

    /// <summary>protocol: move {pos, seq}.</summary>
    public sealed class MoveCommand : ICommand
    {
        public MoveCommand(Vec2 position, long seq)
        {
            Position = position;
            Seq = seq;
        }

        public Vec2 Position { get; }

        public long Seq { get; }
    }

    /// <summary>protocol: use {console}.</summary>
    public sealed class UseCommand : ICommand
    {
        public UseCommand(string consoleId)
        {
            ConsoleId = consoleId ?? "";
        }

        public string ConsoleId { get; }
    }

    /// <summary>protocol: spot_done {spot}. Ends the clean session opened by use.</summary>
    public sealed class SpotDoneCommand : ICommand
    {
        public SpotDoneCommand(string spotId)
        {
            SpotId = spotId ?? "";
        }

        public string SpotId { get; }
    }

    /// <summary>protocol: pick_upgrade {item}. Answers an open upgrade offer.</summary>
    public sealed class PickUpgradeCommand : ICommand
    {
        public PickUpgradeCommand(UpgradeKind item)
        {
            Item = item;
        }

        public UpgradeKind Item { get; }
    }

    /// <summary>protocol: task_step {task}.</summary>
    public sealed class TaskStepCommand : ICommand
    {
        public TaskStepCommand(int taskId)
        {
            TaskId = taskId;
        }

        public int TaskId { get; }
    }

    /// <summary>protocol: hit {target}.</summary>
    public sealed class HitCommand : ICommand
    {
        public HitCommand(byte target)
        {
            Target = target;
        }

        public byte Target { get; }
    }

    /// <summary>protocol: dump.</summary>
    public sealed class DumpCommand : ICommand
    {
    }

    /// <summary>protocol: report {pile}.</summary>
    public sealed class ReportCommand : ICommand
    {
        public ReportCommand(int pileId)
        {
            PileId = pileId;
        }

        public int PileId { get; }
    }

    /// <summary>protocol: call_meeting.</summary>
    public sealed class CallMeetingCommand : ICommand
    {
    }

    /// <summary>protocol: vote {target} or {skip: true}. Target null = skip.</summary>
    public sealed class VoteCommand : ICommand
    {
        public VoteCommand(byte? target)
        {
            Target = target;
        }

        public byte? Target { get; }

        public bool IsSkip => Target == null;
    }

    /// <summary>protocol: repollute {spot}. A Trasher turns a clean spot dirty again.</summary>
    public sealed class RepolluteCommand : ICommand
    {
        public RepolluteCommand(string spotId)
        {
            SpotId = spotId ?? "";
        }

        public string SpotId { get; }
    }

    /// <summary>protocol: pick_ability {ability}. Once per match, after ability_unlocked.</summary>
    public sealed class PickAbilityCommand : ICommand
    {
        public PickAbilityCommand(TrasherAbility ability)
        {
            Ability = ability;
        }

        public TrasherAbility Ability { get; }
    }

    /// <summary>protocol: sabotage {kind}.</summary>
    public sealed class SabotageCommand : ICommand
    {
        public SabotageCommand(SabotageKind kind)
        {
            Kind = kind;
        }

        public SabotageKind Kind { get; }
    }

    /// <summary>protocol: fix {console, holding}. Holding is ignored at the smog panel.</summary>
    public sealed class FixCommand : ICommand
    {
        public FixCommand(string consoleId, bool holding)
        {
            ConsoleId = consoleId ?? "";
            Holding = holding;
        }

        public string ConsoleId { get; }

        public bool Holding { get; }
    }

    /// <summary>protocol: clean_start {pile}.</summary>
    public sealed class CleanStartCommand : ICommand
    {
        public CleanStartCommand(int pileId)
        {
            PileId = pileId;
        }

        public int PileId { get; }
    }

    /// <summary>protocol: clean_cancel {pile}.</summary>
    public sealed class CleanCancelCommand : ICommand
    {
        public CleanCancelCommand(int pileId)
        {
            PileId = pileId;
        }

        public int PileId { get; }
    }

    /// <summary>One chat phrase argument: a seat id or a zone id.</summary>
    public readonly struct ChatArg
    {
        private ChatArg(ChatArgKind kind, byte seat, string zone)
        {
            Kind = kind;
            Seat = seat;
            Zone = zone;
        }

        public ChatArgKind Kind { get; }

        public byte Seat { get; }

        public string Zone { get; }

        public static ChatArg ForSeat(byte seat) => new ChatArg(ChatArgKind.Seat, seat, "");

        public static ChatArg ForZone(string zoneId) => new ChatArg(ChatArgKind.Zone, 0, zoneId ?? "");
    }

    /// <summary>protocol: chat_phrase {id, args}.</summary>
    public sealed class ChatPhraseCommand : ICommand
    {
        public ChatPhraseCommand(string phraseId, IReadOnlyList<ChatArg>? args)
        {
            PhraseId = phraseId ?? "";
            Args = args ?? Array.Empty<ChatArg>();
        }

        public string PhraseId { get; }

        public IReadOnlyList<ChatArg> Args { get; }
    }

    /// <summary>protocol: chat_text {text}. A typed chat line; the engine normalises and filters it.</summary>
    public sealed class ChatTextCommand : ICommand
    {
        public ChatTextCommand(string text)
        {
            Text = text ?? "";
        }

        public string Text { get; }
    }

    /// <summary>protocol: gas {room}. A Trasher that picked the gas; the room is a map gas_rooms id.</summary>
    public sealed class GasCommand : ICommand
    {
        public GasCommand(string roomId)
        {
            RoomId = roomId;
        }

        public string RoomId { get; }
    }

    /// <summary>protocol: plant_virus {spot}. A Trasher with the virus plants it on a dirty spot.</summary>
    public sealed class PlantVirusCommand : ICommand
    {
        public PlantVirusCommand(string spotId)
        {
            SpotId = spotId ?? "";
        }

        public string SpotId { get; }
    }

    /// <summary>protocol: heal {target}. A Cleaner with a heal cures an infected seat (itself or one in reach).</summary>
    public sealed class HealCommand : ICommand
    {
        public HealCommand(byte target)
        {
            Target = target;
        }

        public byte Target { get; }
    }

    /// <summary>Ok, or rejected with a protocol reason.</summary>
    public readonly struct CommandResult
    {
        private CommandResult(bool ok, RejectReason reason)
        {
            IsOk = ok;
            Reason = reason;
        }

        public bool IsOk { get; }

        /// <summary>Meaningful only when IsOk is false.</summary>
        public RejectReason Reason { get; }

        public static CommandResult Ok => new CommandResult(true, RejectReason.NotAllowed);

        public static CommandResult Rejected(RejectReason reason) => new CommandResult(false, reason);

        public override string ToString() => IsOk ? "Ok" : "Rejected(" + Reason + ")";
    }
}
