using System.Diagnostics;
using System.Text.Json;
using Google.OrTools.ConstraintSolver;
using Google.Protobuf.WellKnownTypes;
using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.Services;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.Routing;

public class OutingBenchmarkService : IOutingBenchmarkService
{
    private const double FairnessCostGuardRatio = 1.08;
    private const double FairnessCostGuardSlackSeconds = 90;

    private static readonly string[] Layouts =
    [
        "clustered",
        "spread",
        "corridor",
        "outlier",
        "capacity_tight",
        "shared_cluster"
    ];

    public async Task<OutingBenchmarkReportDto> RunAsync(
        OutingBenchmarkRequestDto request,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var scenarioCount = Math.Clamp(request.ScenarioCount, 1, 60);
        var scenarios = Enumerable.Range(0, scenarioCount)
            .Select(index => GenerateScenario(request.Seed, index))
            .ToList();

        var scenarioResults = new List<BenchmarkScenarioResultDto>();
        foreach (var scenario in scenarios)
        {
            ct.ThrowIfCancellationRequested();

            var runs = new List<BenchmarkAlgorithmRunDto>
            {
                await RunWeightedMedianNearestDriverAsync(scenario, ct),
                await RunOrToolsPickupCostFirstAsync(scenario, ct),
                await RunOrToolsFairnessTunedAsync(scenario, ct),
                await RunPyvrpNativeCostFirstAsync(scenario, ct),
                await RunPyvrpNativeFairnessSelectedAsync(scenario, ct),
                await RunOptiGoHybridAsync(scenario, ct)
            };
            foreach (var run in runs)
            {
                run.ScenarioId = scenario.ScenarioId;
                run.IsScenarioServiceable = IsServiceable(scenario);
            }

            ApplyExternalGaps(runs);
            scenarioResults.Add(ToScenarioResult(scenario, runs));
        }

        stopwatch.Stop();
        var finishedAt = DateTimeOffset.UtcNow;
        var allRuns = scenarioResults.SelectMany(scenario => scenario.Runs).ToList();

        return new OutingBenchmarkReportDto
        {
            Seed = request.Seed,
            ScenarioCount = scenarioCount,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            TotalRuntimeMs = stopwatch.Elapsed.TotalMilliseconds,
            Sources = BuildSources(),
            Aggregates = BuildAggregates(allRuns),
            Scenarios = scenarioResults,
            Weaknesses = BuildWeaknesses(scenarioResults)
        };
    }

    private static BenchmarkScenario GenerateScenario(int seed, int index)
    {
        var random = new Random(seed + index * 7919);
        var layout = Layouts[index % Layouts.Length];
        var memberCount = layout switch
        {
            "capacity_tight" => random.Next(7, 11),
            "shared_cluster" => random.Next(6, 10),
            _ => random.Next(3, 11)
        };
        var driverCount = layout switch
        {
            "capacity_tight" => Math.Max(1, memberCount / 4),
            _ => Math.Clamp(1 + memberCount / 4 + random.Next(0, 2), 1, 3)
        };
        var pickupCount = layout switch
        {
            "capacity_tight" => Math.Max(1, memberCount - driverCount - 1),
            "shared_cluster" => Math.Max(2, memberCount / 2),
            "outlier" => Math.Max(1, memberCount / 3),
            _ => Math.Clamp(memberCount / 2, 1, memberCount - driverCount)
        };

        if (driverCount + pickupCount > memberCount)
        {
            pickupCount = Math.Max(0, memberCount - driverCount);
        }

        var center = new Coordinate(10.7769, 106.7009);
        var session = new Session($"bench-{index:000}");

        for (var i = 0; i < driverCount; i++)
        {
            var mode = layout == "shared_cluster"
                ? TransportMode.Car
                : layout == "capacity_tight" || random.NextDouble() < 0.45
                ? TransportMode.Motorbike
                : TransportMode.Car;
            session.AddMember(new Member(
                session.Id,
                $"Driver {i + 1}",
                GenerateMemberLocation(layout, center, random, i, memberCount, isOutlier: false, isPickup: false),
                mode,
                MemberMobilityRole.SelfTravel));
        }

        for (var i = 0; i < pickupCount; i++)
        {
            var isOutlier = layout == "outlier" && i == pickupCount - 1;
            var member = new Member(
                session.Id,
                $"Pickup {i + 1}",
                GenerateMemberLocation(layout, center, random, driverCount + i, memberCount, isOutlier, isPickup: true),
                TransportMode.Walking,
                MemberMobilityRole.NeedsPickup);
            session.AddMember(member);
            session.CreateOrGetPickupRequest(member.Id);
        }

        for (var i = driverCount + pickupCount; i < memberCount; i++)
        {
            session.AddMember(new Member(
                session.Id,
                $"Self {i - driverCount - pickupCount + 1}",
                GenerateMemberLocation(layout, center, random, i, memberCount, isOutlier: false, isPickup: false),
                random.NextDouble() < 0.35 ? TransportMode.Cycling : TransportMode.Motorbike,
                MemberMobilityRole.SelfTravel));
        }

        var venueCount = random.Next(6, 11);
        var venues = GenerateVenues(layout, center, random, venueCount);
        var description = layout switch
        {
            "clustered" => "Nhóm gần nhau, dùng để kiểm tra overhead của pickup/meeting-point.",
            "spread" => "Nhóm rải rộng, dễ lộ nhược điểm về tổng thời gian và fairness.",
            "corridor" => "Nhiều điểm nằm dọc trục đi tới venue, phù hợp corridor/shared stop.",
            "outlier" => "Có một passenger xa, kiểm tra max passenger time và detour.",
            "capacity_tight" => "Số ghế sát nhu cầu, kiểm tra feasibility và phân bổ capacity.",
            "shared_cluster" => "Passenger tập trung thành cụm, kiểm tra shared meetpoint.",
            _ => "Synthetic small-group outing scenario."
        };

        return new BenchmarkScenario(
            $"S{index + 1:000}",
            layout,
            session,
            venues,
            description);
    }

    private static Coordinate GenerateMemberLocation(
        string layout,
        Coordinate center,
        Random random,
        int index,
        int memberCount,
        bool isOutlier,
        bool isPickup)
    {
        if (isOutlier)
        {
            return Offset(center, 0.030 + random.NextDouble() * 0.010, 0.060 + random.NextDouble() * 0.020);
        }

        return layout switch
        {
            "clustered" => Offset(center, RandomRange(random, -0.006, 0.006), RandomRange(random, -0.006, 0.006)),
            "corridor" => Offset(center, RandomRange(random, -0.004, 0.004), -0.045 + 0.090 * index / Math.Max(1, memberCount - 1)),
            "capacity_tight" => Offset(center, RandomRange(random, -0.020, 0.020), RandomRange(random, -0.030, 0.030)),
            "shared_cluster" when isPickup => Offset(center, RandomRange(random, 0.008, 0.010), RandomRange(random, -0.002, 0.002)),
            "shared_cluster" => Offset(center, RandomRange(random, -0.016, 0.016), RandomRange(random, -0.018, 0.018)),
            "spread" => Offset(center, RandomRange(random, -0.035, 0.035), RandomRange(random, -0.040, 0.040)),
            _ => Offset(center, RandomRange(random, -0.020, 0.020), RandomRange(random, -0.025, 0.025))
        };
    }

    private static IReadOnlyList<Venue> GenerateVenues(
        string layout,
        Coordinate center,
        Random random,
        int venueCount)
    {
        var venues = new List<Venue>();
        for (var i = 0; i < venueCount; i++)
        {
            Coordinate location = layout switch
            {
                "corridor" => Offset(center, RandomRange(random, -0.006, 0.006), -0.010 + i * 0.010),
                "outlier" when i == 0 => Offset(center, 0.018, 0.036),
                "shared_cluster" when i == 0 => Offset(center, 0.007, 0.010),
                _ => Offset(center, RandomRange(random, -0.018, 0.018), RandomRange(random, -0.024, 0.024))
            };
            var rating = Math.Round(3.7 + random.NextDouble() * 1.2, 1);
            var reviews = random.Next(40, 900);
            venues.Add(new Venue(
                $"v-{layout}-{i}",
                $"Venue {i + 1}",
                "cafe",
                location,
                rating,
                reviews));
        }

        return venues;
    }

    private async Task<BenchmarkAlgorithmRunDto> RunWeightedMedianNearestDriverAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var center = OutingSearchCenterCalculator.Calculate(scenario.Session);
        var venue = scenario.Venues
            .OrderBy(candidate => candidate.GetLocation().DistanceTo(center))
            .ThenByDescending(candidate => candidate.Rating)
            .First();
        var assignment = BuildNearestDriverAssignment(scenario.Session);
        var candidate = assignment == null
            ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, "Không đủ driver còn ghế.", ct)
            : await EvaluateDoorstepCandidateAsync(scenario.Session, venue, assignment, provider, ct);
        stopwatch.Stop();

        return ToRun("median_nearest", "Weighted median + nearest driver", false, candidate, stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task<BenchmarkAlgorithmRunDto> RunExactDoorstepVrpAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        CandidateResultDto? best = null;

        foreach (var venue in scenario.Venues)
        {
            var candidate = await FindBestExactDoorstepCandidateAsync(scenario.Session, venue, provider, ct);
            if (best == null || RoutingSolutionScorer.CalculateTotalCostBaseline(candidate) < RoutingSolutionScorer.CalculateTotalCostBaseline(best))
            {
                best = candidate;
            }
        }

        stopwatch.Stop();
        return ToRun("exact_doorstep_vrp", "Exact doorstep VRP", false, best!, stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task<BenchmarkAlgorithmRunDto> RunOrToolsPickupCostFirstAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var assignment = await BuildOrToolsAssignmentAsync(scenario.Session, venue, provider, ct);
            var candidate = assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, "OR-Tools không tìm được nghiệm capacity pickup.", ct)
                : await EvaluateDoorstepCandidateAsync(scenario.Session, venue, assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = candidates.OrderBy(RoutingSolutionScorer.CalculatePureCost).First();
        return ToRun("ortools_pickup_cost", "OR-Tools pickup VRP cost-first", false, best, stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task<BenchmarkAlgorithmRunDto> RunOrToolsFairnessTunedAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var assignment = await BuildOrToolsAssignmentAsync(scenario.Session, venue, provider, ct);
            var candidate = assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, "OR-Tools không tìm được nghiệm capacity pickup.", ct)
                : await EvaluateDoorstepCandidateAsync(scenario.Session, venue, assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = SelectFairnessTunedExternalCandidate(candidates);
        return ToRun("ortools_pickup_fair", "OR-Tools pickup VRP fairness-tuned", false, best, stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task<BenchmarkAlgorithmRunDto> RunPyvrpNativeCostFirstAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var assignment = await BuildPyvrpAssignmentAsync(scenario, venue, provider, ct);
            var candidate = assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, "PyVRP native không tìm được nghiệm capacity pickup.", ct)
                : await EvaluateDoorstepCandidateAsync(scenario.Session, venue, assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = candidates.OrderBy(RoutingSolutionScorer.CalculatePureCost).First();
        return ToRun("pyvrp_hgs_cost", "PyVRP Hybrid Genetic Search cost-first", false, best, stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task<BenchmarkAlgorithmRunDto> RunPyvrpNativeFairnessSelectedAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var assignment = await BuildPyvrpAssignmentAsync(scenario, venue, provider, ct);
            var candidate = assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, "PyVRP native không tìm được nghiệm capacity pickup.", ct)
                : await EvaluateDoorstepCandidateAsync(scenario.Session, venue, assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = SelectFairnessTunedExternalCandidate(candidates);
        return ToRun("pyvrp_hgs_fair", "PyVRP Hybrid Genetic Search fairness-selected", false, best, stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task<BenchmarkAlgorithmRunDto> RunOptiGoHybridAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var traffic = new BenchmarkTrafficSnapshotProvider(scenario.Layout);
        var planner = new HybridOutingRoutePlanner(
            new SharedDestinationRouteOptimizer(new StopCandidateGenerator(), provider),
            provider,
            traffic);
        var evaluator = new DefaultVenueEvaluator();
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            try
            {
                candidates.Add(await planner.PlanVenueAsync(scenario.Session, venue, ct));
            }
            catch (InvalidOperationException ex)
            {
                candidates.Add(await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, ex.Message, ct));
            }
        }

        _ = evaluator.RankCandidates(candidates, Math.Min(3, candidates.Count));
        var best = SelectRobustOptiGoCandidate(candidates);
        stopwatch.Stop();
        return ToRun("optigo_hybrid", "OptiGo Hybrid route-pool Pareto", true, best, stopwatch.Elapsed.TotalMilliseconds);
    }

    private static CandidateResultDto SelectRobustOptiGoCandidate(IReadOnlyList<CandidateResultDto> candidates)
    {
        var feasible = candidates.Where(candidate => candidate.IsFeasible).ToList();
        var pool = feasible.Count > 0 ? feasible : candidates.ToList();
        var bestCompositeCost = pool.Min(RoutingSolutionScorer.CalculateCompositeCost);
        var compositePool = pool
            .Where(candidate => RoutingSolutionScorer.CalculateCompositeCost(candidate) <= bestCompositeCost * 1.03 + 1)
            .ToList();
        var bestPureCost = compositePool.Min(RoutingSolutionScorer.CalculatePureCost);
        var costGuard = bestPureCost * FairnessCostGuardRatio + FairnessCostGuardSlackSeconds;
        var boundedCostPool = compositePool
            .Where(candidate => RoutingSolutionScorer.CalculatePureCost(candidate) <= costGuard)
            .ToList();

        return boundedCostPool
            .OrderBy(candidate => candidate.Metrics.MaxMemberBurdenSeconds)
            .ThenBy(candidate => candidate.Metrics.WorstMemberRegretSeconds)
            .ThenBy(candidate => candidate.Metrics.MaxPassengerTimeSeconds)
            .ThenBy(candidate => candidate.Metrics.PassengerBurdenGini)
            .ThenBy(candidate => candidate.Metrics.DriverDetourGini)
            .ThenBy(candidate => candidate.Metrics.StdDriverDetourSeconds)
            .ThenBy(candidate => candidate.Metrics.MaxDriverDetourSeconds)
            .ThenByDescending(candidate => candidate.Metrics.MaxWalkingTimeSeconds <= RoutingDefaults.MaxWalkSeconds
                ? candidate.Metrics.SharedStopRate
                : 0)
            .ThenBy(RoutingSolutionScorer.CalculateFairnessScore)
            .ThenBy(RoutingSolutionScorer.CalculatePureCost)
            .First();
    }

    private static CandidateResultDto SelectFairnessTunedExternalCandidate(IReadOnlyList<CandidateResultDto> candidates)
    {
        var feasible = candidates.Where(candidate => candidate.IsFeasible).ToList();
        var pool = feasible.Count > 0 ? feasible : candidates.ToList();
        var bestPureCost = pool.Min(RoutingSolutionScorer.CalculatePureCost);
        var costGuard = bestPureCost * FairnessCostGuardRatio + FairnessCostGuardSlackSeconds;
        var boundedCostPool = pool
            .Where(candidate => RoutingSolutionScorer.CalculatePureCost(candidate) <= costGuard)
            .ToList();

        return boundedCostPool
            .OrderBy(RoutingSolutionScorer.CalculateFairnessScore)
            .ThenBy(candidate => candidate.Metrics.MaxMemberBurdenSeconds)
            .ThenBy(candidate => candidate.Metrics.WorstMemberRegretSeconds)
            .ThenBy(candidate => candidate.Metrics.PassengerBurdenGini)
            .ThenBy(candidate => candidate.Metrics.DriverDetourGini)
            .ThenBy(RoutingSolutionScorer.CalculatePureCost)
            .First();
    }

    private async Task<Dictionary<Guid, List<Member>>?> BuildOrToolsAssignmentAsync(
        Session session,
        Venue venue,
        IRouteCostProvider provider,
        CancellationToken ct)
    {
        var drivers = session.Members.Where(member => member.CanOfferPickup()).ToList();
        var passengers = session.Members.Where(member => member.NeedsPickup()).ToList();
        if (drivers.Count == 0 && passengers.Count > 0)
            return null;

        if (drivers.Sum(driver => driver.GetSeatCapacity()) < passengers.Count)
            return null;

        if (passengers.Count == 0)
            return drivers.ToDictionary(driver => driver.Id, _ => new List<Member>());

        var nodes = new List<Coordinate>();
        nodes.AddRange(drivers.Select(driver => driver.GetLocation()));
        nodes.AddRange(passengers.Select(passenger => passenger.GetLocation()));
        nodes.Add(venue.GetLocation());
        var venueNode = nodes.Count - 1;
        var vehicleMatrices = new List<TravelMatrixResult>();
        foreach (var driver in drivers)
        {
            vehicleMatrices.Add(await provider.GetEstimatedMatrixAsync(nodes, nodes, driver.TransportMode, ct: ct));
        }

        var nodeCount = nodes.Count;
        var vehicleCount = drivers.Count;
        var starts = Enumerable.Range(0, vehicleCount).ToArray();
        var ends = Enumerable.Repeat(venueNode, vehicleCount).ToArray();
        var manager = new RoutingIndexManager(nodeCount, vehicleCount, starts, ends);
        using var routing = new RoutingModel(manager);

        var transitCallbackIndexes = new int[vehicleCount];
        for (var vehicle = 0; vehicle < vehicleCount; vehicle++)
        {
            var vehicleMatrix = vehicleMatrices[vehicle];
            var callbackIndex = routing.RegisterTransitCallback((fromIndex, toIndex) =>
            {
                var fromNode = manager.IndexToNode(fromIndex);
                var toNode = manager.IndexToNode(toIndex);
                return Math.Max(1, (long)Math.Round(vehicleMatrix.Durations[fromNode, toNode]));
            });
            transitCallbackIndexes[vehicle] = callbackIndex;
            routing.SetArcCostEvaluatorOfVehicle(callbackIndex, vehicle);
        }

        var demandCallbackIndex = routing.RegisterUnaryTransitCallback(fromIndex =>
        {
            var node = manager.IndexToNode(fromIndex);
            return node >= drivers.Count && node < drivers.Count + passengers.Count ? 1 : 0;
        });
        routing.AddDimensionWithVehicleCapacity(
            demandCallbackIndex,
            0,
            drivers.Select(driver => (long)driver.GetSeatCapacity()).ToArray(),
            true,
            "Capacity");

        routing.AddDimensionWithVehicleTransits(
            transitCallbackIndexes,
            0,
            24 * 60 * 60,
            true,
            "Time");
        var timeDimension = routing.GetMutableDimension("Time");
        timeDimension.SetGlobalSpanCostCoefficient(1);

        var searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;
        searchParameters.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        searchParameters.TimeLimit = new Duration { Nanos = 150_000_000 };

        var solution = routing.SolveWithParameters(searchParameters);
        if (solution == null)
            return null;

        var assignment = drivers.ToDictionary(driver => driver.Id, _ => new List<Member>());
        for (var vehicle = 0; vehicle < vehicleCount; vehicle++)
        {
            var index = routing.Start(vehicle);
            var driver = drivers[vehicle];
            while (!routing.IsEnd(index))
            {
                var node = manager.IndexToNode(index);
                if (node >= drivers.Count && node < drivers.Count + passengers.Count)
                {
                    assignment[driver.Id].Add(passengers[node - drivers.Count]);
                }

                index = solution.Value(routing.NextVar(index));
            }
        }

        var assignedPassengerCount = assignment.Values.Sum(passengerList => passengerList.Count);
        return assignedPassengerCount == passengers.Count ? assignment : null;
    }

    private async Task<Dictionary<Guid, List<Member>>?> BuildPyvrpAssignmentAsync(
        BenchmarkScenario scenario,
        Venue venue,
        IRouteCostProvider provider,
        CancellationToken ct)
    {
        var drivers = scenario.Session.Members.Where(member => member.CanOfferPickup()).ToList();
        var passengers = scenario.Session.Members.Where(member => member.NeedsPickup()).ToList();
        if (drivers.Count == 0 && passengers.Count > 0)
            return null;

        if (drivers.Sum(driver => driver.GetSeatCapacity()) < passengers.Count)
            return null;

        if (passengers.Count == 0)
            return drivers.ToDictionary(driver => driver.Id, _ => new List<Member>());

        var scriptPath = LocateNativeBenchmarkScript("pyvrp_solve.py");
        if (scriptPath == null)
            return null;

        var nodes = new List<Coordinate>();
        nodes.AddRange(drivers.Select(driver => driver.GetLocation()));
        nodes.AddRange(passengers.Select(passenger => passenger.GetLocation()));
        nodes.Add(venue.GetLocation());
        var venueNode = nodes.Count - 1;
        var vehicleMatrices = new List<TravelMatrixResult>();
        foreach (var driver in drivers)
        {
            vehicleMatrices.Add(await provider.GetEstimatedMatrixAsync(nodes, nodes, driver.TransportMode, ct: ct));
        }

        var workDir = Path.Combine(Path.GetTempPath(), "optigo-native-benchmarks");
        Directory.CreateDirectory(workDir);
        var prefix = $"{scenario.ScenarioId}-{venue.Id}-{Guid.NewGuid():N}";
        var inputPath = Path.Combine(workDir, $"{prefix}-pyvrp-input.json");
        var outputPath = Path.Combine(workDir, $"{prefix}-pyvrp-output.json");

        var payload = new
        {
            scenarioId = scenario.ScenarioId,
            venueId = venue.Id,
            seed = Math.Abs(HashCode.Combine(scenario.ScenarioId, venue.Id)),
            timeLimitSeconds = 0.15,
            serviceSeconds = RoutingDefaults.BasePickupServiceSeconds + RoutingDefaults.BoardingServiceSecondsPerPassenger,
            venueNode,
            nodes = nodes.Select((node, index) => new
            {
                index,
                x = node.Longitude,
                y = node.Latitude
            }),
            drivers = drivers.Select((driver, index) => new
            {
                index,
                node = index,
                capacity = driver.GetSeatCapacity(),
                profile = index,
                transportMode = driver.TransportMode.ToString()
            }),
            passengers = passengers.Select((passenger, index) => new
            {
                index,
                node = drivers.Count + index
            }),
            durationProfiles = vehicleMatrices.Select(matrix => ToJaggedArray(matrix.Durations))
        };

        await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(payload), ct);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var waitForExit = process.WaitForExitAsync(ct);
            var timeout = Task.Delay(TimeSpan.FromSeconds(12), ct);
            var completedTask = await Task.WhenAny(waitForExit, timeout);
            if (completedTask != waitForExit)
            {
                TryKill(process);
                return null;
            }

            if (process.ExitCode != 0 || !File.Exists(outputPath))
                return null;

            var outputJson = await File.ReadAllTextAsync(outputPath, ct);
            var output = JsonSerializer.Deserialize<PyvrpOutputDto>(
                outputJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (output is not { IsFeasible: true })
                return null;

            var assignment = drivers.ToDictionary(driver => driver.Id, _ => new List<Member>());
            foreach (var route in output.Routes)
            {
                if (route.DriverIndex < 0 || route.DriverIndex >= drivers.Count)
                    continue;

                var driver = drivers[route.DriverIndex];
                foreach (var passengerIndex in route.PassengerIndices)
                {
                    if (passengerIndex >= 0 && passengerIndex < passengers.Count)
                    {
                        assignment[driver.Id].Add(passengers[passengerIndex]);
                    }
                }
            }

            var assignedPassengerCount = assignment.Values.Sum(passengerList => passengerList.Count);
            return assignedPassengerCount == passengers.Count ? assignment : null;
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(outputPath);
        }
    }

    private static Dictionary<Guid, List<Member>>? BuildNearestDriverAssignment(Session session)
    {
        var drivers = session.Members.Where(member => member.CanOfferPickup()).ToList();
        var assignment = drivers.ToDictionary(driver => driver.Id, _ => new List<Member>());

        foreach (var passenger in session.Members.Where(member => member.NeedsPickup()))
        {
            var driver = drivers
                .Where(candidate => assignment[candidate.Id].Count < candidate.GetSeatCapacity())
                .OrderBy(candidate => candidate.GetLocation().DistanceTo(passenger.GetLocation()))
                .FirstOrDefault();
            if (driver == null)
                return null;

            assignment[driver.Id].Add(passenger);
        }

        return assignment;
    }

    private async Task<CandidateResultDto> FindBestExactDoorstepCandidateAsync(
        Session session,
        Venue venue,
        IRouteCostProvider provider,
        CancellationToken ct)
    {
        var drivers = session.Members.Where(member => member.CanOfferPickup()).ToList();
        var passengers = session.Members.Where(member => member.NeedsPickup()).ToList();
        if (drivers.Sum(driver => driver.GetSeatCapacity()) < passengers.Count)
        {
            return await BuildInfeasibleCandidateAsync(session, venue, provider, "Không đủ tổng số ghế cho passenger cần pickup.", ct);
        }

        var assignment = drivers.ToDictionary(driver => driver.Id, _ => new List<Member>());
        CandidateResultDto? best = null;
        var exploredStates = 0;

        async Task ExploreAsync(int passengerIndex)
        {
            exploredStates++;
            if (exploredStates > RoutingDefaults.MaxExactAssignmentStates)
                return;

            if (passengerIndex >= passengers.Count)
            {
                var candidate = await EvaluateDoorstepCandidateAsync(session, venue, assignment, provider, ct);
                if (best == null || RoutingSolutionScorer.CalculateTotalCostBaseline(candidate) < RoutingSolutionScorer.CalculateTotalCostBaseline(best))
                {
                    best = candidate;
                }

                return;
            }

            var passenger = passengers[passengerIndex];
            foreach (var driver in drivers
                         .Where(driver => assignment[driver.Id].Count < driver.GetSeatCapacity())
                         .OrderBy(driver => driver.GetLocation().DistanceTo(passenger.GetLocation())))
            {
                assignment[driver.Id].Add(passenger);
                await ExploreAsync(passengerIndex + 1);
                assignment[driver.Id].RemoveAt(assignment[driver.Id].Count - 1);
            }
        }

        await ExploreAsync(0);
        return best ?? await BuildInfeasibleCandidateAsync(session, venue, provider, "Exact baseline hết giới hạn state trước khi tìm được nghiệm.", ct);
    }

    private async Task<CandidateResultDto> EvaluateDoorstepCandidateAsync(
        Session session,
        Venue venue,
        IReadOnlyDictionary<Guid, List<Member>> passengersByDriver,
        IRouteCostProvider provider,
        CancellationToken ct)
    {
        var memberRoutes = new List<MemberRouteDto>();
        var driverRoutes = new List<DriverRouteDto>();
        var breakdown = new RouteScoreBreakdownDto();
        var assignedPassengerIds = passengersByDriver.Values
            .SelectMany(passengers => passengers.Select(passenger => passenger.Id))
            .ToHashSet();

        foreach (var driver in session.Members.Where(member => member.CanOfferPickup()))
        {
            passengersByDriver.TryGetValue(driver.Id, out var passengers);
            passengers ??= [];
            var result = await BuildDoorstepDriverResultAsync(driver, passengers, venue, provider, ct);
            driverRoutes.Add(result.DriverRoute);
            memberRoutes.Add(new MemberRouteDto
            {
                MemberId = driver.Id,
                MemberName = driver.Name,
                EstimatedTimeSeconds = result.DriverRoute.TotalTimeSeconds,
                DistanceMeters = result.DriverRoute.TotalDistanceMeters,
                RideDistanceMeters = result.DriverRoute.TotalDistanceMeters,
                RideTimeSeconds = result.DriverRoute.TotalTimeSeconds,
                DriverId = driver.Id,
                BurdenScore = result.DriverRoute.GeneralizedCostSeconds
            });
            memberRoutes.AddRange(result.PassengerRoutes);
            AddBreakdown(breakdown, result.CostBreakdown);
        }

        foreach (var member in session.Members.Where(member => !member.CanOfferPickup() && !assignedPassengerIds.Contains(member.Id)))
        {
            var route = await provider.GetExactRouteAsync(member.GetLocation(), venue.GetLocation(), member.TransportMode, ct: ct);
            memberRoutes.Add(new MemberRouteDto
            {
                MemberId = member.Id,
                MemberName = member.Name,
                EstimatedTimeSeconds = route.DurationSeconds,
                DistanceMeters = route.DistanceMeters,
                RideDistanceMeters = route.DistanceMeters,
                RideTimeSeconds = route.DurationSeconds,
                DriverId = null,
                BurdenScore = route.DurationSeconds
            });
            breakdown.GeneralizedCostSeconds += route.DurationSeconds;
            breakdown.TotalDriveSeconds += route.DurationSeconds;
        }

        breakdown.VenueQualityBonusSeconds = RoutingSolutionScorer.CalculateVenueQualityBonusSeconds(venue);
        breakdown.GeneralizedCostSeconds = Math.Max(0, breakdown.GeneralizedCostSeconds - breakdown.VenueQualityBonusSeconds);
        var metrics = RoutingSolutionScorer.BuildMetrics(venue, memberRoutes, driverRoutes);
        var issues = RoutingSolutionScorer.ValidateSolution(session, memberRoutes, driverRoutes, metrics);
        var unassignedPickupCount = session.Members.Count(member => member.NeedsPickup() && !assignedPassengerIds.Contains(member.Id));
        if (unassignedPickupCount > 0)
        {
            issues.Add($"{unassignedPickupCount} passenger cần pickup không được gán driver.");
        }

        if (issues.Count > 0)
        {
            breakdown.GeneralizedCostSeconds += issues.Count * RoutingDefaults.FeasibilityIssuePenaltySeconds;
        }

        return new CandidateResultDto
        {
            VenueId = venue.Id,
            Name = venue.Name,
            Category = venue.Category,
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            Rating = venue.Rating,
            ReviewCount = venue.ReviewCount,
            TotalTimeSeconds = metrics.TotalGroupTimeSeconds,
            MaxDriverDetourSeconds = metrics.MaxDriverDetourSeconds,
            TotalWalkingDistanceMeters = memberRoutes.Sum(route => route.WalkingDistanceMeters),
            IsFeasible = issues.Count == 0,
            FeasibilityIssues = issues.Distinct(StringComparer.Ordinal).ToList(),
            Metrics = metrics,
            ScoreBreakdown = breakdown,
            MemberRoutes = memberRoutes,
            DriverRoutes = driverRoutes
        };
    }

    private async Task<DriverOptimizationResult> BuildDoorstepDriverResultAsync(
        Member driver,
        IReadOnlyList<Member> passengers,
        Venue venue,
        IRouteCostProvider provider,
        CancellationToken ct)
    {
        var origin = driver.GetLocation();
        var destination = venue.GetLocation();
        var direct = await provider.GetExactRouteAsync(origin, destination, driver.TransportMode, ct: ct);
        var orderedPassengers = await SolveDoorstepOrderAsync(driver, passengers, venue, provider, ct);
        var stops = new List<RouteStopDto>
        {
            new()
            {
                Sequence = 0,
                StopType = "driver_origin",
                Label = driver.Name,
                Latitude = driver.Latitude,
                Longitude = driver.Longitude,
                StopAccessType = "origin"
            }
        };
        var snapshots = new Dictionary<Guid, (double Eta, double Distance, double Wait)>();
        var current = origin;
        var elapsed = 0.0;
        var distance = 0.0;
        var waitTotal = 0.0;

        foreach (var passenger in orderedPassengers)
        {
            var route = await provider.GetExactRouteAsync(current, passenger.GetLocation(), driver.TransportMode, ct: ct);
            elapsed += route.DurationSeconds;
            distance += route.DistanceMeters;
            current = passenger.GetLocation();
            var wait = EstimateBenchmarkWaitSeconds(elapsed);
            waitTotal += wait;
            snapshots[passenger.Id] = (elapsed, distance, wait);
            stops.Add(new RouteStopDto
            {
                Sequence = stops.Count,
                StopType = "pickup",
                Label = passenger.Name,
                Latitude = passenger.Latitude,
                Longitude = passenger.Longitude,
                EtaSeconds = elapsed,
                DistanceFromPreviousMeters = route.DistanceMeters,
                CumulativeDistanceMeters = distance,
                CumulativeTimeSeconds = elapsed,
                WaitSeconds = wait,
                ServiceTimeSeconds = RoutingDefaults.BasePickupServiceSeconds + RoutingDefaults.BoardingServiceSecondsPerPassenger,
                StopAccessType = "doorstep",
                PassengerIds = [passenger.Id]
            });
            elapsed += RoutingDefaults.BasePickupServiceSeconds + RoutingDefaults.BoardingServiceSecondsPerPassenger;
        }

        var finalLeg = await provider.GetExactRouteAsync(current, destination, driver.TransportMode, ct: ct);
        elapsed += finalLeg.DurationSeconds;
        distance += finalLeg.DistanceMeters;
        stops.Add(new RouteStopDto
        {
            Sequence = stops.Count,
            StopType = "destination",
            Label = venue.Name,
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            EtaSeconds = elapsed,
            DistanceFromPreviousMeters = finalLeg.DistanceMeters,
            CumulativeDistanceMeters = distance,
            CumulativeTimeSeconds = elapsed,
            StopAccessType = "destination"
        });

        var passengerRoutes = orderedPassengers.Select(passenger =>
        {
            var snapshot = snapshots[passenger.Id];
            var rideTime = Math.Max(0, elapsed - snapshot.Eta);
            var rideDistance = Math.Max(0, distance - snapshot.Distance);
            return new MemberRouteDto
            {
                MemberId = passenger.Id,
                MemberName = passenger.Name,
                EstimatedTimeSeconds = rideTime + snapshot.Wait,
                DistanceMeters = rideDistance,
                RideDistanceMeters = rideDistance,
                RideTimeSeconds = rideTime,
                WaitTimeSeconds = snapshot.Wait,
                DriverId = driver.Id,
                BurdenScore = rideTime + snapshot.Wait * RoutingDefaults.WaitWeight
            };
        }).ToList();
        var detour = Math.Max(0, elapsed - direct.DurationSeconds);
        var fairness = ComputeStd(passengerRoutes.Select(route => route.BurdenScore).ToList()) * RoutingDefaults.FairnessWeight;
        var generalizedCost =
            elapsed +
            waitTotal * RoutingDefaults.WaitWeight +
            detour * RoutingDefaults.DetourWeight +
            fairness +
            orderedPassengers.Count * RoutingDefaults.StopComplexityWeight;

        return new DriverOptimizationResult
        {
            DriverRoute = new DriverRouteDto
            {
                DriverId = driver.Id,
                DriverName = driver.Name,
                TotalTimeSeconds = elapsed,
                TotalDistanceMeters = distance,
                DirectTimeSeconds = direct.DurationSeconds,
                DirectDistanceMeters = direct.DistanceMeters,
                GeneralizedCostSeconds = generalizedCost,
                PassengerIds = orderedPassengers.Select(passenger => passenger.Id).ToList(),
                Stops = stops,
                RoutePolyline = [new RoutePointDto { Latitude = origin.Latitude, Longitude = origin.Longitude }, new RoutePointDto { Latitude = destination.Latitude, Longitude = destination.Longitude }]
            },
            PassengerRoutes = passengerRoutes,
            CostBreakdown = new RouteScoreBreakdownDto
            {
                GeneralizedCostSeconds = generalizedCost,
                TotalDriveSeconds = elapsed,
                TotalWaitSeconds = waitTotal,
                DetourPenaltySeconds = detour * RoutingDefaults.DetourWeight,
                FairnessPenaltySeconds = fairness,
                StopComplexityPenaltySeconds = orderedPassengers.Count * RoutingDefaults.StopComplexityWeight
            }
        };
    }

    private async Task<IReadOnlyList<Member>> SolveDoorstepOrderAsync(
        Member driver,
        IReadOnlyList<Member> passengers,
        Venue venue,
        IRouteCostProvider provider,
        CancellationToken ct)
    {
        if (passengers.Count <= 1)
            return passengers.ToList();

        var n = passengers.Count;
        var nodes = new List<Coordinate> { driver.GetLocation() };
        nodes.AddRange(passengers.Select(passenger => passenger.GetLocation()));
        nodes.Add(venue.GetLocation());
        var matrix = await provider.GetEstimatedMatrixAsync(nodes, nodes, driver.TransportMode, ct: ct);
        var stateCount = 1 << n;
        var dp = new double[stateCount, n];
        var parent = new int[stateCount, n];
        for (var mask = 0; mask < stateCount; mask++)
        {
            for (var last = 0; last < n; last++)
            {
                dp[mask, last] = double.PositiveInfinity;
                parent[mask, last] = -1;
            }
        }

        var serviceSeconds = RoutingDefaults.BasePickupServiceSeconds + RoutingDefaults.BoardingServiceSecondsPerPassenger;
        for (var i = 0; i < n; i++)
        {
            dp[1 << i, i] = matrix.Durations[0, i + 1] + serviceSeconds;
        }

        for (var mask = 1; mask < stateCount; mask++)
        {
            for (var last = 0; last < n; last++)
            {
                if ((mask & (1 << last)) == 0 || double.IsPositiveInfinity(dp[mask, last]))
                    continue;

                for (var next = 0; next < n; next++)
                {
                    if ((mask & (1 << next)) != 0)
                        continue;

                    var nextMask = mask | (1 << next);
                    var proposal = dp[mask, last] + matrix.Durations[last + 1, next + 1] + serviceSeconds;
                    if (proposal < dp[nextMask, next])
                    {
                        dp[nextMask, next] = proposal;
                        parent[nextMask, next] = last;
                    }
                }
            }
        }

        var fullMask = stateCount - 1;
        var venueIndex = n + 1;
        var bestLast = 0;
        var best = double.PositiveInfinity;
        for (var last = 0; last < n; last++)
        {
            var total = dp[fullMask, last] + matrix.Durations[last + 1, venueIndex];
            if (total < best)
            {
                best = total;
                bestLast = last;
            }
        }

        var order = new List<Member>();
        var currentMask = fullMask;
        var currentLast = bestLast;
        while (currentLast >= 0)
        {
            order.Add(passengers[currentLast]);
            var previous = parent[currentMask, currentLast];
            currentMask &= ~(1 << currentLast);
            currentLast = previous;
        }

        order.Reverse();
        return order;
    }

    private async Task<double> EstimateDoorstepDriverDurationAsync(
        Member driver,
        IReadOnlyList<Member> passengers,
        Venue venue,
        IRouteCostProvider provider,
        CancellationToken ct)
    {
        var order = await SolveDoorstepOrderAsync(driver, passengers, venue, provider, ct);
        var current = driver.GetLocation();
        var elapsed = 0.0;
        foreach (var passenger in order)
        {
            var route = await provider.GetExactRouteAsync(current, passenger.GetLocation(), driver.TransportMode, ct: ct);
            elapsed += route.DurationSeconds + RoutingDefaults.BasePickupServiceSeconds + RoutingDefaults.BoardingServiceSecondsPerPassenger;
            current = passenger.GetLocation();
        }

        var final = await provider.GetExactRouteAsync(current, venue.GetLocation(), driver.TransportMode, ct: ct);
        return elapsed + final.DurationSeconds;
    }

    private async Task<CandidateResultDto> BuildInfeasibleCandidateAsync(
        Session session,
        Venue venue,
        IRouteCostProvider provider,
        string reason,
        CancellationToken ct)
    {
        var emptyAssignment = session.Members
            .Where(member => member.CanOfferPickup())
            .ToDictionary(driver => driver.Id, _ => new List<Member>());
        var candidate = await EvaluateDoorstepCandidateAsync(session, venue, emptyAssignment, provider, ct);
        candidate.IsFeasible = false;
        candidate.FeasibilityIssues.Add(reason);
        candidate.ScoreBreakdown.GeneralizedCostSeconds += RoutingDefaults.FeasibilityIssuePenaltySeconds;
        return candidate;
    }

    private static BenchmarkAlgorithmRunDto ToRun(
        string algorithmKey,
        string algorithmName,
        bool isOptiGo,
        CandidateResultDto candidate,
        double computeTimeMs) =>
        new()
        {
            AlgorithmKey = algorithmKey,
            AlgorithmName = algorithmName,
            IsOptiGo = isOptiGo,
            SelectedVenueId = candidate.VenueId,
            SelectedVenueName = candidate.Name,
            IsFeasible = candidate.IsFeasible,
            FeasibilityIssues = candidate.FeasibilityIssues,
            ObjectiveSeconds = RoutingSolutionScorer.CalculateCompositeCost(candidate),
            PureCostSeconds = RoutingSolutionScorer.CalculatePureCost(candidate),
            FairnessScoreSeconds = RoutingSolutionScorer.CalculateFairnessScore(candidate),
            TotalGroupTimeSeconds = candidate.Metrics.TotalGroupTimeSeconds,
            MaxPassengerTimeSeconds = candidate.Metrics.MaxPassengerTimeSeconds,
            StdPassengerTimeSeconds = candidate.Metrics.StdPassengerTimeSeconds,
            MaxMemberBurdenSeconds = candidate.Metrics.MaxMemberBurdenSeconds,
            WorstMemberRegretSeconds = candidate.Metrics.WorstMemberRegretSeconds,
            PassengerBurdenGini = candidate.Metrics.PassengerBurdenGini,
            MaxDriverDetourSeconds = candidate.Metrics.MaxDriverDetourSeconds,
            StdDriverDetourSeconds = candidate.Metrics.StdDriverDetourSeconds,
            DriverDetourGini = candidate.Metrics.DriverDetourGini,
            TotalDriverDetourSeconds = candidate.Metrics.TotalDriverDetourSeconds,
            MaxWalkingTimeSeconds = candidate.Metrics.MaxWalkingTimeSeconds,
            TotalWalkingTimeSeconds = candidate.Metrics.TotalWalkingTimeSeconds,
            SharedStopRate = candidate.Metrics.SharedStopRate,
            StopCount = candidate.Metrics.StopCount,
            SharedStopCount = candidate.Metrics.SharedStopCount,
            ComputeTimeMs = computeTimeMs
        };

    private static void ApplyExternalGaps(IReadOnlyList<BenchmarkAlgorithmRunDto> runs)
    {
        var bestExternal = runs
            .Where(run => !run.IsOptiGo && run.IsFeasible)
            .OrderBy(run => run.ObjectiveSeconds)
            .FirstOrDefault();
        var bestCostExternal = runs
            .Where(run => !run.IsOptiGo && run.IsFeasible)
            .OrderBy(run => run.PureCostSeconds)
            .FirstOrDefault();

        foreach (var run in runs)
        {
            if (bestExternal != null)
            {
                run.GapToBestExternalPercent = bestExternal.ObjectiveSeconds <= 0
                    ? 0
                    : (run.ObjectiveSeconds - bestExternal.ObjectiveSeconds) / bestExternal.ObjectiveSeconds * 100;
            }

            if (bestCostExternal != null)
            {
                run.CostGapToBestExternalPercent = bestCostExternal.PureCostSeconds <= 0
                    ? 0
                    : (run.PureCostSeconds - bestCostExternal.PureCostSeconds) / bestCostExternal.PureCostSeconds * 100;
                run.FairnessGainVsBestCostExternalPercent = bestCostExternal.FairnessScoreSeconds <= 0
                    ? 0
                    : (bestCostExternal.FairnessScoreSeconds - run.FairnessScoreSeconds) / bestCostExternal.FairnessScoreSeconds * 100;
            }
        }
    }

    private static BenchmarkScenarioResultDto ToScenarioResult(
        BenchmarkScenario scenario,
        List<BenchmarkAlgorithmRunDto> runs)
    {
        var serviceability = GetServiceability(scenario);
        return new BenchmarkScenarioResultDto
        {
            ScenarioId = scenario.ScenarioId,
            Layout = scenario.Layout,
            MemberCount = scenario.Session.Members.Count,
            DriverCount = scenario.Session.Members.Count(member => member.CanOfferPickup()),
            PickupPassengerCount = scenario.Session.Members.Count(member => member.NeedsPickup()),
            VenueCount = scenario.Venues.Count,
            IsServiceable = serviceability.IsServiceable,
            UnserviceableReason = serviceability.Reason,
            Description = scenario.Description,
            Members = scenario.Session.Members.Select(member => new BenchmarkMemberDto
            {
                Name = member.Name,
                Role = member.NeedsPickup() ? "NeedsPickup" : member.CanOfferPickup() ? "Driver" : "SelfTravel",
                TransportMode = member.TransportMode.ToString(),
                Latitude = member.Latitude,
                Longitude = member.Longitude,
                SeatCapacity = member.GetSeatCapacity()
            }).ToList(),
            Venues = scenario.Venues.Select(venue => new BenchmarkVenueDto
            {
                VenueId = venue.Id,
                Name = venue.Name,
                Latitude = venue.Latitude,
                Longitude = venue.Longitude,
                Rating = venue.Rating
            }).ToList(),
            Runs = runs
        };
    }

    private static bool IsServiceable(BenchmarkScenario scenario) =>
        GetServiceability(scenario).IsServiceable;

    private static (bool IsServiceable, string? Reason) GetServiceability(BenchmarkScenario scenario)
    {
        var totalSeats = scenario.Session.Members
            .Where(member => member.CanOfferPickup())
            .Sum(member => member.GetSeatCapacity());
        var pickupPassengers = scenario.Session.Members.Count(member => member.NeedsPickup());

        if (totalSeats < pickupPassengers)
        {
            return (false, $"Không đủ ghế: {totalSeats} ghế cho {pickupPassengers} passenger cần pickup.");
        }

        return (true, null);
    }

    private static List<BenchmarkAlgorithmAggregateDto> BuildAggregates(
        IReadOnlyList<BenchmarkAlgorithmRunDto> runs)
    {
        var bestByScenario = runs
            .GroupBy(run => run.ScenarioId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(run => run.IsFeasible ? 0 : 1)
                    .ThenBy(run => run.ObjectiveSeconds)
                    .First());

        return runs
            .GroupBy(run => new { run.AlgorithmKey, run.AlgorithmName, run.IsOptiGo })
            .Select(group =>
            {
                var groupRuns = group.ToList();
                var serviceableRuns = groupRuns.Where(run => run.IsScenarioServiceable).ToList();
                var metricRuns = serviceableRuns.Count > 0 ? serviceableRuns : groupRuns;
                var wins = metricRuns.Count(run =>
                    bestByScenario.TryGetValue(run.ScenarioId, out var best) &&
                    IsTiedWithBest(run, best));

                return new BenchmarkAlgorithmAggregateDto
                {
                    AlgorithmKey = group.Key.AlgorithmKey,
                    AlgorithmName = group.Key.AlgorithmName,
                    IsOptiGo = group.Key.IsOptiGo,
                    Runs = groupRuns.Count,
                    ServiceableRuns = serviceableRuns.Count,
                    FeasibleRate = groupRuns.Count == 0 ? 0 : groupRuns.Count(run => run.IsFeasible) / (double)groupRuns.Count,
                    ServiceableFeasibleRate = serviceableRuns.Count == 0
                        ? 0
                        : serviceableRuns.Count(run => run.IsFeasible) / (double)serviceableRuns.Count,
                    WinRate = metricRuns.Count == 0 ? 0 : wins / (double)metricRuns.Count,
                    AverageObjectiveSeconds = metricRuns.Average(run => run.ObjectiveSeconds),
                    AveragePureCostSeconds = metricRuns.Average(run => run.PureCostSeconds),
                    AverageFairnessScoreSeconds = metricRuns.Average(run => run.FairnessScoreSeconds),
                    AverageCostGapToBestExternalPercent = metricRuns.Average(run => run.CostGapToBestExternalPercent),
                    AverageFairnessGainVsBestCostExternalPercent = metricRuns.Average(run => run.FairnessGainVsBestCostExternalPercent),
                    AverageTotalGroupTimeSeconds = metricRuns.Average(run => run.TotalGroupTimeSeconds),
                    AverageMaxPassengerTimeSeconds = metricRuns.Average(run => run.MaxPassengerTimeSeconds),
                    AverageMaxMemberBurdenSeconds = metricRuns.Average(run => run.MaxMemberBurdenSeconds),
                    AverageWorstMemberRegretSeconds = metricRuns.Average(run => run.WorstMemberRegretSeconds),
                    AveragePassengerBurdenGini = metricRuns.Average(run => run.PassengerBurdenGini),
                    AverageMaxDriverDetourSeconds = metricRuns.Average(run => run.MaxDriverDetourSeconds),
                    AverageStdDriverDetourSeconds = metricRuns.Average(run => run.StdDriverDetourSeconds),
                    AverageDriverDetourGini = metricRuns.Average(run => run.DriverDetourGini),
                    AverageMaxWalkingTimeSeconds = metricRuns.Average(run => run.MaxWalkingTimeSeconds),
                    AverageSharedStopRate = metricRuns.Average(run => run.SharedStopRate),
                    AverageStopCount = metricRuns.Average(run => run.StopCount),
                    AverageComputeTimeMs = metricRuns.Average(run => run.ComputeTimeMs)
                };
            })
            .OrderBy(aggregate => aggregate.IsOptiGo ? 0 : 1)
            .ThenBy(aggregate => aggregate.AverageObjectiveSeconds)
            .ToList();
    }

    private static bool IsTiedWithBest(
        BenchmarkAlgorithmRunDto run,
        BenchmarkAlgorithmRunDto best)
    {
        if (best.IsFeasible && !run.IsFeasible)
            return false;

        var toleranceSeconds = Math.Max(1, Math.Abs(best.ObjectiveSeconds) * 0.01);
        return run.ObjectiveSeconds <= best.ObjectiveSeconds + toleranceSeconds;
    }

    private static List<BenchmarkWeaknessDto> BuildWeaknesses(
        IReadOnlyList<BenchmarkScenarioResultDto> scenarios)
    {
        var weaknesses = new List<BenchmarkWeaknessDto>();
        foreach (var scenario in scenarios)
        {
            var optigo = scenario.Runs.First(run => run.IsOptiGo);
            var externalRuns = scenario.Runs.Where(run => !run.IsOptiGo && run.IsFeasible).ToList();
            if (externalRuns.Count == 0)
                continue;

            AddWeaknessIfWorse(weaknesses, scenario, optigo, externalRuns, "objective", run => run.ObjectiveSeconds, 0.08, "Composite objective thua baseline ngoài.");
            AddWeaknessIfWorse(weaknesses, scenario, optigo, externalRuns, "maxPassenger", run => run.MaxPassengerTimeSeconds, 0.10, "Passenger tệ nhất mất thời gian lâu hơn.");
            AddWeaknessIfWorse(weaknesses, scenario, optigo, externalRuns, "maxMemberBurden", run => run.MaxMemberBurdenSeconds, 0.08, "Member chịu burden cao nhất tệ hơn baseline ngoài.");
            AddWeaknessIfWorse(weaknesses, scenario, optigo, externalRuns, "worstRegret", run => run.WorstMemberRegretSeconds, 0.10, "Worst-member regret cao hơn baseline ngoài.");
            AddWeaknessIfWorse(weaknesses, scenario, optigo, externalRuns, "driverDetour", run => run.MaxDriverDetourSeconds, 0.12, "Detour tài xế cao hơn.");
            AddWeaknessIfWorse(weaknesses, scenario, optigo, externalRuns, "driverStd", run => run.StdDriverDetourSeconds, 0.15, "Độ lệch detour giữa các tài xế cao hơn.", 20);
            AddWeaknessIfWorse(weaknesses, scenario, optigo, externalRuns, "driverGini", run => run.DriverDetourGini, 0.18, "Gini detour tài xế cao hơn.", 0.03);
            AddWeaknessIfWorse(weaknesses, scenario, optigo, externalRuns, "walking", run => run.MaxWalkingTimeSeconds, 0.20, "Đi bộ tối đa cao hơn đáng kể.");
            if (scenario.Layout == "shared_cluster")
            {
                AddWeaknessIfLower(weaknesses, scenario, optigo, externalRuns, "sharedStopRate", run => run.SharedStopRate, 0.20, "Shared-stop rate thấp hơn trong case passenger theo cụm.", 0.10);
                AddWeaknessIfWorse(weaknesses, scenario, optigo, externalRuns, "stopCount", run => run.StopCount, 0.20, "Số pickup stop nhiều hơn trong shared-cluster.", 1);
            }

            if (!optigo.IsFeasible)
            {
                var best = externalRuns.OrderBy(run => run.ObjectiveSeconds).First();
                weaknesses.Add(new BenchmarkWeaknessDto
                {
                    ScenarioId = scenario.ScenarioId,
                    Layout = scenario.Layout,
                    Metric = "feasibility",
                    Message = "OptiGo infeasible trong khi baseline ngoài có nghiệm feasible.",
                    OptiGoValue = 0,
                    BestExternalValue = 1,
                    BestExternalAlgorithm = best.AlgorithmName,
                    GapPercent = 100
                });
            }
        }

        return weaknesses
            .OrderByDescending(weakness => weakness.GapPercent)
            .Take(18)
            .ToList();
    }

    private static void AddWeaknessIfWorse(
        ICollection<BenchmarkWeaknessDto> weaknesses,
        BenchmarkScenarioResultDto scenario,
        BenchmarkAlgorithmRunDto optigo,
        IReadOnlyList<BenchmarkAlgorithmRunDto> externalRuns,
        string metric,
        Func<BenchmarkAlgorithmRunDto, double> selector,
        double threshold,
        string message,
        double minimumDelta = 30)
    {
        var optigoValue = selector(optigo);
        var best = externalRuns
            .OrderBy(selector)
            .First();
        var bestValue = selector(best);
        if (bestValue <= 0)
            return;

        var gap = (optigoValue - bestValue) / bestValue;
        if (gap < threshold || optigoValue - bestValue < minimumDelta)
            return;

        weaknesses.Add(new BenchmarkWeaknessDto
        {
            ScenarioId = scenario.ScenarioId,
            Layout = scenario.Layout,
            Metric = metric,
            Message = message,
            OptiGoValue = optigoValue,
            BestExternalValue = bestValue,
            BestExternalAlgorithm = best.AlgorithmName,
            GapPercent = gap * 100
        });
    }

    private static void AddWeaknessIfLower(
        ICollection<BenchmarkWeaknessDto> weaknesses,
        BenchmarkScenarioResultDto scenario,
        BenchmarkAlgorithmRunDto optigo,
        IReadOnlyList<BenchmarkAlgorithmRunDto> externalRuns,
        string metric,
        Func<BenchmarkAlgorithmRunDto, double> selector,
        double threshold,
        string message,
        double minimumDelta)
    {
        var optigoValue = selector(optigo);
        var best = externalRuns
            .OrderByDescending(selector)
            .First();
        var bestValue = selector(best);
        if (bestValue <= 0)
            return;

        var gap = (bestValue - optigoValue) / bestValue;
        if (gap < threshold || bestValue - optigoValue < minimumDelta)
            return;

        weaknesses.Add(new BenchmarkWeaknessDto
        {
            ScenarioId = scenario.ScenarioId,
            Layout = scenario.Layout,
            Metric = metric,
            Message = message,
            OptiGoValue = optigoValue,
            BestExternalValue = bestValue,
            BestExternalAlgorithm = best.AlgorithmName,
            GapPercent = gap * 100
        });
    }

    private static List<BenchmarkSourceDto> BuildSources() =>
    [
        new()
        {
            Label = "Google OR-Tools vehicle routing",
            Url = "https://developers.google.com/optimization/routing",
            Relevance = "Chuẩn baseline VRP/CVRP/PDP: capacity, pickup-delivery, dimensions, local search/metaheuristics."
        },
        new()
        {
            Label = "PyVRP Hybrid Genetic Search native solver",
            Url = "https://github.com/PyVRP/PyVRP",
            Relevance = "Native HGS baseline được gọi qua Python bridge; output route được normalize về evaluator OptiGo."
        },
        new()
        {
            Label = "VROOM open-source routing engine",
            Url = "https://github.com/VROOM-Project/vroom",
            Relevance = "Đã pull source để tích hợp native sau; không còn được report như external result nếu chưa chạy binary thật."
        },
        new()
        {
            Label = "jsprit vehicle routing toolkit",
            Url = "https://github.com/graphhopper/jsprit",
            Relevance = "Đã pull source để tích hợp Java native sau; không còn được report như external result nếu chưa chạy jsprit thật."
        },
        new()
        {
            Label = "OR-Tools routing solver paper",
            Url = "https://research.google/pubs/or-tools-vehicle-routing-solver-a-generic-constraint-programming-solver-with-heuristic-search-for-routing-problems/",
            Relevance = "Mô tả kiến trúc first-solution heuristics, local search, metaheuristics và constraint programming."
        },
        new()
        {
            Label = "Hybrid Genetic Search + SWAP* for CVRP",
            Url = "https://arxiv.org/abs/2012.10384",
            Relevance = "Gợi ý cross-route exchange/SWAP* và route-pool như hướng nâng cấp cho nhóm nhỏ."
        },
        new()
        {
            Label = "Adaptive Hybrid Genetic + Large Neighborhood Search",
            Url = "https://arxiv.org/abs/2402.18903",
            Relevance = "Cơ sở cho adaptive destroy/repair, ổn định hội tụ trên VRP đa thuộc tính."
        },
        new()
        {
            Label = "Optimal route and stops for group users",
            Url = "https://doi.org/10.1145/3139958.3140061",
            Relevance = "Bài toán collective travel planning: chọn route và stop cho nhóm người dùng trên road network."
        }
    ];

    private static void AddBreakdown(RouteScoreBreakdownDto aggregate, RouteScoreBreakdownDto current)
    {
        aggregate.GeneralizedCostSeconds += current.GeneralizedCostSeconds;
        aggregate.TotalDriveSeconds += current.TotalDriveSeconds;
        aggregate.TotalWalkSeconds += current.TotalWalkSeconds;
        aggregate.TotalWaitSeconds += current.TotalWaitSeconds;
        aggregate.DetourPenaltySeconds += current.DetourPenaltySeconds;
        aggregate.FairnessPenaltySeconds += current.FairnessPenaltySeconds;
        aggregate.StopComplexityPenaltySeconds += current.StopComplexityPenaltySeconds;
        aggregate.RiskPenaltySeconds += current.RiskPenaltySeconds;
        aggregate.StabilityPenaltySeconds += current.StabilityPenaltySeconds;
    }

    private static double ComputeStd(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        var average = values.Average();
        return Math.Sqrt(values.Sum(value => Math.Pow(value - average, 2)) / values.Count);
    }

    private static double EstimateBenchmarkWaitSeconds(double pickupEtaSeconds) =>
        Math.Min(120, RoutingDefaults.SyncBufferSeconds + pickupEtaSeconds * RoutingDefaults.WaitEtaFactor);

    private static double RandomRange(Random random, double min, double max) =>
        min + random.NextDouble() * (max - min);

    private static Coordinate Offset(Coordinate center, double latitudeDelta, double longitudeDelta) =>
        new(center.Latitude + latitudeDelta, center.Longitude + longitudeDelta);

    private static double[][] ToJaggedArray(double[,] matrix)
    {
        var rows = matrix.GetLength(0);
        var columns = matrix.GetLength(1);
        var result = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            result[row] = new double[columns];
            for (var column = 0; column < columns; column++)
            {
                result[row][column] = matrix[row, column];
            }
        }

        return result;
    }

    private static string? LocateNativeBenchmarkScript(string fileName)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "benchmarks", "native", fileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup for benchmark subprocesses.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for temporary benchmark files.
        }
    }

    private sealed class PyvrpOutputDto
    {
        public bool IsFeasible { get; init; }
        public List<PyvrpRouteDto> Routes { get; init; } = [];
    }

    private sealed class PyvrpRouteDto
    {
        public int DriverIndex { get; init; }
        public List<int> PassengerIndices { get; init; } = [];
    }

    private sealed record BenchmarkScenario(
        string ScenarioId,
        string Layout,
        Session Session,
        IReadOnlyList<Venue> Venues,
        string Description);

    private sealed class BenchmarkTrafficSnapshotProvider : ITrafficSnapshotProvider
    {
        private readonly string _layout;

        public BenchmarkTrafficSnapshotProvider(string layout)
        {
            _layout = layout;
        }

        public TrafficSnapshot GetCurrentSnapshot() =>
            new($"benchmark-{_layout}", _layout is "spread" or "outlier" ? 1.12 : 1.0, false);
    }

    private sealed class BenchmarkRouteCostProvider : IRouteCostProvider
    {
        private readonly double _routeFactor;

        public BenchmarkRouteCostProvider(string layout)
        {
            _routeFactor = layout switch
            {
                "corridor" => 1.08,
                "spread" => 1.22,
                "outlier" => 1.18,
                _ => 1.14
            };
        }

        public Task<RouteResult> GetExactRouteAsync(
            Coordinate origin,
            Coordinate destination,
            TransportMode mode,
            RouteCostContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var distance = origin.DistanceTo(destination) * _routeFactor;
            var congestion = context?.PreferTrafficAware == true ? 1.08 : 1.0;
            return Task.FromResult(new RouteResult
            {
                DistanceMeters = distance,
                DurationSeconds = distance / GetSpeedMetersPerSecond(mode) * congestion,
                Geometry = [origin, destination]
            });
        }

        public Task<TravelMatrixResult> GetEstimatedMatrixAsync(
            IReadOnlyList<Coordinate> origins,
            IReadOnlyList<Coordinate> destinations,
            TransportMode mode,
            RouteCostContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var durations = new double[origins.Count, destinations.Count];
            var distances = new double[origins.Count, destinations.Count];
            var congestion = context?.PreferTrafficAware == true ? 1.08 : 1.0;
            for (var i = 0; i < origins.Count; i++)
            {
                for (var j = 0; j < destinations.Count; j++)
                {
                    distances[i, j] = origins[i].DistanceTo(destinations[j]) * _routeFactor;
                    durations[i, j] = distances[i, j] / GetSpeedMetersPerSecond(mode) * congestion;
                }
            }

            return Task.FromResult(new TravelMatrixResult
            {
                Durations = durations,
                Distances = distances
            });
        }

        private static double GetSpeedMetersPerSecond(TransportMode mode) => mode switch
        {
            TransportMode.Walking => 1.25,
            TransportMode.Cycling => 4.2,
            TransportMode.Bus => 6.0,
            TransportMode.Motorbike => 8.5,
            TransportMode.Car => 7.6,
            _ => 7.6
        };
    }
}
