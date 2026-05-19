# Native Benchmark Methodology Audit

## Kết luận ngắn

Benchmark hiện tại đã chuyển sang nguyên tắc: **chỉ report external solver nếu solver đó chạy native/core thật**. Vì vậy các kết quả `VROOM-style` và `jsprit-style` cũ không còn nằm trong benchmark mặc định.

Hiện các external native đang được chấm:

| Solver | Trạng thái |
| --- | --- |
| OR-Tools | Native thật qua `Google.OrTools` NuGet. |
| PyVRP | Native thật qua Python package `pyvrp`, gọi bằng bridge `benchmarks/native/pyvrp_solve.py`. |
| VROOM | Source đã pull, chưa report vì Docker/native binary chưa chạy được trong môi trường hiện tại. |
| jsprit | Source đã pull, chưa report vì chưa có Maven/Gradle runner native. |

Luận điểm báo cáo hợp lệ:

> OptiGo được so sánh với native OR-Tools và native PyVRP HGS trên cùng input, cùng matrix, cùng evaluator. VROOM/jsprit chưa được đưa vào kết luận vì chưa chạy native binary/library thật.

## Những phần đang công bằng

| Hạng mục | Trạng thái | Lý do |
| --- | --- | --- |
| Cùng input | Đạt | `GenerateScenario(seed, index)` tạo cùng member, driver, passenger, venue cho mọi algorithm trong cùng scenario. |
| Cùng route-cost model | Đạt | OR-Tools, PyVRP và OptiGo đều dùng `BenchmarkRouteCostProvider`; OR-Tools/PyVRP còn dùng profile riêng theo `driver.TransportMode`. |
| Cùng evaluator | Đạt | Output route của external được normalize rồi chấm bằng `EvaluateDoorstepCandidateAsync` và `RoutingSolutionScorer`. |
| Cùng feasibility validation | Đạt | Tất cả candidate dùng `RoutingSolutionScorer.ValidateSolution`. |
| Tách case không serviceable | Đạt | Aggregate chính dùng serviceable scenarios, tránh case thiếu ghế làm méo kết luận. |
| Reproducibility | Đạt | Fixed seed, scenario count, raw JSON output và external commit đều kiểm tra được. |

## Những phần cần ghi rõ

| Vấn đề | Mức độ | Cách ghi |
| --- | --- | --- |
| VROOM/jsprit chưa native | Cao | Không claim OptiGo thắng VROOM/jsprit. Chỉ nói source đã pull và chờ native runner. |
| PyVRP bridge chậm | Trung bình | PyVRP đang gọi subprocess Python theo venue, runtime cao hơn do startup overhead. |
| OR-Tools/PyVRP không hiểu fairness native | Trung bình | External solve bằng core cost VRP; fairness được chấm/chọn bởi evaluator chung của OptiGo. |
| OptiGo có shared-stop planner riêng | Trung bình | Đây là so sánh hệ thống OptiGo hoàn chỉnh với native VRP baselines trên cùng evaluator. |
| Synthetic matrix | Trung bình | Đây là synthetic benchmark, không phải Google Maps/OSRM traffic benchmark. |

## Tiêu chí đánh giá chuẩn

### Feasibility

- `IsScenarioServiceable`
- `IsFeasible`
- `ServiceableFeasibleRate`
- `FeasibilityIssues`

### Cost

- `PureCostSeconds`
- `TotalGroupTimeSeconds`
- `TotalDriverDetourSeconds`
- `StopCount`

### Fairness

- `FairnessScoreSeconds`
- `MaxMemberBurdenSeconds`
- `WorstMemberRegretSeconds`
- `PassengerBurdenGini`
- `MaxDriverDetourSeconds`
- `DriverDetourGini`

Một kết quả đúng hướng cho OptiGo:

```text
PureCostSeconds tăng nhẹ hoặc ngang external
FairnessScoreSeconds giảm rõ
PassengerBurdenGini giảm
WorstMemberRegretSeconds giảm
MaxDriverDetourSeconds không tăng mạnh
```

## Các chỗ đã sửa để setup native benchmark

| File | Vai trò |
| --- | --- |
| `src/OptiGo.Infrastructure/OptiGo.Infrastructure.csproj` | Thêm `Google.OrTools`. |
| `src/OptiGo.Infrastructure/Routing/OutingBenchmarkService.cs` | Chạy OR-Tools native, PyVRP native, bỏ VROOM/jsprit style khỏi benchmark mặc định. |
| `benchmarks/native/pyvrp_solve.py` | Python bridge gọi PyVRP HGS thật, dùng profile theo từng driver mode và export route JSON. |
| `.buildtmp/BenchmarkRunner/Program.cs` | Runner tạm để chạy benchmark không qua API/auth/DB. |
| `docs/benchmark-cost-first-external.md` | Native cost-first report. |
| `docs/benchmark-fairness-tuned-external.md` | Native fairness-selected report. |
| `.gitignore` | Ignore `.buildtmp/`. |

Không sửa trực tiếp trong repo outsource. Các repo external vẫn clean:

```text
E:\Code\BeGo\.buildtmp\external\or-tools
E:\Code\BeGo\.buildtmp\external\vroom
E:\Code\BeGo\.buildtmp\external\jsprit
E:\Code\BeGo\.buildtmp\external\pyvrp
```

## Cách tự confirm benchmark đúng

### 1. Kiểm tra external repos không bị sửa

```powershell
cd E:\Code\BeGo

git -C .buildtmp\external\or-tools status --short
git -C .buildtmp\external\vroom status --short
git -C .buildtmp\external\jsprit status --short
git -C .buildtmp\external\pyvrp status --short
```

Expected: không in gì, tức repo clean.

### 2. Kiểm tra PyVRP native package

```powershell
python -m pip show pyvrp
```

Expected: có package `pyvrp`.

### 3. Build và chạy native benchmark

```powershell
dotnet build src\OptiGo.slnx
dotnet run --project .buildtmp\BenchmarkRunner\BenchmarkRunner.csproj -- 20260505 18 .buildtmp\benchmark-native-confirm.json
```

Expected algorithms:

```text
ortools_pickup_cost
ortools_pickup_fair
pyvrp_hgs_cost
pyvrp_hgs_fair
optigo_hybrid
median_nearest
```

Không được có:

```text
vroom_cost_adapter
jsprit_fair_adapter
```

### 4. Kiểm tra aggregate

```powershell
$r = Get-Content .buildtmp\benchmark-native-confirm.json -Raw | ConvertFrom-Json
$r.aggregates |
  Sort-Object averagePureCostSeconds |
  Select-Object algorithmKey, serviceableRuns, serviceableFeasibleRate,
    averagePureCostSeconds, averageFairnessScoreSeconds,
    averageCostGapToBestExternalPercent,
    averageFairnessGainVsBestCostExternalPercent |
  Format-Table -AutoSize
```

Với seed `20260505`, count `18`, expected `serviceableRuns = 15`.

### 5. Kiểm tra từng scenario dùng cùng input

```powershell
$s = $r.scenarios | Where-Object scenarioId -eq "S001"
$s.members | Format-Table name, role, transportMode, seatCapacity, latitude, longitude
$s.venues | Format-Table venueId, name, rating, latitude, longitude
$s.runs |
  Sort-Object pureCostSeconds |
  Select-Object algorithmKey, isFeasible, pureCostSeconds, fairnessScoreSeconds,
    maxMemberBurdenSeconds, worstMemberRegretSeconds,
    passengerBurdenGini, maxDriverDetourSeconds, driverDetourGini |
  Format-Table -AutoSize
```

## Việc còn lại để benchmark native đầy đủ hơn

1. Tích hợp VROOM native khi Docker daemon/native binary chạy được.
2. Tạo jsprit Java runner bằng Maven/Gradle hoặc build jar riêng.
3. Tối ưu PyVRP bridge để solve nhiều venue trong một Python process thay vì một subprocess mỗi venue.
4. Chạy thêm sensitivity: scenario count `36`, OR-Tools/PyVRP time limit `0.5s`, `1s`, `3s`.
