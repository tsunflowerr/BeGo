# Native Cost-first External Benchmark

## Mục tiêu

Report này chỉ dùng external solver chạy native/core thật. Các adapter `VROOM-style` và `jsprit-style` đã bị loại khỏi benchmark mặc định để tránh so sánh giả. Mục tiêu là so OptiGo với external solver tối ưu cost trước, sau đó chấm lại bằng evaluator và fairness metrics của OptiGo.

## Cách chạy

```powershell
cd E:\Code\BeGo
dotnet run --project .buildtmp\BenchmarkRunner\BenchmarkRunner.csproj -- 20260505 18 .buildtmp\benchmark-native-18.json
```

- Seed: `20260505`
- Scenario count: `18`
- Serviceable scenarios: `15/18`
- Raw output: `.buildtmp\benchmark-native-18.json`
- Chỉ lấy kết luận chính trên serviceable scenarios.

## Native external solvers

| Solver | Trạng thái | Cách chạy |
| --- | --- | --- |
| OR-Tools | Native thật | Gọi `Google.OrTools` NuGet trong .NET, dùng `RoutingModel`, capacity dimension và time dimension. |
| PyVRP | Native thật | C# xuất matrix/input JSON, Python bridge gọi `pyvrp` HGS thật, rồi normalize route output về evaluator OptiGo. |
| VROOM | Chưa report | Source đã pull, nhưng Docker/native binary chưa chạy được trong môi trường hiện tại. Không dùng số liệu style adapter nữa. |
| jsprit | Chưa report | Source đã pull, nhưng chưa có Maven/Gradle runner native. Không dùng số liệu style adapter nữa. |

## Kết quả cost-first

| Algorithm | Serviceable feasible | Avg pure cost | Avg fairness | Avg max burden | Avg worst regret | Passenger Gini | Max driver detour | Driver Gini | Avg runtime |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| OR-Tools pickup VRP cost-first | 73.3% | 4362.3s | 1543.1s | 1938.2s | 1388.6s | 0.148 | 711.8s | 0.554 | 1081.2ms |
| PyVRP HGS cost-first | 66.7% | 4398.1s | 1557.5s | 1908.6s | 1344.0s | 0.146 | 683.0s | 0.552 | 4740.0ms |
| OptiGo Hybrid route-pool Pareto | 80.0% | 4637.5s | 1197.6s | 1655.1s | 986.5s | 0.054 | 526.6s | 0.389 | 161.9ms |

## Diễn giải

So với OR-Tools cost-first, OptiGo trả thêm `+6.3%` pure cost nhưng giảm `22.4%` fairness score. Passenger Gini giảm `63.8%`, max driver detour giảm `26.0%`. Đây là tradeoff đúng với mục tiêu của dự án: không thắng cost tuyệt đối, nhưng mua được fairness rõ ràng.

So với PyVRP HGS cost-first, OptiGo trả thêm `+5.4%` pure cost nhưng giảm `23.1%` fairness score. Đây là baseline cost-first native quan trọng hơn các adapter cũ vì PyVRP đang chạy thuật toán HGS thật.

## Kết luận

Với cost-first native external, claim hợp lệ là:

> OptiGo không tối ưu cost thuần. Trên benchmark native hiện tại, OptiGo chấp nhận cost cao hơn nhẹ so với external cost-first, đổi lại giảm đáng kể burden, regret, passenger inequality và driver detour.

Không dùng câu: “OptiGo đánh bại VROOM/jsprit”, vì hai solver đó chưa chạy native trong benchmark này.
