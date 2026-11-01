"""Verify a real replica accepts sequence 1 after a scene-authority epoch change.

Uses two already-owned QA Unity copies and one scripted TCP authority. It never
launches or terminates a game. The scripted peer publishes a small, valid state
change for the actual observed actor; it does not generate player attacks.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import select
import socket
import struct
import sys
import time
from pathlib import Path


SCENE = "D17Z01S11"
HOST = "QAEpochHost"
FAKE = "QAEpochAuthority"
GUEST = "QAEpochGuest"


def frame(kind: int, body: bytes) -> bytes:
    if not 0 <= kind <= 255 or len(body) > 32768:
        raise ValueError("Invalid frame")
    return struct.pack("<HB", len(body), kind) + body


def named(body: bytes) -> tuple[str, bytes]:
    if not body or len(body) < body[0] + 1:
        raise ValueError("Truncated sender prefix")
    return body[1:1 + body[0]].decode("utf-8"), body[1 + body[0]:]


def authority(body: bytes) -> tuple[str, int, str]:
    if not body or len(body) < 1 + body[0] + 5:
        raise ValueError("Truncated scene authority")
    length = body[0]
    scene = body[1:1 + length].decode("utf-8")
    term = struct.unpack_from("<I", body, 1 + length)[0]
    at = 1 + length + 4
    owner_length = body[at]
    if len(body) != at + 1 + owner_length:
        raise ValueError("Invalid scene authority length")
    return scene, term, body[at + 1:].decode("utf-8")


def enemy_snapshot(body: bytes) -> dict:
    sender, data = named(body)
    if len(data) < 48 or len(data) != 48 + data[47]:
        raise ValueError("Invalid enemy snapshot length")
    scene_hash, term, generation, sequence = struct.unpack_from("<iIHI", data)
    net_id, flags = struct.unpack_from("<iB", data, 14)
    x, y, life, anim_hash, anim_time, host_time, spawn_key = struct.unpack_from("<fffiffi", data, 19)
    return dict(sender=sender, sceneHash=scene_hash, term=term, generation=generation,
                sequence=sequence, netId=net_id, flags=flags, x=x, y=y, life=life,
                animation=anim_hash, animationTime=anim_time, hostTime=host_time,
                spawnKey=spawn_key, template=data[48:].decode("utf-8"), data=data)


class Peer:
    def __init__(self, port: int):
        self.socket = socket.create_connection(("127.0.0.1", port), timeout=5)
        self.socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.buffer = bytearray()
        self.pending: list[tuple[int, bytes]] = []

    def send(self, kind: int, body: bytes = b""):
        self.socket.sendall(frame(kind, body))

    def read(self, kind: int, predicate=lambda body: True, timeout=8):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            for index, (packet_kind, body) in enumerate(self.pending):
                if packet_kind == kind and predicate(body):
                    self.pending.pop(index)
                    return body
            if len(self.pending) > 2048:
                raise RuntimeError("Unexpectedly large scripted-peer queue")
            readable, _, _ = select.select([self.socket], [], [], min(.1, max(0, deadline - time.monotonic())))
            if not readable:
                continue
            chunk = self.socket.recv(65536)
            if not chunk:
                raise ConnectionError("Scripted peer disconnected")
            self.buffer.extend(chunk)
            while len(self.buffer) >= 3:
                size, packet_kind = struct.unpack_from("<HB", self.buffer)
                if size > 32768:
                    raise ValueError("Oversized frame")
                if len(self.buffer) < size + 3:
                    break
                self.pending.append((packet_kind, bytes(self.buffer[3:3 + size])))
                del self.buffer[:3 + size]
        raise TimeoutError(f"Scripted peer packet {kind} was not received")

    def close(self):
        self.socket.close()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[2])
    ap.add_argument("--port", type=int, default=27052)
    args = ap.parse_args()
    if not 1024 <= args.port <= 65535:
        ap.error("A local QA server port is required")
    root = args.workspace.resolve()
    sys.path.insert(0, str(root / "local/coop-anything/tools/bridge"))
    from bridge_client import Bridge, BridgeError, GameCrashed

    evidence = root / "local/checks" / (time.strftime("%Y%m%d-%H%M%S") + "-epoch-observer")
    evidence.mkdir(parents=True)
    bridges = []
    fake = None
    checks = []
    passed = False

    def check(condition, label):
        if not condition:
            raise AssertionError(label)
        checks.append(label)
        print("PASS: " + label, flush=True)

    def wait(label, action, predicate, timeout=20):
        deadline = time.monotonic() + timeout
        last = None
        while time.monotonic() < deadline:
            try:
                last = action()
                if predicate(last):
                    return last
            except GameCrashed:
                raise
            except BridgeError as error:
                last = str(error)
            time.sleep(.15)
        raise TimeoutError(f"{label}: {last!r}")

    def record(label):
        states = [g.state() for g in bridges]
        (evidence / (label + ".json")).write_text(json.dumps(states, indent=2), encoding="utf-8")
        with (evidence / "states.jsonl").open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(dict(t=time.monotonic(), label=label, states=states)) + "\n")
        return states

    def boss(state):
        return next((e for e in state["enemies"] if e["id"] == "BS13"), None)

    try:
        builds = []
        for index in range(2):
            slot = root / f"local/slots/{index}"
            endpoint = json.loads((slot / "qa/bridge-endpoint.json").read_text(encoding="utf-8"))
            g = Bridge.connect(endpoint["endpoints"][0], wait_s=5, timeout_s=12)
            bridges.append(g)
            state = g.state()
            manifest = json.loads((slot / "qa-isolation.json").read_text(encoding="utf-8"))
            check(state["pid"] == endpoint["pid"] and state["product"] == f"BlasCoopQA{index}",
                  f"slot {index} is its recorded isolated Unity process")
            check(Path(state["savePath"]).is_relative_to(Path(manifest["SaveRoot"])),
                  f"slot {index} uses its private save root")
            g.require("state", "bot.disconnect", "bot.connect", "bot.loadscene", "bot.pause", "bot.flag",
                      "bot.heal", "bot.landing", "bot.newgame", "bot.selectslot")
            builds.append(dict(slot=index, clientSha256=hashlib.sha256((slot / "Modding/plugins/BlasClient.dll").read_bytes()).hexdigest(),
                               commonSha256=hashlib.sha256((slot / "Modding/data/Blasphemous.Multiplayer.Common.dll").read_bytes()).hexdigest()))
            g.call("bot.pause", {"scale": 1})
            if state["connected"]:
                g.call("bot.disconnect")
        (evidence / "builds.json").write_text(json.dumps(builds, indent=2), encoding="utf-8")
        for g in bridges:
            wait("disconnect before ordered election", g.state, lambda s: not s["connected"])
            wait("title transition", lambda: g.call("bot.landing"), lambda s: s["accepted"], timeout=30)
            state = wait("native core ready", g.state,
                         lambda s: s["connected"] is not None and (s["menu"]["main"] or s["inLevel"]), timeout=180)
            if not state["inLevel"]:
                wait("native campaign menu", lambda: g.call("bot.newgame"), lambda s: s["accepted"], timeout=30)
                wait("native slot panel", lambda: g.call("bot.selectslot"), lambda s: s["accepted"])
                wait("native new-game level", g.state, lambda s: s["inLevel"], timeout=60)
            if g.state()["level"] != "D01Z01S01":
                g.call("bot.loadscene", {"scene": "D01Z01S01"})
            wait("outside boss room", g.state, lambda s: s["inLevel"] and s["level"] == "D01Z01S01", timeout=60)
            # Private QA scenario reset, performed through native flag APIs.
            g.call("bot.flag", {"name": "D17Z01_BOSSDEAD", "value": False})
            g.call("bot.flag", {"name": "D17Z01S11_BOSSDEAD", "value": False})
            g.call("bot.heal")
            g.call("bot.loadscene", {"scene": SCENE})
            wait("fresh actual Warden", g.state,
                 lambda s: s["inLevel"] and s["level"] == SCENE and boss(s) and not boss(s)["dead"], timeout=60)

        bridges[0].call("bot.connect", {"name": HOST, "port": args.port})
        wait("initial real authority", bridges[0].state, lambda s: s["connected"] and s["sceneHost"])
        fake = Peer(args.port)
        encoded_name = FAKE.encode("utf-8")
        fake.send(8, bytes([17, 1, ord("a"), len(encoded_name)]) + encoded_name + bytes([0, 1]))
        check(fake.read(8)[0] == 0, "scripted peer passes the protocol17 handshake")
        fake.send(3, SCENE.encode("utf-8"))
        fake.read(20, lambda p: authority(p)[0] == SCENE and authority(p)[2] == HOST)
        bridges[1].call("bot.connect", {"name": GUEST, "port": args.port})
        wait("third peer remains a real replica", bridges[1].state,
             lambda s: s["connected"] and not s["sceneHost"] and len(s["peers"]) == 2)
        fake.send(17)
        captured = enemy_snapshot(fake.read(13, lambda p: enemy_snapshot(p)["template"] == "BS13"))
        old_term = captured["term"]
        def old_epoch_states():
            fake.send(17)
            return record("old-epoch-wait")
        baseline = wait("existing replica has high old-term sequence", old_epoch_states,
                        lambda ss: boss(ss[1]) and (boss(ss[1]).get("syncSequence") or 0) >= 20
                        and boss(ss[1]).get("syncNetId") == captured["netId"], timeout=20)
        actor = boss(baseline[1])
        original_instance = actor["instance"]
        original_sequence = actor["syncSequence"]
        check(actor["syncGeneration"] == captured["generation"] and actor["syncSpawnKey"] == captured["spawnKey"],
              "the wire snapshot identifies the same existing replica generation and spawn")
        check(baseline[1]["sceneHost"] is False and actor["life"] > 7,
              "the existing guest replica has living health and an old sequence above one")
        (evidence / "captured-snapshot.json").write_text(json.dumps({k: v for k, v in captured.items() if k != "data"}, indent=2), encoding="utf-8")
        bridges[0].call("bot.loadscene", {"scene": "D01Z01S01"})
        _, new_term, owner = authority(fake.read(20, lambda p: authority(p)[0] == SCENE and authority(p)[2] == FAKE))
        check(owner == FAKE and new_term > old_term, "the scripted second peer becomes the new scene authority")
        changed = wait("same real actor receives the new term", lambda: record("new-epoch-wait"),
                       lambda ss: ss[1]["hostTerm"] == new_term and not ss[1]["sceneHost"]
                       and boss(ss[1]) and boss(ss[1])["instance"] == original_instance)
        check(boss(changed[1])["syncSequence"] == 0,
              "the same observer actor resets its old per-authority sequence")
        target_life = actor["life"] - 7
        target_x = actor["position"]["x"] + .75
        target_y = actor["position"]["y"]
        data = bytearray(captured["data"][:47])
        struct.pack_into("<I", data, 4, new_term)
        struct.pack_into("<I", data, 10, 1)
        struct.pack_into("<fff", data, 19, target_x, target_y, target_life)
        struct.pack_into("<f", data, 39, changed[1]["gameTime"])
        fake.send(14, data)
        accepted = wait("new authority sequence one applies to existing replica", lambda: record("sequence-one-wait"),
                        lambda ss: boss(ss[1]) and boss(ss[1])["instance"] == original_instance
                        and boss(ss[1])["syncSequence"] == 1
                        and abs(boss(ss[1])["life"] - target_life) < .05
                        and abs(boss(ss[1])["position"]["x"] - target_x) < .15)
        check(original_sequence > 1 and boss(accepted[1])["syncSequence"] == 1,
              "sequence one updates the same real replica's health and pose after a higher old-epoch sequence")
        stale = bytearray(data)
        struct.pack_into("<I", stale, 4, old_term)
        struct.pack_into("<I", stale, 10, original_sequence + 10000)
        struct.pack_into("<f", stale, 27, target_life - 5)
        fake.send(14, stale)
        fake.send(12, bytes(6))
        fake.read(12)  # Server stream barrier after forwarding the stale packet.
        first_frame = bridges[1].ping()["frames"]
        wait("observer processes later frames", bridges[1].ping, lambda p: p["frames"] > first_frame + 10)
        quiet_until = time.monotonic() + .75
        while True:
            final = record("stale-old-epoch")
            if boss(final[1])["syncSequence"] != 1 or abs(boss(final[1])["life"] - target_life) >= .05:
                raise AssertionError("A stale old-authority packet overwrote the replica")
            if time.monotonic() >= quiet_until:
                break
            time.sleep(.1)
        check(True, "a higher sequence from the stale authority term cannot overwrite the replica during the quiet window")
        passed = True
        return 0
    except Exception as error:
        (evidence / "failure.txt").write_text(str(error), encoding="utf-8")
        print("RESULT: FAIL: " + str(error), flush=True)
        return 1
    finally:
        for g in bridges:
            try:
                g.call("bot.pause", {"scale": 0})
            except Exception:
                pass
        if fake:
            fake.close()
        for g in bridges:
            g.close()
        (evidence / "checks.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
        print(f"EVIDENCE: {evidence}", flush=True)
        if passed:
            print(f"RESULT: PASS ({len(checks)} assertions; two real Unity clients and one scripted authority)", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
