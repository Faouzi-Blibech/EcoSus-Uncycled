"""Fake training host that speaks the Uncycled pipe protocol (plan Task 4) with random data.

Handshake: b"UCT1", int32 obsSize, int32 actionCount, int32 agentCount, 64 bytes ASCII schema hash.
Then repeatedly: for each agent float32[obsSize] obs, uint8[actionCount] mask, float32 reward, uint8 done;
the trainer replies int32[agentCount] actions. A finished match is replaced by a new one: its agents get
done=1 and the observation of the new match in the same frame.

The mask always allows action 0 and a random subset of the others. An agent earns +1 when its action equals
the hidden good action. A masked-out action prints "invalid action" to stderr and is replaced by action 0.

Only numpy and the standard library are used, so it can run under any interpreter that has numpy.
"""
import argparse
import hashlib
import struct
import sys

import numpy as np

AGENTS_PER_MATCH = {"citizen": 6, "polluter": 2}


def read_exact(stream, n):
    buf = bytearray()
    while len(buf) < n:
        chunk = stream.read(n - len(buf))
        if not chunk:
            return None
        buf += chunk
    return bytes(buf)


def parse_args(argv):
    p = argparse.ArgumentParser(prog="fake_host")
    p.add_argument("--content", default="")
    p.add_argument("--matches", type=int, default=16)
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--learner", choices=sorted(AGENTS_PER_MATCH), default="citizen")
    p.add_argument("--threads", type=int, default=1)
    p.add_argument("--obs-size", type=int, default=64)
    p.add_argument("--actions", type=int, default=30)
    p.add_argument("--good-action", type=int, default=5)
    p.add_argument("--mask-p", type=float, default=0.6, help="probability that a non-zero action is allowed")
    p.add_argument("--min-len", type=int, default=20)
    p.add_argument("--max-len", type=int, default=60)
    args, unknown = p.parse_known_args(argv)
    bad = [u for u in unknown if not u.startswith("--rw-") and not _is_rw_value(u)]
    if bad:
        p.error("unknown option(s): " + " ".join(bad))
    return args


def _is_rw_value(token):
    try:
        float(token)
        return True
    except ValueError:
        return False


def main(argv=None):
    args = parse_args(sys.argv[1:] if argv is None else argv)
    rng = np.random.default_rng(args.seed)
    per_match = AGENTS_PER_MATCH[args.learner]
    agents = args.matches * per_match
    obs_size, n_actions = args.obs_size, args.actions

    schema_hash = hashlib.sha256(f"fake-{obs_size}-{n_actions}".encode()).hexdigest().encode("ascii")
    assert len(schema_hash) == 64

    out = sys.stdout.buffer
    inp = sys.stdin.buffer

    frame_dtype = np.dtype([("obs", "<f4", (obs_size,)), ("mask", "u1", (n_actions,)),
                            ("reward", "<f4"), ("done", "u1")])

    out.write(b"UCT1" + struct.pack("<iii", obs_size, n_actions, agents) + schema_hash)

    def new_lengths():
        return rng.integers(args.min_len, args.max_len + 1, size=args.matches)

    lengths = new_lengths()
    steps = np.zeros(args.matches, dtype=np.int64)
    frame = np.zeros(agents, dtype=frame_dtype)

    def fill(agent_slice_obs_for):
        for a in agent_slice_obs_for:
            frame["obs"][a] = rng.uniform(-1.0, 1.0, obs_size)
            m = rng.random(n_actions) < args.mask_p
            m[0] = True
            frame["mask"][a] = m

    fill(range(agents))
    frame["reward"] = 0.0
    frame["done"] = 0

    while True:
        out.write(frame.tobytes())
        out.flush()
        raw = read_exact(inp, 4 * agents)
        if raw is None:
            return 0
        actions = np.frombuffer(raw, dtype="<i4")

        rewards = np.zeros(agents, dtype=np.float32)
        for a in range(agents):
            act = int(actions[a])
            if act < 0 or act >= n_actions or not frame["mask"][a][act]:
                sys.stderr.write(f"invalid action {act} for agent {a}\n")
                sys.stderr.flush()
                act = 0
            if act == args.good_action:
                rewards[a] = 1.0

        steps += 1
        done = np.zeros(agents, dtype=np.uint8)
        for m in range(args.matches):
            if steps[m] >= lengths[m]:
                done[m * per_match:(m + 1) * per_match] = 1
                steps[m] = 0
                lengths[m] = rng.integers(args.min_len, args.max_len + 1)
        fill(range(agents))
        frame["reward"] = rewards
        frame["done"] = done


if __name__ == "__main__":
    sys.exit(main())
