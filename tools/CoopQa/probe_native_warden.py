"""Observe the native, disconnected Warden progression in a fresh owned QA slot.

The launcher owns the process. This probe never sets a story flag or starts the
boss manager directly; it uses the native menu and arena trigger.
"""
import json
import sys
import time
from pathlib import Path

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / "local/coop-anything/tools/bridge"))
from bridge_client import Bridge

evidence = root / "local/checks" / (time.strftime("%Y%m%d-%H%M%S") + "-native-warden")
evidence.mkdir(parents=True)
endpoint = json.loads((root / "local/slots/0/qa/bridge-endpoint.json").read_text())
game = Bridge.connect(endpoint["endpoints"][0], wait_s=10, timeout_s=12)


def snapshot(label):
    state = game.state()
    if state["product"] != "BlasCoopQA0":
        raise RuntimeError("This probe requires the owned QA slot")
    with (evidence / "states.jsonl").open("a", encoding="utf-8") as trace:
        trace.write(json.dumps({"label": label, "state": state}) + "\n")
    return state


def wait(label, predicate, seconds=60):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        state = snapshot(label)
        if predicate(state):
            return state
        time.sleep(.2)
    raise TimeoutError(label)


def boss(state):
    return next((enemy for enemy in state["enemies"] if enemy["id"] == "BS13"), None)


def describe(state):
    return {"connected": state["connected"], "defeated": state["wardenDefeated"],
            "boss": boss(state), "fights": state["bossFights"],
            "fsms": [fsm for fsm in state["fsms"] if "Boss" in fsm["gameObject"]]}


try:
    game.call("bot.pause", {"scale": 1})
    end = time.monotonic() + 30
    while time.monotonic() < end:
        if game.call("bot.landing")["accepted"]:
            break
        time.sleep(.2)
    state = wait("core", lambda s: s["connected"] is not None and (s["menu"]["main"] or s["inLevel"]), 180)
    if state["connected"]:
        raise RuntimeError("Disconnect the QA slot before the native control")
    if not state["inLevel"]:
        end = time.monotonic() + 30
        while time.monotonic() < end:
            if game.call("bot.newgame")["accepted"]:
                break
            time.sleep(.2)
        end = time.monotonic() + 20
        while time.monotonic() < end:
            try:
                if game.call("bot.selectslot")["accepted"]:
                    break
            except Exception:
                pass
            time.sleep(.2)
        wait("new game", lambda s: s["inLevel"] and s["player"] and s["player"]["active"])
    game.call("bot.loadscene", {"scene": "D17Z01S11"})
    state = wait("arena", lambda s: s["level"] == "D17Z01S11" and s["inLevel"] and s["player"] and s["player"]["active"] and boss(s))
    print("BEFORE ENTRY " + json.dumps(describe(state)), flush=True)
    wait("native arena trigger listening", lambda s: any(fsm["gameObject"] == "BossFight" and fsm["name"] == "FSM"
         and fsm["state"] == "Wait For Player" for fsm in s["fsms"]), 20)
    game.call("bot.heal")
    game.call("bot.teleport", {"x": -826.7, "y": 9.01})
    time.sleep(.3)
    # Authored BossFight trigger world bounds are x[-813.6875,-809.6875].
    game.call("bot.teleport", {"x": -811.7, "y": 9.01})
    state = wait("native intro", lambda s: boss(s) and boss(s)["treeRunning"] is True, 20)
    state = wait("grounded boss", lambda s: boss(s)["position"]["y"] < 11.5, 15)
    game.call("bot.hit", {"instance": boss(state)["instance"], "damage": 10000})
    for index in range(100):
        state = snapshot("native death")
        if index % 10 == 0:
            print("AFTER DEATH " + json.dumps(describe(state)), flush=True)
        if state["wardenDefeated"] or state["wardenRoomDefeated"]:
            print("NATIVE SOLO DEFEAT FLAG SET", flush=True)
            break
        time.sleep(.2)
finally:
    game.call("bot.pause", {"scale": 0})
    game.close()
    print("EVIDENCE: " + str(evidence), flush=True)
