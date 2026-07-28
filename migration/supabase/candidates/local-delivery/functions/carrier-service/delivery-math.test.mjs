import assert from "node:assert/strict";
import test from "node:test";

import { calculateDriveTimeCost } from "./delivery-math.ts";

test("calculates the round-trip delivery price from the configured rate", () => {
  const result = calculateDriveTimeCost(900, 16093.4, 2.08);

  assert.equal(result.roundTripMinutes, 30);
  assert.equal(result.costDollars, 62.4);
  assert.equal(result.oneWayMiles, 10);
});

test("clamps invalid negative inputs instead of producing a credit", () => {
  const result = calculateDriveTimeCost(-60, -100, -2.08);

  assert.deepEqual(result, {
    costDollars: 0,
    oneWayMiles: 0,
    roundTripMinutes: 0,
  });
});
