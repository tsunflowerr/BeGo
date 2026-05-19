# Native Fairness-selected External Benchmark

## Mục tiêu

Report này so sánh OptiGo với các external native solver khi kết quả của external cũng được chọn theo fairness trong cost guard. External solver vẫn solve bằng core thuật toán thật; phần fairness selection chỉ là bước chọn candidate sau solve, dùng cùng evaluator của OptiGo.

Cost guard:

```text
bestPureCost * 1.08 + 90s
```

## Cách chạy

```powershell
cd E:\Code\BeGo
dotnet run --project .buildtmp\BenchmarkRunner\BenchmarkRunner.csproj -- 20260505 18 .buildtmp\benchmark-native-18.json
```

- Seed: `20260505`
- Scenario count: `18`
- Serviceable scenarios: `15/18`
- Raw output: `.buildtmp\benchmark-native-18.json`

## Fairness-selected native baselines

| Solver | Native/core solve | Fairness step |
| --- | --- | --- |
| OR-Tools pickup VRP fairness-tuned | OR-Tools `RoutingModel` thật | Chọn venue candidate có fairness tốt nhất trong cost guard. |
| PyVRP HGS fairness-selected | PyVRP HGS thật qua Python bridge | Chọn venue candidate có fairness tốt nhất trong cost guard. |
| OptiGo Hybrid | OptiGo route-pool/shared-stop planner | Chọn route-pool Pareto theo burden, regret, Gini, detour và cost guard. |

## Kết quả

| Algorithm | Serviceable feasible | Avg pure cost | Avg fairness | Avg max burden | Avg worst regret | Passenger Gini | Max driver detour | Driver Gini | Avg runtime |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| OR-Tools pickup VRP fairness-tuned | 73.3% | 4455.7s | 1464.1s | 1798.6s | 1185.4s | 0.117 | 667.7s | 0.527 | 1067.1ms |
| PyVRP HGS fairness-selected | 73.3% | 4454.8s | 1508.0s | 1848.3s | 1251.3s | 0.142 | 683.6s | 0.533 | 4570.3ms |
| OptiGo Hybrid route-pool Pareto | 80.0% | 4637.5s | 1197.6s | 1655.1s | 986.5s | 0.054 | 526.6s | 0.389 | 161.9ms |

## Diễn giải

So với OR-Tools fairness-tuned, OptiGo trả thêm `+4.1%` pure cost nhưng giảm `18.2%` fairness score. Passenger Gini giảm `54.4%`, max driver detour giảm `21.1%`.

So với PyVRP HGS fairness-selected, OptiGo trả thêm `+4.1%` pure cost nhưng giảm `20.6%` fairness score. Passenger Gini giảm `62.2%`, max driver detour giảm `23.0%`.

## Weakness còn lại

Raw benchmark vẫn chỉ ra các case cần cải tiến: `S014/spread` có worst regret cao hơn OR-Tools fairness-tuned khoảng `42.8%`, và `S003/corridor` có max driver detour cao hơn OR-Tools cost-first khoảng `23.7%`. Đây là các case nên dùng để cải tiến tiếp: thêm local-search operator swap/relocate passenger giữa driver sau khi chọn route-pool.

## Kết luận

Với native external fairness-selected, claim hợp lệ là:

> Khi external solver cũng được phép chọn candidate theo fairness trong cost guard, OptiGo vẫn giữ lợi thế fairness rõ ràng, đặc biệt ở passenger inequality, worst regret và driver detour. Cost tradeoff nằm trong vùng nhỏ và phù hợp mục tiêu fairness-first của dự án.
