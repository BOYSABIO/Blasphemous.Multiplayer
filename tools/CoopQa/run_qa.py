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
    ap.add_argument("--extended-boss", action="store_true", help="Exercise Warden hazards, replay, handoff and native death")
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
    builds = [{"slot": i, "clientSha256": hashlib.sha256((root / f"local/slots/{i}/Modding/plugins/BlasClient.dll").read_bytes()).hexdigest(),
               "commonSha256": hashlib.sha256((root / f"local/slots/{i}/Modding/data/Blasphemous.Multiplayer.Common.dll").read_bytes()).hexdigest(),
               "qaSha256": hashlib.sha256(next((root / f"local/slots/{i}").rglob("Blasphemous.CoopQA.dll")).read_bytes()).hexdigest()}
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
        if args.extended_boss:
            if args.scenario != "warden":
                raise ValueError("Extended boss checks currently implement Warden only")
            def boss(s):
                return next((e for e in s["enemies"] if e["id"] == "BS13"), None)
            def safe_states(label):
                for g in bridges:
                    player = g.state()["player"]
                    if not player or not player["active"]:
                        continue
                    if player["dead"]:
                        raise AssertionError("A test player died before the next action; reload the scenario")
                    g.call("bot.heal")
                return snapshot(label)
            # IsFightStarted becomes true before the authored intro jump ends.
            # Wait for its native decision tree instead of sampling that gap.
            states = wait("native Warden intro completes", lambda: safe_states("boss-extended-start"),
                          lambda ss: boss(ss[0])["treeRunning"] is True and boss(ss[1])["treeRunning"] is False, timeout=15)
            check(boss(states[0])["treeRunning"] is True and boss(states[1])["treeRunning"] is False,
                  "only the authoritative Warden runs its decision tree")
            check(boss(states[1])["damageCollider"] is True, "guest Warden retains its native damage collider")
            check(states[1]["wardenIntroVisible"] is False, "guest intro dummy has finished its presentation")
            sword_life = boss(states[0])["life"]
            def sword_overlap():
                current = bridges[1].state()
                target = boss(current)
                if target and target["position"]["y"] < 12:
                    bridges[1].call("bot.teleport", {"x": target["position"]["x"] - 1.5, "y": 9.01})
                    bridges[1].call("bot.walk", {"x": 1, "seconds": .08})
                    time.sleep(.12)
                    bridges[1].call("bot.swordhit")
                return safe_states("sword-overlap-wait")
            states = wait("guest native sword collision reaches authoritative Warden", sword_overlap,
                          lambda ss: boss(ss[0])["life"] < sword_life and abs(boss(ss[0])["life"] - boss(ss[1])["life"]) < .5,
                          timeout=15)
            check(True, "guest native sword overlap damages the shared boss through its enabled damage collider")
            seen = set()
            def hazards_match(ss):
                host = {a["id"]: a for a in ss[0]["wardenHazards"] if a["active"]}
                guest = {a["id"]: a for a in ss[1]["wardenHazards"] if a["active"]}
                for key in host.keys() & guest.keys():
                    a, b = host[key], guest[key]
                    if (a["kind"] == b["kind"] and a["prefab"] == b["prefab"]
                        and abs(a["position"]["x"] - b["position"]["x"]) < .15
                        and abs(a["position"]["y"] - b["position"]["y"]) < .15
                        and a["replica"] is False and b["replica"] is True
                        and b["colliders"] and all(not c["enabled"] for c in b["colliders"])):
                        seen.add(a["kind"])
                return {"Area", "Mace"}.issubset(seen)
            wait("both native hazard kinds replay at matching positions", lambda: safe_states("hazard-wait"), hazards_match, timeout=25)
            check(True, "Area and Mace effects share identity, prefab, position and one collision authority")

            # Disconnect/reconnect a live guest to exercise the server's cached
            # encounter replay, rather than calling the guest boss-start bot.
            bridges[1].call("bot.disconnect")
            wait("guest disconnected", bridges[1].state, lambda s: not s["connected"])
            bridges[1].call("bot.loadscene", {"scene": "D01Z01S01"})
            wait("guest leaves arena", bridges[1].state, lambda s: s["inLevel"] and s["level"] == "D01Z01S01")
            safe_states("latejoin-away")
            bridges[1].call("bot.loadscene", {"scene": scene})
            wait("guest returns to arena", bridges[1].state, lambda s: s["inLevel"] and s["level"] == scene and boss(s))
            bridges[1].call("bot.connect", {"name": "QAGuest", "port": args.port})
            states = wait("late join native encounter and health replay", lambda: safe_states("latejoin-wait"),
                          lambda ss: ss[1]["connected"] and len(ss[0]["peers"]) == 1
                          and any(f["started"] for f in ss[1]["bossFights"])
                          and abs(boss(ss[0])["life"] - boss(ss[1])["life"]) < .5, timeout=20)
            check(True, "fresh guest arena receives native encounter start and current boss health")

            old_term = states[1]["hostTerm"]
            handoff_life = boss(states[1])["life"]
            before_position = boss(states[1])["position"]
            bridges[0].call("bot.loadscene", {"scene": "D01Z01S01"})
            promoted = wait("living boss authority handoff", bridges[1].state,
                            lambda s: s["sceneHost"] and s["hostTerm"] > old_term and boss(s)["treeRunning"] is True, timeout=15)
            check(boss(promoted)["controller"] is True, "promoted Warden restores native movement and decisions")
            wait("host completes arena exit", bridges[0].state, lambda s: s["inLevel"] and s["level"] == "D01Z01S01")
            safe_states("promoted")
            moved = wait("promoted boss keeps fighting", bridges[1].state,
                         lambda s: boss(s) and (abs(boss(s)["position"]["x"] - before_position["x"]) > .3
                                               or abs(boss(s)["position"]["y"] - before_position["y"]) > .3), timeout=20)
            check(True, "the promoted Warden continues moving in the actual game")
            bridges[0].call("bot.loadscene", {"scene": scene})
            states = wait("original host rejoins the surviving authority", lambda: safe_states("handoff-return-wait"),
                          lambda ss: ss[0]["inLevel"] and ss[0]["level"] == scene and boss(ss[0])
                          and not ss[0]["sceneHost"] and ss[1]["sceneHost"] and ss[0]["hostTerm"] == ss[1]["hostTerm"]
                          and any(f["started"] for f in ss[0]["bossFights"]), timeout=45)
            check(True, "returning original host joins the active encounter without preempting its authority")
            check(all(abs(boss(s)["life"] - handoff_life) < .5 for s in states),
                  "returning host preserves the surviving boss health instead of resetting it")
            states = wait("surviving host keeps native decisions", lambda: safe_states("returned-ai-wait"),
                          lambda ss: boss(ss[1])["treeRunning"] is True and boss(ss[0])["treeRunning"] is False, timeout=15)
            check(True, "surviving authority continues native boss decisions after the original host rejoins")
            # Damage relay validates hit proximity. Move the attacking player
            # beside the actual boss before testing a lethal hit after travel.
            target = boss(states[0])
            bridges[0].call("bot.teleport", {"x": target["position"]["x"] - 2, "y": target["position"]["y"]})
            bridges[0].call("bot.hit", {"instance": target["instance"], "damage": 10000})
            states = wait("native Warden death on both games", lambda: safe_states("boss-death-wait"),
                          lambda ss: all((boss(s) is None or boss(s)["dead"])
                                         and not any(f["started"] for f in s["bossFights"]) for s in ss), timeout=20)
            check(True, "guest lethal hit ends both native boss fights")
            check(all(s["wardenDefeated"] or s["wardenRoomDefeated"] for s in states),
                  "both games record the native Warden defeat flag")
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
                     lambda s: s["inLevel"] and s["level"] == scene and not s["sceneHost"], timeout=60)
                returned = wait("authority after return", lambda: snapshot("return-wait"),
                                lambda ss: not ss[0]["sceneHost"] and ss[1]["sceneHost"] and ss[0]["hostTerm"] == ss[1]["hostTerm"], timeout=12)
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
            if args.extended_boss:
                try:
                    g.call("bot.pause", {"scale": 0})
                except Exception:
                    pass
            g.close()
        (evidence / "checks.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
        print(f"EVIDENCE: {evidence}", flush=True)
        if passed:
            print(f"RESULT: PASS ({args.scenario}; {len(checks)} assertions; two real Unity clients)", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
