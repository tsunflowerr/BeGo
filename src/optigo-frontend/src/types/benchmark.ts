export interface BenchmarkSource {
  label: string;
  url: string;
  relevance: string;
}

export interface BenchmarkAlgorithmAggregate {
  algorithmKey: string;
  algorithmName: string;
  isOptiGo: boolean;
  runs: number;
  feasibleRate: number;
  winRate: number;
  averageObjectiveSeconds: number;
  averageTotalGroupTimeSeconds: number;
  averageMaxPassengerTimeSeconds: number;
  averageMaxMemberBurdenSeconds: number;
  averageWorstMemberRegretSeconds: number;
  averagePassengerBurdenGini: number;
  averageMaxDriverDetourSeconds: number;
  averageStdDriverDetourSeconds: number;
  averageDriverDetourGini: number;
  averageMaxWalkingTimeSeconds: number;
  averageSharedStopRate: number;
  averageStopCount: number;
  averageComputeTimeMs: number;
}

export interface BenchmarkMember {
  name: string;
  role: string;
  transportMode: string;
  latitude: number;
  longitude: number;
  seatCapacity: number;
}

export interface BenchmarkVenue {
  venueId: string;
  name: string;
  latitude: number;
  longitude: number;
  rating: number;
}

export interface BenchmarkAlgorithmRun {
  scenarioId: string;
  algorithmKey: string;
  algorithmName: string;
  isOptiGo: boolean;
  selectedVenueId: string;
  selectedVenueName: string;
  isFeasible: boolean;
  feasibilityIssues: string[];
  objectiveSeconds: number;
  totalGroupTimeSeconds: number;
  maxPassengerTimeSeconds: number;
  stdPassengerTimeSeconds: number;
  maxMemberBurdenSeconds: number;
  worstMemberRegretSeconds: number;
  passengerBurdenGini: number;
  maxDriverDetourSeconds: number;
  stdDriverDetourSeconds: number;
  driverDetourGini: number;
  totalDriverDetourSeconds: number;
  maxWalkingTimeSeconds: number;
  totalWalkingTimeSeconds: number;
  sharedStopRate: number;
  stopCount: number;
  sharedStopCount: number;
  computeTimeMs: number;
  gapToBestExternalPercent: number;
}

export interface BenchmarkScenarioResult {
  scenarioId: string;
  layout: string;
  memberCount: number;
  driverCount: number;
  pickupPassengerCount: number;
  venueCount: number;
  description: string;
  members: BenchmarkMember[];
  venues: BenchmarkVenue[];
  runs: BenchmarkAlgorithmRun[];
}

export interface BenchmarkWeakness {
  scenarioId: string;
  layout: string;
  metric: string;
  message: string;
  optiGoValue: number;
  bestExternalValue: number;
  bestExternalAlgorithm: string;
  gapPercent: number;
}

export interface OutingBenchmarkReport {
  seed: number;
  scenarioCount: number;
  startedAt: string;
  finishedAt: string;
  totalRuntimeMs: number;
  sources: BenchmarkSource[];
  aggregates: BenchmarkAlgorithmAggregate[];
  scenarios: BenchmarkScenarioResult[];
  weaknesses: BenchmarkWeakness[];
}
