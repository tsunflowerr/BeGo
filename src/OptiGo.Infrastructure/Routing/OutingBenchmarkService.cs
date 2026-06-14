using System.Diagnostics;
using System.Globalization;
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
    private const string SyntheticBenchmarkMode = "synthetic";
    private const string PublicAllBenchmarkMode = "public-all";
    private const string DarpBenchmarkMode = "darp-mp";
    private const string LiLimBenchmarkMode = "li-lim-pdptw";
    private const double PublicCoordinateScaleDegrees = 0.0009;
    private static readonly Coordinate PublicCoordinateAnchor = new(10.7769, 106.7009);
    private static readonly Lazy<string> BenchmarkPythonExecutable = new(ResolvePythonExecutableCore);

    private static readonly string[] Layouts =
    [
        "clustered",
        "spread",
        "corridor",
        "outlier",
        "capacity_tight",
        "shared_cluster",
        "uniform_grid"
    ];

    private static readonly (string Name, Coordinate Center, string Description)[] VietnamRegions =
    [
        ("Quận 1", new Coordinate(10.7756, 106.7019), "Trung tâm TP.HCM, nhiều quán café."),
        ("Quận 3", new Coordinate(10.7844, 106.6887), "Khu dân cư + thương mại."),
        ("Quận 7 (PMH)", new Coordinate(10.7293, 106.7218), "Khu đô thị mới, đường rộng."),
        ("Thủ Đức", new Coordinate(10.8497, 106.7697), "Khu ĐHQG, xa trung tâm."),
        ("Bình Thạnh", new Coordinate(10.8046, 106.7109), "Giáp ranh Q1, tắc đường."),
        ("Tân Bình", new Coordinate(10.8022, 106.6527), "Gần sân bay, đa dạng đường.")
    ];

    public async Task<OutingBenchmarkReportDto> RunAsync(
        OutingBenchmarkRequestDto request,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var benchmarkMode = NormalizeBenchmarkMode(request.BenchmarkMode);
        var scenarioCount = Math.Clamp(request.ScenarioCount, 1, benchmarkMode == SyntheticBenchmarkMode ? 60 : 180);
        var scenarios = benchmarkMode == SyntheticBenchmarkMode
            ? Enumerable.Range(0, scenarioCount)
                .Select(index => GenerateScenario(request.Seed, index))
                .ToList()
            : LoadPublicScenarios(request, benchmarkMode)
                .Take(scenarioCount)
                .ToList();

        var scenarioResults = new List<BenchmarkScenarioResultDto>();
        foreach (var scenario in scenarios)
        {
            ct.ThrowIfCancellationRequested();

            var runs = new List<BenchmarkAlgorithmRunDto>
            {
                // Group A: Assignment-Level (all use same TSP Held-Karp for ordering)
                await RunWeightedMedianNearestDriverAsync(scenario, ct),
                await RunOrToolsPickupCostFirstAsync(scenario, ct),
                await RunOrToolsPickupCost2sAsync(scenario, ct),
                await RunPyvrpNativeCostFirstAsync(scenario, ct),
                await RunPyvrpNativeCost2sAsync(scenario, ct),
                await RunVroomNativeCostFirstAsync(scenario, ct),
                await RunOptiGoCostOnlyAsync(scenario, ct),
                await RunOptiGoNoSharedStopAsync(scenario, ct),
                // Group B: System-Level (each solver uses its own routing order)
                await RunOrToolsFairness2sAsync(scenario, ct),
                await RunPyvrpNativeFairness2sAsync(scenario, ct),
                await RunVroomNativeFairnessAsync(scenario, ct),
                await RunOptiGoHybridAsync(scenario, ct)
            };
            foreach (var run in runs)
            {
                run.ScenarioId = scenario.ScenarioId;
                run.DatasetName = scenario.DatasetName;
                run.InstanceName = scenario.InstanceName;
                run.ScenarioSlice = scenario.ScenarioSlice;
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
            ScenarioCount = scenarios.Count,
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

        // For scenarios 18+, use real TP.HCM district centers
        var regionIndex = index - 18;
        var center = regionIndex >= 0 && regionIndex < VietnamRegions.Length
            ? VietnamRegions[regionIndex].Center
            : new Coordinate(10.7769, 106.7009);
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
        var regionDesc = regionIndex >= 0 && regionIndex < VietnamRegions.Length
            ? $" ({VietnamRegions[regionIndex].Name}: {VietnamRegions[regionIndex].Description})"
            : "";
        var description = layout switch
        {
            "clustered" => "Nhóm gần nhau, dùng để kiểm tra overhead của pickup/meeting-point." + regionDesc,
            "spread" => "Nhóm rải rộng, dễ lộ nhược điểm về tổng thời gian và fairness." + regionDesc,
            "corridor" => "Nhiều điểm nằm dọc trục đi tới venue, phù hợp corridor/shared stop." + regionDesc,
            "outlier" => "Có một passenger xa, kiểm tra max passenger time và detour." + regionDesc,
            "capacity_tight" => "Số ghế sát nhu cầu, kiểm tra feasibility và phân bổ capacity." + regionDesc,
            "shared_cluster" => "Passenger tập trung thành cụm, kiểm tra shared meetpoint." + regionDesc,
            "uniform_grid" => "Phân bố đều trên lưới 3×3, test trung lập." + regionDesc,
            _ => "Synthetic small-group outing scenario." + regionDesc
        };

        return new BenchmarkScenario(
            $"S{index + 1:000}",
            "synthetic",
            $"seed-{seed}",
            index,
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
            "uniform_grid" => Offset(center, -0.014 + 0.014 * (index % 3), -0.014 + 0.014 * (index / 3)),
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

    private static string NormalizeBenchmarkMode(string? benchmarkMode)
    {
        var normalized = string.IsNullOrWhiteSpace(benchmarkMode)
            ? SyntheticBenchmarkMode
            : benchmarkMode.Trim().ToLowerInvariant();

        return normalized switch
        {
            "public" => PublicAllBenchmarkMode,
            "darp" => DarpBenchmarkMode,
            "darp-meeting-points" => DarpBenchmarkMode,
            "li-lim" => LiLimBenchmarkMode,
            "lilim" => LiLimBenchmarkMode,
            "pdptw" => LiLimBenchmarkMode,
            PublicAllBenchmarkMode => PublicAllBenchmarkMode,
            DarpBenchmarkMode => DarpBenchmarkMode,
            LiLimBenchmarkMode => LiLimBenchmarkMode,
            _ => SyntheticBenchmarkMode
        };
    }

    private static IReadOnlyList<BenchmarkScenario> LoadPublicScenarios(
        OutingBenchmarkRequestDto request,
        string benchmarkMode)
    {
        var publicRoot = ResolvePublicBenchmarkRoot(request.PublicDataRoot);
        var scenarios = new List<BenchmarkScenario>();
        var includeDarp = benchmarkMode is PublicAllBenchmarkMode or DarpBenchmarkMode;
        var includeLiLim = benchmarkMode is PublicAllBenchmarkMode or LiLimBenchmarkMode;
        var maxVenuesPerScenario = Math.Clamp(request.PublicMaxVenuesPerScenario, 2, 8);

        if (includeDarp)
        {
            var darpRoot = Path.Combine(publicRoot, "darp-meeting-points");
            foreach (var file in Directory.EnumerateFiles(darpRoot, "*.txt").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                scenarios.AddRange(BuildDarpMeetingPointScenarios(
                    file,
                    Math.Clamp(request.DarpSlicesPerFile, 1, 16),
                    maxVenuesPerScenario));
            }
        }

        if (includeLiLim)
        {
            var liLimRoot = Path.Combine(publicRoot, "li-lim-pdptw");
            foreach (var file in Directory.EnumerateFiles(liLimRoot, "*.txt").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                scenarios.AddRange(BuildLiLimScenarios(
                    file,
                    Math.Clamp(request.LiLimSlicesPerFile, 1, 16),
                    maxVenuesPerScenario));
            }
        }

        return scenarios
            .Select((scenario, index) => scenario with { ScenarioId = $"P{index + 1:000}" })
            .ToList();
    }

    private static string ResolvePublicBenchmarkRoot(string? configuredRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var fullPath = Path.GetFullPath(configuredRoot);
            if (Directory.Exists(fullPath))
                return fullPath;
        }

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "benchmarks", "public");
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Không tìm thấy benchmarks/public để chạy public benchmark.");
    }

    private static IReadOnlyList<BenchmarkScenario> BuildDarpMeetingPointScenarios(
        string filePath,
        int slicesPerFile,
        int maxVenuesPerScenario)
    {
        var lines = File.ReadAllLines(filePath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        if (lines.Count < 3)
            return [];

        var header = SplitPublicLine(lines[0]);
        var vehicleCount = ParseInt(header[0]);
        var requestNodeCount = ParseInt(header[1]);
        var capacity = ParseInt(header[3]);
        var requestPairCount = Math.Max(1, requestNodeCount / 2);
        var nodes = lines
            .Skip(2)
            .Select(ParsePublicNode)
            .ToDictionary(node => node.Id);
        var allCoordinates = nodes.Values.Select(node => (node.X, node.Y)).ToList();
        var instanceName = Path.GetFileNameWithoutExtension(filePath);
        var scenarios = new List<BenchmarkScenario>();

        for (var slice = 0; slice < slicesPerFile; slice++)
        {
            var session = new Session($"public-darp-{instanceName}-{slice:00}");
            var driverCount = Math.Clamp(vehicleCount, 2, 4);
            var selectedDriverIds = SelectRotatingIds(1, requestPairCount, driverCount, slice * 2);
            var pickupPool = Enumerable.Range(1, requestPairCount)
                .Where(id => !selectedDriverIds.Contains(id))
                .ToList();
            var passengerCount = Math.Min(Math.Min(driverCount * capacity, 8), pickupPool.Count);
            var selectedPassengerIds = SelectRotatingIds(pickupPool, passengerCount, slice * 3);

            foreach (var driverId in selectedDriverIds)
            {
                if (!nodes.TryGetValue(driverId, out var driverNode))
                    continue;

                session.AddMember(new Member(
                    session.Id,
                    $"Driver {driverId}",
                    ToPublicCoordinate(driverNode.X, driverNode.Y, allCoordinates),
                    TransportMode.Car,
                    MemberMobilityRole.SelfTravel));
            }

            foreach (var passengerId in selectedPassengerIds)
            {
                if (!nodes.TryGetValue(passengerId, out var passengerNode))
                    continue;

                var member = new Member(
                    session.Id,
                    $"Passenger {passengerId}",
                    ToPublicCoordinate(passengerNode.X, passengerNode.Y, allCoordinates),
                    TransportMode.Walking,
                    MemberMobilityRole.NeedsPickup);
                session.AddMember(member);
                session.CreateOrGetPickupRequest(member.Id);
            }

            var venueNodes = new List<PublicNode>();
            venueNodes.AddRange(selectedPassengerIds
                .Select(id => id + requestPairCount)
                .Where(nodes.ContainsKey)
                .Select(id => nodes[id]));
            venueNodes.AddRange(nodes.Values
                .Where(node => node.Id > requestNodeCount + 1)
                .OrderBy(node => Math.Abs(node.Id - (34 + slice * 3)))
                .ThenBy(node => node.Id)
                .Take(5));

            var venues = venueNodes
                .GroupBy(node => node.Id)
                .Select(group => group.First())
                .Take(maxVenuesPerScenario)
                .Select(node => new Venue(
                    $"darp-{instanceName}-{slice:00}-{node.Id}",
                    $"DARP node {node.Id}",
                    "public-meeting-point",
                    ToPublicCoordinate(node.X, node.Y, allCoordinates),
                    4.2,
                    100))
                .ToList();

            scenarios.Add(new BenchmarkScenario(
                "",
                "darp-meeting-points",
                instanceName,
                slice,
                "darp_mp",
                session,
                venues,
                $"Public DARP-MP instance {instanceName}, deterministic slice {slice + 1}/{slicesPerFile}."));
        }

        return scenarios;
    }

    private static IReadOnlyList<BenchmarkScenario> BuildLiLimScenarios(
        string filePath,
        int slicesPerFile,
        int maxVenuesPerScenario)
    {
        var lines = File.ReadAllLines(filePath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        if (lines.Count < 3)
            return [];

        var instanceName = Path.GetFileNameWithoutExtension(filePath);
        var nodes = lines
            .Skip(1)
            .Select(ParsePublicNode)
            .ToDictionary(node => node.Id);
        var pickupIds = nodes.Values
            .Where(node => node.DeliveryNodeId > 0 && nodes.ContainsKey(node.DeliveryNodeId))
            .OrderBy(node => node.Id)
            .Select(node => node.Id)
            .ToList();
        var allCoordinates = nodes.Values.Select(node => (node.X, node.Y)).ToList();
        var layout = instanceName.StartsWith("lc", StringComparison.OrdinalIgnoreCase)
            ? "li_lim_clustered"
            : instanceName.StartsWith("lr", StringComparison.OrdinalIgnoreCase)
                ? "li_lim_random"
                : "li_lim_mixed";
        var scenarios = new List<BenchmarkScenario>();

        for (var slice = 0; slice < slicesPerFile; slice++)
        {
            var session = new Session($"public-lilim-{instanceName}-{slice:00}");
            var driverCount = 3;
            var selectedDriverIds = SelectRotatingIds(pickupIds, driverCount, slice * 5);
            var passengerPool = pickupIds
                .Where(id => !selectedDriverIds.Contains(id))
                .ToList();
            var selectedPassengerIds = SelectRotatingIds(passengerPool, 6, slice * 7);

            foreach (var driverId in selectedDriverIds)
            {
                var driverNode = nodes[driverId];
                session.AddMember(new Member(
                    session.Id,
                    $"Driver {driverId}",
                    ToPublicCoordinate(driverNode.X, driverNode.Y, allCoordinates),
                    TransportMode.Car,
                    MemberMobilityRole.SelfTravel));
            }

            foreach (var passengerId in selectedPassengerIds)
            {
                var passengerNode = nodes[passengerId];
                var member = new Member(
                    session.Id,
                    $"Passenger {passengerId}",
                    ToPublicCoordinate(passengerNode.X, passengerNode.Y, allCoordinates),
                    TransportMode.Walking,
                    MemberMobilityRole.NeedsPickup);
                session.AddMember(member);
                session.CreateOrGetPickupRequest(member.Id);
            }

            var venueNodes = selectedPassengerIds
                .Select(id => nodes[nodes[id].DeliveryNodeId])
                .Concat(selectedDriverIds.Select(id => nodes[nodes[id].DeliveryNodeId]))
                .GroupBy(node => node.Id)
                .Select(group => group.First())
                .Take(maxVenuesPerScenario)
                .ToList();
            var venues = venueNodes
                .Select(node => new Venue(
                    $"lilim-{instanceName}-{slice:00}-{node.Id}",
                    $"Li-Lim node {node.Id}",
                    "public-delivery-point",
                    ToPublicCoordinate(node.X, node.Y, allCoordinates),
                    4.2,
                    100))
                .ToList();

            scenarios.Add(new BenchmarkScenario(
                "",
                "li-lim-pdptw",
                instanceName,
                slice,
                layout,
                session,
                venues,
                $"Public Li-Lim PDPTW instance {instanceName}, deterministic slice {slice + 1}/{slicesPerFile}."));
        }

        return scenarios;
    }

    private static PublicNode ParsePublicNode(string line)
    {
        var parts = SplitPublicLine(line);
        var pickupNodeId = parts.Length > 7 ? ParseInt(parts[7]) : 0;
        var deliveryNodeId = parts.Length > 8 ? ParseInt(parts[8]) : 0;
        return new PublicNode(
            ParseInt(parts[0]),
            ParseDouble(parts[1]),
            ParseDouble(parts[2]),
            parts.Length > 4 ? ParseInt(parts[4]) : 0,
            pickupNodeId,
            deliveryNodeId);
    }

    private static string[] SplitPublicLine(string line) =>
        line.Split([',', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int ParseInt(string value) =>
        int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double ParseDouble(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static HashSet<int> SelectRotatingIds(int firstId, int count, int take, int offset) =>
        SelectRotatingIds(Enumerable.Range(firstId, count).ToList(), take, offset).ToHashSet();

    private static List<int> SelectRotatingIds(IReadOnlyList<int> ids, int take, int offset)
    {
        if (ids.Count == 0 || take <= 0)
            return [];

        return Enumerable.Range(0, Math.Min(take, ids.Count))
            .Select(index => ids[(offset + index) % ids.Count])
            .Distinct()
            .ToList();
    }

    private static Coordinate ToPublicCoordinate(
        double x,
        double y,
        IReadOnlyList<(double X, double Y)> allCoordinates)
    {
        var centerX = allCoordinates.Count == 0 ? 0 : allCoordinates.Average(point => point.X);
        var centerY = allCoordinates.Count == 0 ? 0 : allCoordinates.Average(point => point.Y);
        return new Coordinate(
            PublicCoordinateAnchor.Latitude + (y - centerY) * PublicCoordinateScaleDegrees,
            PublicCoordinateAnchor.Longitude + (x - centerX) * PublicCoordinateScaleDegrees);
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
            var assignment = await BuildOrToolsAssignmentAsync(scenario.Session, venue, provider, ct: ct);
            var candidate = assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, "OR-Tools không tìm được nghiệm capacity pickup.", ct)
                : await EvaluateDoorstepCandidateAsync(scenario.Session, venue, assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = candidates.OrderBy(RoutingSolutionScorer.CalculatePureCost).First();
        return ToRun("ortools_pickup_cost", "OR-Tools pickup VRP cost-first", false, best, stopwatch.Elapsed.TotalMilliseconds);
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
            var result = await BuildPyvrpAssignmentAsync(scenario, venue, provider, ct: ct);
            var candidate = result.Assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, result.Error ?? "PyVRP native không tìm được nghiệm capacity pickup.", ct)
                : await EvaluateDoorstepCandidateAsync(scenario.Session, venue, result.Assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = candidates.OrderBy(RoutingSolutionScorer.CalculatePureCost).First();
        return ToRun("pyvrp_hgs_cost", "PyVRP Hybrid Genetic Search cost-first", false, best, stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task<BenchmarkAlgorithmRunDto> RunOrToolsPickupCost2sAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var assignment = await BuildOrToolsAssignmentAsync(scenario.Session, venue, provider, timeLimitMs: 2000, ct: ct);
            var candidate = assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, "OR-Tools 2s không tìm được nghiệm capacity pickup.", ct)
                : await EvaluateDoorstepCandidateAsync(scenario.Session, venue, assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = candidates.OrderBy(RoutingSolutionScorer.CalculatePureCost).First();
        return ToRun("ortools_pickup_cost_2s", "OR-Tools pickup VRP cost-first 2s", false, best, stopwatch.Elapsed.TotalMilliseconds, "A");
    }

    private async Task<BenchmarkAlgorithmRunDto> RunOrToolsFairness2sAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var assignment = await BuildOrToolsAssignmentAsync(
                scenario.Session, venue, provider, timeLimitMs: 2000, globalSpanCoefficient: 10, ct: ct);
            var candidate = assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, "OR-Tools fairness 2s không tìm được nghiệm.", ct)
                : await EvaluateOrderedDoorstepCandidateAsync(scenario.Session, venue, assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = SelectFairnessTunedExternalCandidate(candidates);
        return ToRun("ortools_pickup_fair_2s", "OR-Tools pickup VRP fairness 2s", false, best, stopwatch.Elapsed.TotalMilliseconds, "B");
    }

    private async Task<BenchmarkAlgorithmRunDto> RunPyvrpNativeCost2sAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var result = await BuildPyvrpAssignmentAsync(scenario, venue, provider, timeLimitSeconds: 2.0, ct: ct);
            var candidate = result.Assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, result.Error ?? "PyVRP 2s không tìm được nghiệm capacity pickup.", ct)
                : await EvaluateDoorstepCandidateAsync(scenario.Session, venue, result.Assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = candidates.OrderBy(RoutingSolutionScorer.CalculatePureCost).First();
        return ToRun("pyvrp_hgs_cost_2s", "PyVRP HGS cost-first 2s", false, best, stopwatch.Elapsed.TotalMilliseconds, "A");
    }

    private async Task<BenchmarkAlgorithmRunDto> RunPyvrpNativeFairness2sAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var result = await BuildPyvrpAssignmentAsync(scenario, venue, provider, timeLimitSeconds: 2.0, ct: ct);
            var candidate = result.Assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, result.Error ?? "PyVRP fairness 2s không tìm được nghiệm.", ct)
                : await EvaluateOrderedDoorstepCandidateAsync(scenario.Session, venue, result.Assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = SelectFairnessTunedExternalCandidate(candidates);
        return ToRun("pyvrp_hgs_fair_2s", "PyVRP HGS fairness 2s", false, best, stopwatch.Elapsed.TotalMilliseconds, "B");
    }

    private async Task<BenchmarkAlgorithmRunDto> RunVroomNativeCostFirstAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var result = await BuildVroomAssignmentAsync(scenario, venue, provider, ct: ct);
            var candidate = result.Assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, result.Error ?? "VROOM native không tìm được nghiệm capacity pickup.", ct)
                : await EvaluateOrderedDoorstepCandidateAsync(scenario.Session, venue, result.Assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = candidates.OrderBy(RoutingSolutionScorer.CalculatePureCost).First();
        return ToRun("vroom_cost", "VROOM native cost-first", false, best, stopwatch.Elapsed.TotalMilliseconds, "A");
    }

    private async Task<BenchmarkAlgorithmRunDto> RunVroomNativeFairnessAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var candidates = new List<CandidateResultDto>();

        foreach (var venue in scenario.Venues)
        {
            var result = await BuildVroomAssignmentAsync(scenario, venue, provider, ct: ct);
            var candidate = result.Assignment == null
                ? await BuildInfeasibleCandidateAsync(scenario.Session, venue, provider, result.Error ?? "VROOM native không tìm được nghiệm.", ct)
                : await EvaluateOrderedDoorstepCandidateAsync(scenario.Session, venue, result.Assignment, provider, ct);
            candidates.Add(candidate);
        }

        stopwatch.Stop();
        var best = SelectFairnessTunedExternalCandidate(candidates);
        return ToRun("vroom_fair", "VROOM native fairness-selected", false, best, stopwatch.Elapsed.TotalMilliseconds, "B");
    }

    private async Task<BenchmarkAlgorithmRunDto> RunOptiGoCostOnlyAsync(
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

        var pool = candidates.Any(candidate => candidate.IsFeasible)
            ? candidates.Where(candidate => candidate.IsFeasible)
            : candidates;
        var best = pool
            .OrderBy(RoutingSolutionScorer.CalculatePureCost)
            .ThenBy(RoutingSolutionScorer.CalculateFairnessScore)
            .First();
        stopwatch.Stop();
        return ToRun("optigo_cost_only", "OptiGo cost-only selection", true, best, stopwatch.Elapsed.TotalMilliseconds, "A");
    }

    private async Task<BenchmarkAlgorithmRunDto> RunOptiGoNoSharedStopAsync(
        BenchmarkScenario scenario,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = new BenchmarkRouteCostProvider(scenario.Layout);
        var traffic = new BenchmarkTrafficSnapshotProvider(scenario.Layout);
        var planner = new HybridOutingRoutePlanner(
            new SharedDestinationRouteOptimizer(new BenchmarkDoorstepOnlyStopCandidateGenerator(), provider),
            provider,
            traffic);
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

        var best = SelectFairnessTunedExternalCandidate(candidates);
        stopwatch.Stop();
        return ToRun("optigo_no_shared_stop", "OptiGo fairness without shared-stop", true, best, stopwatch.Elapsed.TotalMilliseconds, "A");
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
        return ToRun("optigo_hybrid", "OptiGo Hybrid route-pool Pareto", true, best, stopwatch.Elapsed.TotalMilliseconds, "B");
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
        int timeLimitMs = 150,
        int globalSpanCoefficient = 1,
        CancellationToken ct = default)
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
        timeDimension.SetGlobalSpanCostCoefficient(globalSpanCoefficient);

        var searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;
        searchParameters.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        searchParameters.TimeLimit = new Duration { Nanos = timeLimitMs * 1_000_000 };

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

    private async Task<NativeAssignmentResult> BuildPyvrpAssignmentAsync(
        BenchmarkScenario scenario,
        Venue venue,
        IRouteCostProvider provider,
        double timeLimitSeconds = 0.15,
        CancellationToken ct = default)
    {
        return await BuildPythonNativeAssignmentAsync(
            "pyvrp",
            "pyvrp_solve.py",
            scenario,
            venue,
            provider,
            timeLimitSeconds,
            ct);
    }

    private async Task<NativeAssignmentResult> BuildVroomAssignmentAsync(
        BenchmarkScenario scenario,
        Venue venue,
        IRouteCostProvider provider,
        double timeLimitSeconds = 2.0,
        CancellationToken ct = default)
    {
        return await BuildPythonNativeAssignmentAsync(
            "vroom",
            "vroom_solve.py",
            scenario,
            venue,
            provider,
            timeLimitSeconds,
            ct);
    }

    private async Task<NativeAssignmentResult> BuildPythonNativeAssignmentAsync(
        string solverKey,
        string scriptFileName,
        BenchmarkScenario scenario,
        Venue venue,
        IRouteCostProvider provider,
        double timeLimitSeconds,
        CancellationToken ct)
    {
        var drivers = scenario.Session.Members.Where(member => member.CanOfferPickup()).ToList();
        var passengers = scenario.Session.Members.Where(member => member.NeedsPickup()).ToList();
        if (drivers.Count == 0 && passengers.Count > 0)
            return NativeAssignmentResult.Failed("Không có driver cho passenger cần pickup.");

        if (drivers.Sum(driver => driver.GetSeatCapacity()) < passengers.Count)
            return NativeAssignmentResult.Failed("Không đủ tổng số ghế cho passenger cần pickup.");

        if (passengers.Count == 0)
            return NativeAssignmentResult.Success(drivers.ToDictionary(driver => driver.Id, _ => new List<Member>()));

        var scriptPath = LocateNativeBenchmarkScript(scriptFileName);
        if (scriptPath == null)
            return NativeAssignmentResult.Failed($"Không tìm thấy native bridge script {scriptFileName}.");

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
        var inputPath = Path.Combine(workDir, $"{prefix}-{solverKey}-input.json");
        var outputPath = Path.Combine(workDir, $"{prefix}-{solverKey}-output.json");

        var payload = new
        {
            scenarioId = scenario.ScenarioId,
            venueId = venue.Id,
            seed = StableNativeSeed(scenario.ScenarioId, venue.Id),
            timeLimitSeconds,
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
                FileName = ResolvePythonExecutable(),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo);
            if (process == null)
                return NativeAssignmentResult.Failed($"{solverKey} bridge process could not start.");

            var waitForExit = process.WaitForExitAsync(ct);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            var timeout = Task.Delay(TimeSpan.FromSeconds(Math.Max(12, timeLimitSeconds * 5 + 5)), ct);
            var completedTask = await Task.WhenAny(waitForExit, timeout);
            if (completedTask != waitForExit)
            {
                TryKill(process);
                PreserveNativeFailure(inputPath, outputPath, solverKey, "timeout", "", "");
                return NativeAssignmentResult.Failed($"{solverKey} bridge timed out after {Math.Max(12, timeLimitSeconds * 5 + 5):0}s.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                PreserveNativeFailure(inputPath, outputPath, solverKey, $"exit-{process.ExitCode}", stdout, stderr);
                return NativeAssignmentResult.Failed($"{solverKey} bridge failed: {TrimNativeError(stderr, stdout)}");
            }

            var outputJson = await File.ReadAllTextAsync(outputPath, ct);
            var output = JsonSerializer.Deserialize<PyvrpOutputDto>(
                outputJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (output is not { IsFeasible: true })
            {
                PreserveNativeFailure(inputPath, outputPath, solverKey, "infeasible", stdout, stderr);
                return NativeAssignmentResult.Failed(output?.BridgeError ?? $"{solverKey} bridge returned no feasible assignment.");
            }

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
            return assignedPassengerCount == passengers.Count
                ? NativeAssignmentResult.Success(assignment)
                : NativeAssignmentResult.Failed($"{solverKey} assignment covered {assignedPassengerCount}/{passengers.Count} pickup passengers.");
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

    /// <summary>
    /// Like EvaluateDoorstepCandidateAsync but preserves the solver's passenger visit order.
    /// Used by Group B algorithms where OR-Tools/PyVRP routing order should be respected.
    /// </summary>
    private async Task<CandidateResultDto> EvaluateOrderedDoorstepCandidateAsync(
        Session session,
        Venue venue,
        IReadOnlyDictionary<Guid, List<Member>> orderedPassengersByDriver,
        IRouteCostProvider provider,
        CancellationToken ct)
    {
        var memberRoutes = new List<MemberRouteDto>();
        var driverRoutes = new List<DriverRouteDto>();
        var breakdown = new RouteScoreBreakdownDto();
        var assignedPassengerIds = orderedPassengersByDriver.Values
            .SelectMany(passengers => passengers.Select(passenger => passenger.Id))
            .ToHashSet();

        foreach (var driver in session.Members.Where(member => member.CanOfferPickup()))
        {
            orderedPassengersByDriver.TryGetValue(driver.Id, out var passengers);
            passengers ??= [];
            // Key difference: pass passengers in solver-provided order, skip TSP re-solve
            var result = await BuildOrderedDoorstepDriverResultAsync(driver, passengers, venue, provider, ct);
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

    /// <summary>
    /// Like BuildDoorstepDriverResultAsync but uses passengers in the provided order
    /// (from external solver) instead of calling SolveDoorstepOrderAsync.
    /// </summary>
    private async Task<DriverOptimizationResult> BuildOrderedDoorstepDriverResultAsync(
        Member driver,
        IReadOnlyList<Member> passengers,
        Venue venue,
        IRouteCostProvider provider,
        CancellationToken ct)
    {
        var origin = driver.GetLocation();
        var destination = venue.GetLocation();
        var direct = await provider.GetExactRouteAsync(origin, destination, driver.TransportMode, ct: ct);
        // Use passengers in given order directly (solver's order)
        var orderedPassengers = passengers;
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
        double computeTimeMs,
        string benchmarkGroup = "A") =>
        new()
        {
            AlgorithmKey = algorithmKey,
            AlgorithmName = algorithmName,
            IsOptiGo = isOptiGo,
            BenchmarkGroup = benchmarkGroup,
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
        // Compute gaps within the same benchmark group for fair comparison
        foreach (var groupRuns in runs.GroupBy(run => run.BenchmarkGroup))
        {
            var groupRunList = groupRuns.ToList();
            var bestExternal = groupRunList
                .Where(run => !run.IsOptiGo && run.IsFeasible)
                .OrderBy(run => run.ObjectiveSeconds)
                .FirstOrDefault();
            var bestCostExternal = groupRunList
                .Where(run => !run.IsOptiGo && run.IsFeasible)
                .OrderBy(run => run.PureCostSeconds)
                .FirstOrDefault();

            foreach (var run in groupRunList)
            {
                if (bestExternal != null)
                {
                    run.GapToBestExternalPercent = bestExternal.ObjectiveSeconds <= 0
                        ? 0
                        : (run.ObjectiveSeconds - bestExternal.ObjectiveSeconds) / bestExternal.ObjectiveSeconds * 100;
                }

                if (bestCostExternal != null)
                {
                    var costGuard = bestCostExternal.PureCostSeconds * FairnessCostGuardRatio + FairnessCostGuardSlackSeconds;
                    run.CostGapToBestExternalPercent = bestCostExternal.PureCostSeconds <= 0
                        ? 0
                        : (run.PureCostSeconds - bestCostExternal.PureCostSeconds) / bestCostExternal.PureCostSeconds * 100;
                    run.FairnessGainVsBestCostExternalPercent = bestCostExternal.FairnessScoreSeconds <= 0
                        ? 0
                        : (bestCostExternal.FairnessScoreSeconds - run.FairnessScoreSeconds) / bestCostExternal.FairnessScoreSeconds * 100;
                    run.CostGuardPassed = run.IsFeasible && run.PureCostSeconds <= costGuard;
                    run.FairnessGainWithinGuardPercent = run.CostGuardPassed
                        ? run.FairnessGainVsBestCostExternalPercent
                        : 0;
                }
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
            DatasetName = scenario.DatasetName,
            InstanceName = scenario.InstanceName,
            ScenarioSlice = scenario.ScenarioSlice,
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
            .GroupBy(run => (run.ScenarioId, run.BenchmarkGroup))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(run => run.IsFeasible ? 0 : 1)
                    .ThenBy(run => run.ObjectiveSeconds)
                    .First());

        return runs
            .GroupBy(run => new { run.AlgorithmKey, run.AlgorithmName, run.IsOptiGo, run.BenchmarkGroup })
            .Select(group =>
            {
                var groupRuns = group.ToList();
                var serviceableRuns = groupRuns.Where(run => run.IsScenarioServiceable).ToList();
                var metricRuns = serviceableRuns.Count > 0 ? serviceableRuns : groupRuns;
                var wins = metricRuns.Count(run =>
                    bestByScenario.TryGetValue((run.ScenarioId, run.BenchmarkGroup), out var best) &&
                    IsTiedWithBest(run, best));

                return new BenchmarkAlgorithmAggregateDto
                {
                    AlgorithmKey = group.Key.AlgorithmKey,
                    AlgorithmName = group.Key.AlgorithmName,
                    IsOptiGo = group.Key.IsOptiGo,
                    BenchmarkGroup = group.Key.BenchmarkGroup,
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
            .OrderBy(aggregate => aggregate.BenchmarkGroup)
            .ThenBy(aggregate => aggregate.IsOptiGo ? 0 : 1)
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
            var optigo = scenario.Runs.FirstOrDefault(run => run.AlgorithmKey == "optigo_hybrid")
                ?? scenario.Runs.First(run => run.IsOptiGo);
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

    private static string ResolvePythonExecutable() => BenchmarkPythonExecutable.Value;

    private static string ResolvePythonExecutableCore()
    {
        var explicitPython =
            Environment.GetEnvironmentVariable("PYVRP_PYTHON")
            ?? Environment.GetEnvironmentVariable("OPTIGO_BENCHMARK_PYTHON");
        if (!string.IsNullOrWhiteSpace(explicitPython))
            return explicitPython;

        foreach (var candidate in GetPythonExecutableCandidates())
        {
            if (CanImportPythonModule(candidate, "pyvrp"))
                return candidate;
        }

        return "python";
    }

    private static IEnumerable<string> GetPythonExecutableCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in GetPythonExecutablesFromLauncher())
        {
            if (seen.Add(candidate))
                yield return candidate;
        }

        foreach (var candidate in GetExecutablesFromPath("python"))
        {
            if (seen.Add(candidate))
                yield return candidate;
        }

        foreach (var candidate in GetExecutablesFromPath("python3"))
        {
            if (seen.Add(candidate))
                yield return candidate;
        }

        foreach (var candidate in new[] { @"C:\Python314\python.exe", @"C:\Python313\python.exe", "python", "python3" })
        {
            if (seen.Add(candidate))
                yield return candidate;
        }
    }

    private static IEnumerable<string> GetPythonExecutablesFromLauncher()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        var output = RunShortProcess("py", ["-0p"]);
        if (string.IsNullOrWhiteSpace(output))
            yield break;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var driveSeparatorIndex = line.IndexOf(@":\", StringComparison.Ordinal);
            if (driveSeparatorIndex <= 0)
                continue;

            var path = line[(driveSeparatorIndex - 1)..].Trim();
            if (File.Exists(path))
                yield return path;
        }
    }

    private static IEnumerable<string> GetExecutablesFromPath(string executableName)
    {
        var command = OperatingSystem.IsWindows() ? "where" : "which";
        var output = RunShortProcess(command, [executableName]);
        if (string.IsNullOrWhiteSpace(output))
            yield break;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = line.Trim();
            if (path.Length > 0)
                yield return path;
        }
    }

    private static bool CanImportPythonModule(string pythonExecutable, string moduleName)
    {
        var output = RunShortProcess(pythonExecutable, ["-c", $"import {moduleName}"], timeoutMilliseconds: 3000);
        return output != null;
    }

    private static string? RunShortProcess(string fileName, IReadOnlyList<string> arguments, int timeoutMilliseconds = 2000)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            if (!process.WaitForExit(timeoutMilliseconds))
            {
                TryKill(process);
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            return process.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }

    private static string TrimNativeError(string stderr, string stdout)
    {
        var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        message = string.IsNullOrWhiteSpace(message) ? "no stderr/stdout" : message.Trim();
        return message.Length <= 500 ? message : message[..500];
    }

    private static int StableNativeSeed(string scenarioId, string venueId)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in $"{scenarioId}:{venueId}")
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7fffffff);
        }
    }

    private static void PreserveNativeFailure(
        string inputPath,
        string outputPath,
        string solverKey,
        string reason,
        string stdout,
        string stderr)
    {
        try
        {
            var root = FindRepositoryRoot() ?? Directory.GetCurrentDirectory();
            var failureDir = Path.Combine(root, ".buildtmp", "native-failures", solverKey);
            Directory.CreateDirectory(failureDir);
            var prefix = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{reason}-{Path.GetFileNameWithoutExtension(inputPath)}";
            if (File.Exists(inputPath))
            {
                File.Copy(inputPath, Path.Combine(failureDir, $"{prefix}.input.json"), overwrite: true);
            }

            if (File.Exists(outputPath))
            {
                File.Copy(outputPath, Path.Combine(failureDir, $"{prefix}.output.json"), overwrite: true);
            }

            File.WriteAllText(Path.Combine(failureDir, $"{prefix}.stderr.txt"), stderr);
            File.WriteAllText(Path.Combine(failureDir, $"{prefix}.stdout.txt"), stdout);
        }
        catch
        {
            // Best-effort diagnostics only.
        }
    }

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

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
        public string? BridgeError { get; init; }
        public List<PyvrpRouteDto> Routes { get; init; } = [];
    }

    private sealed class PyvrpRouteDto
    {
        public int DriverIndex { get; init; }
        public List<int> PassengerIndices { get; init; } = [];
    }

    private sealed record NativeAssignmentResult(
        Dictionary<Guid, List<Member>>? Assignment,
        string? Error)
    {
        public static NativeAssignmentResult Success(Dictionary<Guid, List<Member>> assignment) =>
            new(assignment, null);

        public static NativeAssignmentResult Failed(string error) =>
            new(null, error);
    }

    private sealed record BenchmarkScenario(
        string ScenarioId,
        string DatasetName,
        string InstanceName,
        int ScenarioSlice,
        string Layout,
        Session Session,
        IReadOnlyList<Venue> Venues,
        string Description);

    private sealed record PublicNode(
        int Id,
        double X,
        double Y,
        int Demand,
        int PickupNodeId,
        int DeliveryNodeId);

    private sealed class BenchmarkDoorstepOnlyStopCandidateGenerator : IStopCandidateGenerator
    {
        public Task<IReadOnlyList<StopCandidate>> GenerateAsync(
            DriverOptimizationInput input,
            CancellationToken ct = default)
        {
            IReadOnlyList<StopCandidate> candidates = input.Passengers
                .Select(passenger => new StopCandidate
                {
                    CandidateId = $"{passenger.Id}:doorstep-only",
                    StopLocation = passenger.GetLocation(),
                    Label = passenger.Name,
                    StopAccessType = "doorstep",
                    PassengerIds = [passenger.Id],
                    WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = 0 },
                    AccessPenaltySeconds = 0,
                    RiskPenaltySeconds = 0
                })
                .ToList();

            return Task.FromResult(candidates);
        }
    }

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
