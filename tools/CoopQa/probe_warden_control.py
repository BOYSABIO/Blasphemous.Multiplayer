"""Vanilla-mechanics control: disconnected private client enters the Warden arena."""
import json
import sys
import time
from pathlib import Path

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / "local/coop-anything/tools/bridge"))
from bridge_client import Bridge

out = root / "local/checks" / (time.strftime("%Y%m%d-%H%M%S") + "-warden-solo-control")
out.mkdir()
endpoint = json.loads((root / "local/slots/0/qa/bridge-endpoint.json").read_text())["endpoints"][0]
with Bridge.connect(endpoint) as game:
    game.call("bot.disconnect")
    for scene in ("D17Z01S04", "D17Z01S11"):
        game.call("bot.loadscene", {"scene": scene})
        until = time.monotonic() + 45
        while time.monotonic() < until:
            state = game.state()
            if state["inLevel"] and state["level"] == scene and state["player"]:
                break
            time.sleep(0.1)
        else:
            raise TimeoutError("control scene did not load")
    game.call("bot.heal")
    initial = game.state()
    boss = next(e for e in initial["enemies"] if e["type"].endswith(".ElderBrother"))
    game.call("bot.teleport", {"x": boss["position"]["x"] - 4, "y": initial["player"]["position"]["y"]})
    for i in range(25):
        state = game.state()
        (out / f"state-{i:02}.json").write_text(json.dumps(state, indent=2))
        if i % 5 == 0:
            print(json.dumps({"seconds": i / 2, "player": state["player"], "bossFights": state["bossFights"], "enemies": state["enemies"]}), flush=True)
        time.sleep(0.5)
print("CONTROL EVIDENCE:", out, flush=True)
