import json
import os
import shutil
import subprocess
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: vroom_solve.py <input.json> <output.json>", file=sys.stderr)
        return 2

    input_path = Path(sys.argv[1]).resolve()
    output_path = Path(sys.argv[2]).resolve()
    data = json.loads(input_path.read_text(encoding="utf-8"))
    vroom_input = build_vroom_input(data)
    vroom_input_path = output_path.with_suffix(".vroom-input.json")
    vroom_output_path = output_path.with_suffix(".vroom-output.json")
    vroom_input_path.write_text(json.dumps(vroom_input, indent=2), encoding="utf-8")

    command = build_command(vroom_input_path, vroom_output_path)
    if command is None:
        write_output(
            output_path,
            False,
            [],
            "VROOM binary not found. Set VROOM_BIN or VROOM_DOCKER_IMAGE.",
        )
        return 0

    result = subprocess.run(command, capture_output=True, text=True, timeout=60)
    if result.returncode != 0:
        write_output(
            output_path,
            False,
            [],
            f"VROOM exited with {result.returncode}: {result.stderr.strip()}",
        )
        return 0

    if not vroom_output_path.exists():
        write_output(output_path, False, [], "VROOM did not produce an output file.")
        return 0

    solution = json.loads(vroom_output_path.read_text(encoding="utf-8"))
    passengers = data["passengers"]
    routes = []
    assigned = set()
    for route in solution.get("routes", []):
        driver_index = int(route["vehicle"])
        passenger_indices = []
        for step in route.get("steps", []):
            if step.get("type") != "job":
                continue

            passenger_index = int(step["id"])
            if 0 <= passenger_index < len(passengers):
                passenger_indices.append(passenger_index)
                assigned.add(passenger_index)

        routes.append(
            {
                "driverIndex": driver_index,
                "passengerIndices": passenger_indices,
            }
        )

    missing = [idx for idx in range(len(passengers)) if idx not in assigned]
    unassigned = solution.get("unassigned", [])
    write_output(
        output_path,
        len(missing) == 0 and len(unassigned) == 0,
        routes,
        None if len(missing) == 0 and len(unassigned) == 0 else "VROOM left jobs unassigned.",
        missing,
    )
    return 0


def build_vroom_input(data):
    nodes = data["nodes"]
    drivers = data["drivers"]
    passengers = data["passengers"]
    duration_profiles = data.get("durationProfiles")
    if duration_profiles is None:
        duration_profiles = [data["durations"] for _ in drivers]
    venue_index = data["venueNode"]
    service_seconds = int(round(data.get("serviceSeconds", 0)))

    vehicles = []
    matrices = {}
    for driver in drivers:
        profile = f"profile_{driver['index']}"
        vehicles.append(
            {
                "id": driver["index"],
                "profile": profile,
                "start_index": driver["node"],
                "end_index": venue_index,
                "capacity": [driver["capacity"]],
            }
        )
        matrices[profile] = {
            "durations": to_integer_matrix(duration_profiles[driver.get("profile", driver["index"])])
        }

    jobs = []
    for passenger in passengers:
        jobs.append(
            {
                "id": passenger["index"],
                "location_index": passenger["node"],
                "pickup": [1],
                "service": service_seconds,
            }
        )

    return {
        "vehicles": vehicles,
        "jobs": jobs,
        "matrices": matrices,
    }


def to_integer_matrix(matrix):
    return [[max(0, int(round(value))) for value in row] for row in matrix]


def build_command(input_path: Path, output_path: Path):
    vroom_bin = os.environ.get("VROOM_BIN") or shutil.which("vroom")
    if vroom_bin:
        return [vroom_bin, "-i", str(input_path), "-o", str(output_path)]

    image = os.environ.get("VROOM_DOCKER_IMAGE")
    if not image:
        return None

    mount_dir = input_path.parent
    return [
        "docker",
        "run",
        "--rm",
        "--entrypoint",
        "/usr/local/bin/vroom",
        "-v",
        f"{mount_dir}:/data",
        image,
        "-i",
        f"/data/{input_path.name}",
        "-o",
        f"/data/{output_path.name}",
    ]


def write_output(output_path, is_feasible, routes, bridge_error=None, missing=None):
    payload = {
        "isFeasible": bool(is_feasible),
        "routes": routes,
        "missingPassengerIndices": missing or [],
        "bridgeError": bridge_error,
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
