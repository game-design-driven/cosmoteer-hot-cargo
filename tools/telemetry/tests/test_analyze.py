import json
from pathlib import Path

from hc_telemetry.analyze import analyze
from hc_telemetry.load import load

CTX = {"t": 0, "ss": 0, "sec": 1, "sys": "A", "tier": 1, "arch": "basic", "heat": 0, "tech": 8}


def ev(name: str, gs: float, /, **fields) -> dict:
    return {"e": name, **CTX, "gs": gs, **fields}


def write(path: Path, events: list[dict], broken_tail: bool = False) -> None:
    text = "\n".join(json.dumps(e) for e in events) + "\n"
    path.write_text(text + ('{"e": "trunc' if broken_tail else ""))


def test_sessions_join_runs_and_parts_and_a_cut_last_line(tmp_path: Path) -> None:
    head = {"run": "r1", "session": "s1", "wall_utc": "2026-01-01T00:00"}
    write(tmp_path / "r1__s1.jsonl", [ev("session.start", 0, **head), ev("heat.change", 1, src="fame", delta=5)])
    write(tmp_path / "r1__s1.part2.jsonl", [{"e": "file.part", "part": 2, "run": "r1", "session": "s1"}, ev("run.won", 9, reason="sold")], broken_tail=True)
    write(tmp_path / "r1__s2.jsonl", [ev("session.start", 9, run="r1", session="s2", wall_utc="2026-01-01T01:00", loaded=True)])
    runs = load(tmp_path)
    assert [r.id for r in runs] == ["r1"]
    assert [s.id for s in runs[0].sessions] == ["s1", "s2"]
    assert [e["e"] for e in runs[0].sessions[0].events] == ["session.start", "heat.change", "run.won"]


def test_hunter_fates_and_heat_sources(tmp_path: Path) -> None:
    hunter = {"hunter": True, "side": "hunter"}
    write(tmp_path / "r__s.jsonl", [
        ev("session.start", 0, run="r", session="s", wall_utc="x"),
        ev("heat.accrual", 5, time=2.0, trail=1.0),
        ev("heat.change", 6, src="fame", delta=10.0),
        ev("combat.spawn", 10, ship="h1", **hunter),
        ev("combat.spawn", 10, ship="h2", **hunter),
        ev("combat.spawn", 10, ship="h3", **hunter),
        ev("combat.destroyed", 40, ship="h1", side="hunter"),
        ev("combat.removed", 50, ship="h2", side="hunter", src="flee_jump"),
        ev("system.enter", 70, sys="B", sec=2),
    ])
    rep = analyze(load(tmp_path)[0])
    assert rep.heat_sources == {"fame": 10.0, "time": 2.0, "trail": 1.0}
    outcomes = {row[0]: row[1] for row in next(t for t in rep.tables if t.title == "Hunter outcomes").rows}
    assert outcomes == {"destroyed": 1, "flee_jump": 1, "left behind (players jumped)": 1}


def test_fights_jumps_captures_and_diplomacy_make_tables(tmp_path: Path) -> None:
    ship = {"drive": False, "fuel": 0, "capacity": 0, "cost": None, "too_big": False, "parts": 50}
    write(tmp_path / "r__s.jsonl", [
        ev("session.start", 0, run="r", session="s", wall_utc="x"),
        ev("combat.fight", 100, start_gs=40, end_gs=100, duration_s=60, fight_sec=1, sides=["hunter", "player"],
           damage={"player": 10, "hunter": 90}, destroyed={"hunter": 2}, kills=2, losses=0, crew_lost=1, windows=5),
        ev("combat.captured", 110, ship="c1", name="Taken", player=0, old_player=100017, old_side="hunter", how="claim", value=900, drive=False, tech=0),
        ev("run.jump", 120, **{"from": "A", "to": "B", "kind": "RelayClear",
           "jumped": [{"ship": "p1", "name": "Mother", "player": 0, "tech": 2, **ship, "drive": True}],
           "left": [{"ship": "c1", "name": "Taken", "player": 0, "tech": 1, **ship}]}),
        ev("combat.fleet_left", 120, ship="c1", name="Taken", player=0, reason="left_behind", value=900, tech=1),
        ev("dip.surrender", 130, name="Arche", side="hunter", odds=0.2, stance="Surrender", by="ai", price=4000, accepted=True),
    ])
    rep = analyze(load(tmp_path)[0])
    titles = {t.title: t for t in rep.tables}
    assert titles["Fights"].rows[0][6] == 2
    assert [r[0] for r in titles["Jumps"].rows] == ["2:00"]
    assert titles["Jumps"].rows[0][5] == 1
    assert {r[1] for r in titles["Fleet changes"].rows} == {"captured", "left the fleet"}
    assert titles["Diplomacy"].rows[0][9] == "yes"
    assert any(f.startswith("Captures: 1 ships (claim 1), 1 without a hyperdrive") for f in rep.findings)


def test_a_client_copy_on_the_host_stays_its_own_run(tmp_path: Path) -> None:
    head = {"run": "r", "wall_utc": "2026-01-01T00:00", "mp": True}
    write(tmp_path / "r__host.jsonl", [ev("session.start", 0, session="host", host=True, local_player=0, **head)])
    (tmp_path / "players" / "Tom").mkdir(parents=True)
    write(tmp_path / "players" / "Tom" / "r__cli.jsonl", [ev("session.start", 0, session="cli", host=False, local_player=1, **head)])
    # The same client file, pulled from the client's machine as well, counts once.
    write(tmp_path / "r__cli.jsonl", [ev("session.start", 0, session="cli", host=False, local_player=1, **head)])
    runs = {r.id: [s.id for s in r.sessions] for r in load(tmp_path)}
    assert runs == {"r": ["host"], "r (client 1)": ["cli"]}


def test_time_use_splits_each_visit_and_adds_session_parts(tmp_path: Path) -> None:
    def use(**kw: float) -> dict:
        return {"fight": 0, "build": 0, "trade": 0, "travel": 0, "repair": 0, "idle": 0, **kw}
    snap = {"why": "timer", "money": {"0": 0}, "fleet_value": 1, "tech_value": 1, "fleet_firepower": 1, "fleet_parts": 1, "crew": 1, "notoriety": 0}
    write(tmp_path / "r__s1.jsonl", [
        ev("session.start", 0, run="r", session="s1", wall_utc="1"),
        ev("snap", 50, **snap, time_use=use(travel=30, idle=20)),
        ev("snap", 100, **snap, time_use=use(travel=60, fight=40)),
    ])
    # A load continues the first visit: the new session counts its part from 0.
    write(tmp_path / "r__s2.jsonl", [
        ev("session.start", 100, run="r", session="s2", wall_utc="2"),
        ev("system.enter", 200, sys="B", sec=2, prev_time_use=use(repair=100)),
        ev("snap", 260, sys="B", sec=2, **snap, time_use=use(trade=60)),
    ])
    rep = analyze(load(tmp_path)[0])
    visits = dict(rep.sector_time_use)
    assert visits["S1 A"] == use(travel=60, fight=40, repair=100)
    assert visits["S2 B"] == use(trade=60)
    sectors = next(t for t in rep.tables if t.title == "Sectors")
    assert sectors.columns[-6:] == ["fight %", "build %", "trade %", "travel %", "repair %", "idle %"]
    assert sectors.rows[0][-6:] == ["20", "0", "0", "30", "50", "0"]
    assert any(f.startswith("Time in systems by activity: repair 38 %") for f in rep.findings)


def test_notes_list_in_play_order_with_nearby_events(tmp_path: Path) -> None:
    write(tmp_path / "r__s.jsonl", [
        ev("session.start", 0, run="r", session="s", wall_utc="x"),
        ev("heat.stage", 95, stage=1, name="Warning"),
        ev("note", 100, text="hunters came from the sun", player="Tom", screen={"event": "defector"}, screenshot=None),
        ev("trace", 101, method="X.Y"),
        ev("combat.destroyed", 130, ship="h1", name="Corporate Arche", side="hunter"),
        ev("heat.stage", 500, stage=2, name="Hunt"),
        ev("note", 600, text="second", player="Yarden", screen={}, screenshot="r__x.png"),
    ])
    rep = analyze(load(tmp_path)[0])
    notes = rep.tables[0]
    assert notes.title == "Playtest notes"
    assert [r[7] for r in notes.rows] == ["hunters came from the sun", "second"]
    assert notes.rows[0][5] == "event defector"
    around = [r for r in rep.tables[1].rows if r[0] == 1]
    assert [r[3] for r in around] == ["heat.stage", "combat.destroyed"]
    assert rep.findings[0] == "2 playtest notes (see 'Playtest notes')."
