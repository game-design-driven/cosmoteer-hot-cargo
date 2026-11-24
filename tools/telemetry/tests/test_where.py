from hc_telemetry.where import compass, latest, lines


def test_compass_has_north_up_on_the_screen() -> None:
    assert compass(0, -100) == "N"
    assert compass(100, 0) == "E"
    assert compass(0, 100) == "S"
    assert compass(-100, -100) == "NW"


def test_latest_positions_drop_ended_missions_and_give_distance_and_direction() -> None:
    fleet = {"center": {"x": 0, "y": 0}, "ships": [{"ship": "p1"}]}
    m1 = {"mission": "M1", "type": "Bounty", "name": "Bounty: Byte", "objectives": ["DefeatShipsObjective"],
          "ships": [{"ship": "s1", "name": "Byte", "side": "hostile", "x": 3000, "y": -4000}], "items": [], "areas": []}
    m2 = {"mission": "M2", "type": "Explore", "name": "Explore", "objectives": [], "ships": [], "items": [],
          "areas": [{"objective": "ExploreObjective", "x": -500, "y": 0, "r": 800}]}
    events = [
        {"e": "snap", "gs": 60, "sys": "A", "fleet": fleet, "missions": [m1, m2]},
        {"e": "mission.ended", "gs": 70, "mission": "M2"},
    ]
    f, missions = latest(events)
    assert list(missions) == ["M1"]
    text = "\n".join(lines(f, missions))
    assert "Byte (hostile): 5,000 NE" in text
    assert "Explore" not in text
