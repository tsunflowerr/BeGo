# Public Benchmark Methodology

## Scope

Public benchmark mode uses only files under `benchmarks/public`:

- `darp-meeting-points`: main benchmark for pickup, capacity, meeting-point/shared-stop behavior.
- `li-lim-pdptw`: secondary benchmark for clustered, random, and mixed pickup-delivery layouts.

These are public academic benchmark instances, not GPS or traffic traces. The benchmark does not generate synthetic member or venue locations in public mode. It maps public `(x, y)` coordinates into a local coordinate frame only so the existing route-cost provider can consume valid latitude/longitude values.

## Modes

The API and runner support:

- `synthetic`: existing generated benchmark.
- `darp-mp`: only DARP with meeting points.
- `li-lim-pdptw`: only Li & Lim PDPTW.
- `public-all`: both public datasets.

Recommended full public run:

```powershell
dotnet run --project .buildtmp\BenchmarkRunner\BenchmarkRunner.csproj -- 20260505 120 .buildtmp\benchmark-public-120.json --mode public-all
```

To include VROOM as a real native baseline, install a `vroom` binary on `PATH` or set `VROOM_BIN` to the executable path before running. Alternatively, start Docker Desktop and set `VROOM_DOCKER_IMAGE=ghcr.io/vroom-project/vroom-docker:v1.15.0-rc.1`. If neither is available, the benchmark reports VROOM as a bridge/runtime failure and its rows should not be used for conclusions.

On Windows, the benchmark runner auto-detects a Python executable that can import `pyvrp`. Set `PYVRP_PYTHON` explicitly only when you want to override that detection, for example on a machine with multiple Python installations.

```powershell
$env:PYVRP_PYTHON="C:\Python314\python.exe"
```

The default public slicing is `12 DARP files x 8 slices = 96 scenarios` plus `3 Li-Lim files x 8 slices = 24 scenarios`, for `120` scenarios total. Use at least `90` scenarios for report conclusions; do not use fewer than `60` for a main claim.

## Fairness Controls

Each scenario uses the same:

- public source file and deterministic slice rule;
- Euclidean route matrix derived from the public coordinates;
- drivers, pickup passengers, candidate venues, and capacities;
- evaluator for cost, fairness, Gini, regret, detour, walking, and runtime.

The report separates:

- Group A: solver-neutral assignment-level comparison and OptiGo ablations.
- Group B: system-level comparison with full OptiGo hybrid shared-stop planner.

The cost guard is fixed before running: OptiGo passes when its pure cost is no worse than `best external cost * 1.08 + 90s`. Fairness improvement is measured against the best external cost-first run in the same benchmark group.

## Caveats

Li & Lim time windows are not enforced as hard constraints in this v1 public outing benchmark. The dataset is used for public, reproducible spatial pickup-delivery structure; the current OptiGo evaluator remains focused on small-group outing fairness rather than full PDPTW time-window feasibility.
