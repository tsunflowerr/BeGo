import assert from "node:assert/strict";
import test from "node:test";

import {
  buildGoogleMapsSearchUrl,
  buildShareUrl,
  canShowLocalTestVotingTools,
  canShowLocalTestTools,
  clampBenchmarkScenarioCount,
  formatCompactDistance,
  formatCompactDuration,
  formatMetricPercent,
  getStatusMeta,
  signedGapPercent,
} from "../src/lib/frontend-utils.ts";

test("status metadata falls back to waiting state", () => {
  assert.equal(getStatusMeta("Voting").label, "Voting");
  assert.equal(getStatusMeta("Unexpected").tone, "idle");
  assert.equal(getStatusMeta(undefined).action.includes("Invite members"), true);
});

test("compact distance and duration format edge values", () => {
  assert.equal(formatCompactDuration(42), "42s");
  assert.equal(formatCompactDuration(180), "3m");
  assert.equal(formatCompactDuration(7320), "2h 2m");
  assert.equal(formatCompactDistance(950), "950m");
  assert.equal(formatCompactDistance(1420), "1.4km");
});

test("benchmark helpers normalize values", () => {
  assert.equal(clampBenchmarkScenarioCount(-5), 1);
  assert.equal(clampBenchmarkScenarioCount(18.4), 18);
  assert.equal(clampBenchmarkScenarioCount(99), 60);
  assert.equal(formatMetricPercent(0.873, 1), "87.3%");
  assert.equal(signedGapPercent(12.345), "+12.3%");
  assert.equal(signedGapPercent(-4.51), "-4.5%");
});

test("local test tools are exposed only for host on localhost", () => {
  assert.equal(
    canShowLocalTestTools({
      nodeEnv: "development",
      hostname: "localhost",
      hasJoined: true,
      isHost: true,
      status: "WaitingForMembers",
    }),
    true
  );
  assert.equal(
    canShowLocalTestTools({
      nodeEnv: "production",
      hostname: "localhost",
      hasJoined: true,
      isHost: true,
      status: "WaitingForMembers",
    }),
    true
  );
  assert.equal(
    canShowLocalTestTools({
      nodeEnv: "development",
      hostname: "example.com",
      hasJoined: true,
      isHost: true,
      status: "WaitingForMembers",
    }),
    false
  );
});

test("local test voting tools are exposed only during voting", () => {
  assert.equal(
    canShowLocalTestVotingTools({
      hostname: "localhost",
      hasJoined: true,
      isHost: true,
      status: "Voting",
    }),
    true
  );
  assert.equal(
    canShowLocalTestVotingTools({
      hostname: "localhost",
      hasJoined: true,
      isHost: true,
      status: "WaitingForMembers",
    }),
    false
  );
});

test("share URL builder removes trailing slashes", () => {
  assert.equal(buildShareUrl("http://localhost:3000///", "abc"), "http://localhost:3000/room/abc");
});

test("google maps URL builder includes venue text and place id", () => {
  const url = buildGoogleMapsSearchUrl({
    name: "Bun Cha Test",
    address: "1 Hang Manh, Ha Noi",
    latitude: 21.032,
    longitude: 105.849,
    placeId: "ChIJtest",
  });
  const parsed = new URL(url);

  assert.equal(url.startsWith("https://www.google.com/maps/search/?api=1"), true);
  assert.equal(parsed.searchParams.get("query"), "Bun Cha Test, 1 Hang Manh, Ha Noi");
  assert.equal(parsed.searchParams.get("query_place_id"), "ChIJtest");
});
