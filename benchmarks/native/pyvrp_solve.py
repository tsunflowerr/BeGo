import json
import sys
from pathlib import Path

from pyvrp import Model
from pyvrp.stop import MaxRuntime


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: pyvrp_solve.py <input.json> <output.json>", file=sys.stderr)
        return 2

    input_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2])
    data = json.loads(input_path.read_text(encoding="utf-8"))

    model = Model()
    nodes = data["nodes"]
    drivers = data["drivers"]
    passengers = data["passengers"]
    duration_profiles = data.get("durationProfiles")
    if duration_profiles is None:
        duration_profiles = [data["durations"] for _ in drivers]
    venue_index = data["venueNode"]

    depots = {}
    for driver in drivers:
        node = nodes[driver["node"]]
        depots[("driver", driver["index"])] = model.add_depot(
            node["x"],
            node["y"],
            name=f"driver:{driver['index']}",
        )

    venue = nodes[venue_index]
    end_depot = model.add_depot(venue["x"], venue["y"], name="venue")

    clients = {}
    for passenger in passengers:
        node = nodes[passenger["node"]]
        clients[passenger["index"]] = model.add_client(
            node["x"],
            node["y"],
            pickup=1,
            service_duration=data.get("serviceSeconds", 0),
            required=True,
            name=f"passenger:{passenger['index']}",
        )

    profiles = [
        model.add_profile(name=f"profile:{idx}") for idx in range(len(duration_profiles))
    ]

    for driver in drivers:
        model.add_vehicle_type(
            num_available=1,
            capacity=driver["capacity"],
            start_depot=depots[("driver", driver["index"])],
            end_depot=end_depot,
            unit_distance_cost=1,
            unit_duration_cost=0,
            profile=profiles[driver.get("profile", driver["index"])],
            name=f"driver:{driver['index']}",
        )

    objects = []
    object_nodes = []
    for driver in drivers:
        objects.append(depots[("driver", driver["index"])])
        object_nodes.append(driver["node"])
    objects.append(end_depot)
    object_nodes.append(venue_index)
    for passenger in passengers:
        objects.append(clients[passenger["index"]])
        object_nodes.append(passenger["node"])

    for profile_idx, durations in enumerate(duration_profiles):
        profile = profiles[profile_idx]
        for i, frm in enumerate(objects):
            from_node = object_nodes[i]
            for j, to in enumerate(objects):
                if i == j:
                    continue

                to_node = object_nodes[j]
                duration = max(1, int(round(durations[from_node][to_node])))
                model.add_edge(
                    frm,
                    to,
                    distance=duration,
                    duration=duration,
                    profile=profile,
                )

    result = model.solve(
        MaxRuntime(float(data.get("timeLimitSeconds", 0.15))),
        seed=int(data.get("seed", 1)),
        display=False,
    )
    solution = result.best

    routes = []
    assigned = set()
    for route in solution.routes():
        driver_index = int(route.vehicle_type())
        passenger_indices = []
        for visit in route.visits():
            passenger_index = int(visit) - len(drivers) - 1
            if 0 <= passenger_index < len(passengers):
                passenger_indices.append(passenger_index)
                assigned.add(passenger_index)

        routes.append(
            {
                "driverIndex": driver_index,
                "passengerIndices": passenger_indices,
            }
        )

    payload = {
        "isFeasible": bool(solution.is_feasible()) and len(assigned) == len(passengers),
        "objective": int(result.cost()),
        "routes": routes,
        "missingPassengerIndices": [
            idx for idx in range(len(passengers)) if idx not in assigned
        ],
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
