# Plan: homeless crew crash (issue 350)

Cause: vanilla HomelessJob.GetDestination picks a random part from an empty list when a homeless crew member stands on a part with no path sets and the game does not cull it. The mod leaves AI crew homeless on live player ships (takehunters, mech_capture_claim) and moves homeless crew in a weld merge.

Steps:
1. SimQueries.CullHomeless: delete the crew aboard with no home ship that the game culls (AllowCullHomelessCrew). Call it in takehunters, TestClaim (before the owner change) and WeldShip.Merge (before the crew move).
2. Invariant homeless_crew (spec 1971): no crew of a non-human side stand without a home on a live human ship.
3. Vanilla guard on HomelessJob.GetDestination for the empty list, with a test in core-vanilla-guards.
4. Specs 17, 44, 71, 2280 area. Issue comment.
