"""Drive two already-running isolated Unity clients with the shared bridge client.

The launcher owns process lifetime and game slots. This runner only calls the
loopback endpoints recorded by each in-game test plugin and stores its evidence.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[2])
    ap.add_argument("--port", type=int, default=27052)
    ap.add_argument("--scenario", choices=["boot", "connect", "combat", "death", "migration", "warden", "piety"], default="boot")
    args = ap.parse_args()
    root = args.workspace.resolve()
    sys.path.insert(0, str(root / "local/coop-anything/tools/bridge"))
    from bridge_client import Bridge, BridgeError, GameCrashed

    evidence = root / "local/checks" / (time.strftime("%Y%m%d-%H%M%S") + "-" + args.scenario)
    evidence.mkdir(parents=True)
    bridges = []
    checks = []
    passed = False
    logs = [root / f"local/slots/{i}/BepInEx/LogOutput.log" for i in range(2)]
    log_offsets = [p.stat().st_size for p in logs]
    builds = [{"slot": i, "clientSha256": hashlib.sha256((root / f"local/slots/{i}/Modding/plugins/BlasClient.dll").read_bytes()).hexdigest()}
              for i in range(2)]
    (evidence / "builds.json").write_text(json.dumps(builds, indent=2), encoding="utf-8")

    def check(condition, name):
        if not condition:
            raise AssertionError(name)
        checks.append(name)
        print("PASS: " + name, flush=True)

    def snapshot(label):
        states = [g.state() for g in bridges]
        (evidence / (label + ".json")).write_text(json.dumps(states, indent=2), encoding="utf-8")
        with (evidence / "states.jsonl").open("a", encoding="utf-8") as trace:
            trace.write(json.dumps({"t": time.monotonic(), "label": label, "states": states}) + "\n")
        return states

    def wait(label, action, predicate, timeout=45):
        deadline = time.monotonic() + timeout
        last = None
        while time.monotonic() < deadline:
            try:
                last = action()
                if predicate(last):
                    return last
            except GameCrashed:
                raise
            except BridgeError as exc:
                last = str(exc)
            time.sleep(0.2)
        raise TimeoutError(f"{label}: {last!r}")

    try:
        for i in range(2):
            endpoint_path = root / f"local/slots/{i}/qa/bridge-endpoint.json"
            endpoint = json.loads(endpoint_path.read_text(encoding="utf-8"))
            g = Bridge.connect(endpoint["endpoints"][0], wait_s=10, timeout_s=12)
            bridges.append(g)
            g.require("state", "bot.newgame", "bot.connect", "bot.hit")
            g.call("bot.pause", {"scale": 1})
            ping1 = g.ping()
            ping2 = wait("advancing Unity frame", g.ping,
                         lambda p: p["frames"] > ping1["frames"] and 0 <= p["mainLoopAgeMs"] < 1000, timeout=20)
            check(ping1["pid"] == endpoint["pid"] and ping2["frames"] > ping1["frames"], f"slot {i} is a live Unity client")
            wait("title screen native transition", lambda: g.call("bot.landing"), lambda s: s["accepted"], timeout=30)
            state = wait("core ready", g.state, lambda s: s["connected"] is not None and (s["menu"]["main"] or s["inLevel"]), timeout=180)
            check(state["product"] == f"BlasCoopQA{i}", f"slot {i} has its own native Unity identity")
            manifest = json.loads((root / f"local/slots/{i}/qa-isolation.json").read_text(encoding="utf-8"))
            check(Path(state["savePath"]).is_relative_to(Path(manifest["SaveRoot"])), f"slot {i} saves stay in the private copy")
        states = snapshot("boot")
        check(states[0]["pid"] != states[1]["pid"], "two distinct real game processes")
        if args.scenario == "boot":
            passed = True
            return 0

        for g in bridges:
            s = g.state()
            if not s["inLevel"]:
                g.call("bot.newgame")
                wait("slot selection", lambda: g.call("bot.selectslot"), lambda s: s["accepted"], timeout=20)
                wait("new-game scene", g.state, lambda s: s["inLevel"], timeout=60)
        # Official Randomizer locations BS13 / BS01 provide the story rooms.
        # Assert the actual actor/manager below; raw asset string presence is not a mapping.
        scene = {"warden": "D17Z01S11", "piety": "D01Z04S18"}.get(args.scenario, "D01Z01S02")
        for g in bridges:
            current = g.state()
            if current["level"] != scene or not current["inLevel"]:
                g.call("bot.loadscene", {"scene": scene})
            wait("load " + scene, g.state, lambda s: s["level"] == scene and s["inLevel"] and s["player"] and s["player"]["active"], timeout=60)
        for i, g in enumerate(bridges):
            if not g.state()["connected"]:
                g.call("bot.connect", {"name": "QAHost" if i == 0 else "QAGuest", "port": args.port})
        for g in bridges:
            wait("two-player connection", g.state, lambda s: s["connected"] and len(s["peers"]) >= 1)
        states = wait("scene authority convergence", lambda: snapshot("authority-wait"),
                      lambda ss: all(s["inLevel"] and s["level"] == scene for s in ss)
                      and ss[0]["sceneHost"] is True and ss[1]["sceneHost"] is False
                      and ss[0]["hostTerm"] == ss[1]["hostTerm"], timeout=12)
        snapshot("connected")
        check(all(s["connected"] and len(s["peers"]) == 1 for s in states), "both clients see exactly one other player")
        check(states[0]["level"] == states[1]["level"] == scene, "both clients are in the tested scene")
        check(states[0]["sceneHost"] is True and states[1]["sceneHost"] is False, "clients agree on one scene host")
        if args.scenario == "connect":
            passed = True
            return 0

        if args.scenario in ("warden", "piety"):
            for i, g in enumerate(bridges):
                g.call("bot.heal")
                if args.scenario == "warden":
                    s = g.state()
                    wardens = [e for e in s["enemies"] if e["type"].endswith(".ElderBrother")]
                    check(len(wardens) == 1 and wardens[0]["id"] == "BS13", f"slot {i} contains the actual Warden actor")
                    # Warden has no authored BossFight.SpawnPoint. Enter the
                    # arena trigger on its ground, as a player normally would.
                    g.call("bot.teleport", {"x": wardens[0]["position"]["x"] + (-4 if i == 0 else 4),
                                             "y": s["player"]["position"]["y"]})
                else:
                    g.call("bot.bossstart")
            states = wait("native boss encounter starts on both clients", lambda: snapshot("boss-start-wait"),
                          lambda ss: all(any(f["started"] for f in s["bossFights"]) for s in ss), timeout=15)
            check(True, "both native boss managers report the encounter started")
        states = wait("matching enemies", lambda: snapshot("enemy-wait"),
                      lambda ss: all(any(e["active"] and not e["dead"] for e in s["enemies"]) for s in ss))
        bosses = args.scenario in ("warden", "piety")
        host_enemy = next(e for e in states[0]["enemies"] if e["active"] and not e["dead"]
                          and (("Boss" in e["type"]) == bosses))
        # Entity.Id identifies an enemy template, not a unique spawn. Match its
        # actual type and position here; later state probes also expose sync IDs.
        guests = [e for e in states[1]["enemies"] if e["id"] == host_enemy["id"] and e["type"] == host_enemy["type"]
                  and (bosses or (abs(e["position"]["x"] - host_enemy["position"]["x"]) < 0.5
                                  and abs(e["position"]["y"] - host_enemy["position"]["y"]) < 0.5))]
        check(len(guests) == 1, "the target spawn has one matching type and position on the guest")
        guest_enemy = guests[0]
        start_life = host_enemy["life"]
        check(abs(start_life - guest_enemy["life"]) < 0.5, "enemy initial health agrees")
        bridges[1].call("bot.hit", {"instance": guest_enemy["instance"], "damage": 1})
        after = wait("guest damage reaches the real host enemy", lambda: snapshot("damage-wait"),
                     lambda ss: any(e["instance"] == host_enemy["instance"] and e["life"] < start_life for e in ss[0]["enemies"]), timeout=12)
        h = next(e for e in after[0]["enemies"] if e["instance"] == host_enemy["instance"])
        after = wait("guest health converges", lambda: snapshot("converge-wait"),
                     lambda ss: any(e["instance"] == guest_enemy["instance"] and abs(e["life"] - h["life"]) < 0.5 for e in ss[1]["enemies"]), timeout=12)
        check(h["life"] < start_life, "guest attack changed actual host health")
        check(any(e["instance"] == guest_enemy["instance"] and abs(e["life"] - h["life"]) < 0.5 for e in after[1]["enemies"]), "both actual games agree after the guest hit")
        if args.scenario in ("death", "migration"):
            bridges[1].call("bot.hit", {"instance": guest_enemy["instance"], "damage": 1000})
            def dead_or_gone(state, instance):
                found = [e for e in state["enemies"] if e["instance"] == instance]
                return not found or (found[0]["dead"] and found[0]["life"] <= 0)
            after = wait("shared enemy death", lambda: snapshot("death-wait"),
                         lambda ss: dead_or_gone(ss[0], host_enemy["instance"]) and dead_or_gone(ss[1], guest_enemy["instance"]), timeout=12)
            check(True, "guest lethal hit kills the actual enemy on both clients")
            time.sleep(4)
            for i, path in enumerate(logs):
                with path.open("rb") as f:
                    f.seek(log_offsets[i]); added = f.read().decode("utf-8", "replace")
                (evidence / f"client-{i}-new.log").write_text(added, encoding="utf-8")
                access_error = "is inaccessible from method" in added or "FieldAccessException" in added or "MethodAccessException" in added
                check(not access_error, f"client {i} death handling has no inaccessible game-member exceptions")
            if args.scenario == "migration":
                old_term = after[1]["hostTerm"]
                bridges[0].call("bot.loadscene", {"scene": "D01Z01S01"})
                migrated = wait("guest promotion", bridges[1].state,
                                lambda s: s["sceneHost"] and s["hostTerm"] > old_term, timeout=12)
                check(dead_or_gone(migrated, guest_enemy["instance"]), "dead enemy stays dead after the host leaves")
                # LeaveScene reaches the server before Unity finishes loading.
                # The game ignores a second ChangeLevel during that load.
                wait("host finishes leaving", bridges[0].state,
                     lambda s: s["inLevel"] and s["level"] == "D01Z01S01" and s["player"] and s["player"]["active"], timeout=60)
                bridges[0].call("bot.loadscene", {"scene": scene})
                wait("host scene return", bridges[0].state,
                     lambda s: s["inLevel"] and s["level"] == scene and s["sceneHost"], timeout=60)
                returned = wait("authority after return", lambda: snapshot("return-wait"),
                                lambda ss: ss[0]["sceneHost"] and not ss[1]["sceneHost"] and ss[0]["hostTerm"] == ss[1]["hostTerm"], timeout=12)
                check(dead_or_gone(returned[1], guest_enemy["instance"]), "stored death survives host return")
        snapshot("final")
        passed = True
        return 0
    except Exception as exc:
        (evidence / "failure.txt").write_text(str(exc), encoding="utf-8")
        print("RESULT: FAIL: " + str(exc), flush=True)
        return 1
    finally:
        for g in bridges:
            g.close()
        (evidence / "checks.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
        print(f"EVIDENCE: {evidence}", flush=True)
        if passed:
            print(f"RESULT: PASS ({args.scenario}; {len(checks)} assertions; two real Unity clients)", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
