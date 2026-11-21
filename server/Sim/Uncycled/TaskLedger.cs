using System;
using System.Collections.Generic;
using Sim.Core;

namespace Sim.Uncycled
{
    /// <summary>A task type from content/tasks.json. Step i is done at ConsoleIds[i % count].</summary>
    public sealed class TaskTypeDef
    {
        public TaskTypeDef(string id, TaskCategory category, int steps, float minSeconds, IReadOnlyList<string> consoleIds)
        {
            if (steps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(steps));
            }

            if (consoleIds == null || consoleIds.Count == 0)
            {
                throw new ArgumentException("A task type needs at least one console.", nameof(consoleIds));
            }

            Id = id ?? throw new ArgumentNullException(nameof(id));
            Category = category;
            Steps = steps;
            MinSeconds = minSeconds;
            ConsoleIds = new List<string>(consoleIds);
        }

        public string Id { get; }

        public TaskCategory Category { get; }

        public int Steps { get; }

        /// <summary>Minimum seconds between use_ok and task_step.</summary>
        public float MinSeconds { get; }

        public IReadOnlyList<string> ConsoleIds { get; }

        public string ConsoleForStep(int step) => ConsoleIds[step % ConsoleIds.Count];
    }

    /// <summary>A task owned by one citizen.</summary>
    public sealed class TaskState
    {
        internal TaskState(int id, TaskTypeDef type, byte owner)
        {
            Id = id;
            Type = type;
            Owner = owner;
        }

        public int Id { get; }

        public TaskTypeDef Type { get; }

        public byte Owner { get; internal set; }

        public int StepsDone { get; internal set; }

        public int Steps => Type.Steps;

        public bool IsDone => StepsDone >= Type.Steps;

        /// <summary>Console of the next step (of the last step when done).</summary>
        public string CurrentConsoleId => Type.ConsoleForStep(IsDone ? Type.Steps - 1 : StepsDone);
    }

    /// <summary>
    /// Step-based task accounting. The total is fixed at match start and never shrinks;
    /// unfinished tasks of a citizen who goes Out move to an active citizen.
    /// </summary>
    public sealed class TaskLedger
    {
        private readonly List<TaskState> _tasks;

        private TaskLedger(List<TaskState> tasks)
        {
            _tasks = tasks;
            int total = 0;
            foreach (TaskState t in tasks)
            {
                total += t.Steps;
            }

            TotalSteps = total;
        }

        public IReadOnlyList<TaskState> All => _tasks;

        public int TotalSteps { get; }

        public int DoneSteps
        {
            get
            {
                int done = 0;
                foreach (TaskState t in _tasks)
                {
                    done += Math.Min(t.StepsDone, t.Steps);
                }

                return done;
            }
        }

        /// <summary>0..1 task bar value. 0 when there are no tasks at all.</summary>
        public float Progress => TotalSteps == 0 ? 0f : (float)DoneSteps / TotalSteps;

        /// <summary>True when there is at least one step and every step is done.</summary>
        public bool AllDone => TotalSteps > 0 && DoneSteps >= TotalSteps;

        public TaskState? Find(int id)
        {
            foreach (TaskState t in _tasks)
            {
                if (t.Id == id)
                {
                    return t;
                }
            }

            return null;
        }

        public List<TaskState> OwnedBy(byte seat)
        {
            var list = new List<TaskState>();
            foreach (TaskState t in _tasks)
            {
                if (t.Owner == seat)
                {
                    list.Add(t);
                }
            }

            return list;
        }

        public int RemainingFor(byte seat)
        {
            int n = 0;
            foreach (TaskState t in _tasks)
            {
                if (t.Owner == seat && !t.IsDone)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Marks one more step done. Returns false when the task was already done.</summary>
        internal bool CompleteStep(TaskState task)
        {
            if (task.IsDone)
            {
                return false;
            }

            task.StepsDone++;
            return true;
        }

        /// <summary>
        /// Moves each unfinished task of outSeat (in id order) to the active citizen with the fewest
        /// unfinished tasks (ties: lowest seat id). Returns the moved tasks. With no active citizen
        /// nothing moves.
        /// </summary>
        internal List<TaskState> Reassign(byte outSeat, IReadOnlyList<byte> activeCitizens)
        {
            var moved = new List<TaskState>();
            if (activeCitizens == null || activeCitizens.Count == 0)
            {
                return moved;
            }

            foreach (TaskState t in _tasks)
            {
                if (t.Owner != outSeat || t.IsDone)
                {
                    continue;
                }

                byte best = 0;
                int bestCount = int.MaxValue;
                foreach (byte seat in activeCitizens)
                {
                    if (seat == outSeat)
                    {
                        continue;
                    }

                    int remaining = RemainingFor(seat);
                    if (remaining < bestCount || (remaining == bestCount && seat < best))
                    {
                        best = seat;
                        bestCount = remaining;
                    }
                }

                if (bestCount == int.MaxValue)
                {
                    break;
                }

                t.Owner = best;
                moved.Add(t);
            }

            return moved;
        }

        /// <summary>
        /// Assigns tasks to citizens. Common tasks are drawn once and given to every citizen;
        /// short and long tasks are drawn per citizen without repeats. A pool smaller than the
        /// requested count gives every type it has.
        /// </summary>
        public static TaskLedger Assign(
            IReadOnlyList<byte> citizens,
            IReadOnlyList<TaskTypeDef> types,
            int commonCount,
            int shortCount,
            int longCount,
            IRandom rng)
        {
            TaskTypeDef[] common = Pool(types, TaskCategory.Common);
            rng.Shuffle(common);
            int commonTake = Math.Min(Math.Max(commonCount, 0), common.Length);

            var tasks = new List<TaskState>();
            int nextId = 1;
            foreach (byte seat in citizens)
            {
                for (int i = 0; i < commonTake; i++)
                {
                    tasks.Add(new TaskState(nextId++, common[i], seat));
                }

                foreach (TaskTypeDef def in Draw(types, TaskCategory.Short, shortCount, rng))
                {
                    tasks.Add(new TaskState(nextId++, def, seat));
                }

                foreach (TaskTypeDef def in Draw(types, TaskCategory.Long, longCount, rng))
                {
                    tasks.Add(new TaskState(nextId++, def, seat));
                }
            }

            return new TaskLedger(tasks);
        }

        private static TaskTypeDef[] Pool(IReadOnlyList<TaskTypeDef> types, TaskCategory category)
        {
            var list = new List<TaskTypeDef>();
            foreach (TaskTypeDef t in types)
            {
                if (t.Category == category)
                {
                    list.Add(t);
                }
            }

            return list.ToArray();
        }

        private static List<TaskTypeDef> Draw(IReadOnlyList<TaskTypeDef> types, TaskCategory category, int count, IRandom rng)
        {
            TaskTypeDef[] pool = Pool(types, category);
            rng.Shuffle(pool);
            int take = Math.Min(Math.Max(count, 0), pool.Length);
            var result = new List<TaskTypeDef>(take);
            for (int i = 0; i < take; i++)
            {
                result.Add(pool[i]);
            }

            return result;
        }
    }
}
