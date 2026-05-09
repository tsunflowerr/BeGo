"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useMemo, useState } from "react";
import { useGeolocation } from "@/hooks";
import { api } from "@/lib/api";
import { MemberMobilityRole, TransportMode, transportModeLabels } from "@/types";

const activeCapabilities = [
  "Create meeting room by real location",
  "Members needing pickup and drivers with seats",
  "Optimize location by travel time",
  "Real-time voting and route locking",
  "Benchmark outing algorithm",
];

const transportModes = [
  TransportMode.Walking,
  TransportMode.Cycling,
  TransportMode.Motorbike,
  TransportMode.Car,
  TransportMode.Bus,
];

export default function Home() {
  const router = useRouter();
  const [isOpen, setIsOpen] = useState(false);
  const {
    latitude,
    longitude,
    error: locationError,
    loading: locationLoading,
    refresh,
  } = useGeolocation({ enabled: isOpen });

  const location = useMemo(
    () => (latitude !== null && longitude !== null ? { latitude, longitude } : null),
    [latitude, longitude]
  );

  const handleCreate = useCallback(
    async (payload: CreateRoomDraft) => {
      const response = await api.sessions.create(payload);
      localStorage.setItem(`room-${response.sessionId}-memberId`, response.hostMemberId);
      router.push(`/room/${response.sessionId}`);
    },
    [router]
  );

  return (
    <main className="min-h-screen px-4 py-5 text-[#172033] sm:px-6 lg:px-8">
      <div className="mx-auto flex max-w-7xl flex-col gap-6">
        <header className="bego-hard-card flex flex-col gap-4 px-4 py-4 sm:flex-row sm:items-center sm:justify-between">
          <Link href="/" className="flex items-center gap-3 font-black">
            <span className="grid h-11 w-11 place-items-center rounded-full border-2 border-[#172033] bg-[#f7c948] text-lg shadow-[3px_3px_0_#172033]">
              B
            </span>
            <span className="text-xl tracking-[0]">BeGo</span>
          </Link>
          <nav className="flex flex-wrap gap-2">
            <Link className="bego-secondary inline-flex items-center" href="/benchmark">
              Benchmark
            </Link>
            <button type="button" className="bego-primary" onClick={() => setIsOpen(true)}>
              Create room
            </button>
          </nav>
        </header>

        <section className="grid gap-6 lg:grid-cols-[1.04fr_0.96fr] lg:items-stretch">
          <div className="bego-hard-card relative overflow-hidden bg-[#fffdf5] p-6 sm:p-8 lg:min-h-[620px]">
            <span className="bego-chip bg-[#45d483]">Working outing optimizer</span>
            <h1 className="mt-6 max-w-3xl text-5xl font-black leading-[0.96] tracking-[0] sm:text-7xl">
              Meet up faster, fairer.
            </h1>
            <p className="mt-5 max-w-2xl text-base font-semibold text-[#475569] sm:text-lg">
              BeGo collects group locations, handles people needing pickups, finds suitable locations, allows group voting and locks the route before departing.
            </p>

            <div className="mt-7 flex flex-wrap gap-3">
              <button type="button" className="bego-primary" onClick={() => setIsOpen(true)}>
                Start with current location
              </button>
              <Link href="/benchmark" className="bego-secondary inline-flex items-center">
                Open benchmark lab
              </Link>
            </div>

            <div className="mt-8 grid gap-3 sm:grid-cols-2">
              {activeCapabilities.map((capability, index) => (
                <div
                  key={capability}
                  className="border-2 border-[#172033] bg-white p-4 shadow-[4px_4px_0_#d8e3ea]"
                >
                  <p className="text-xs font-black uppercase text-[#64748b]">Feature {index + 1}</p>
                  <p className="mt-2 font-black">{capability}</p>
                </div>
              ))}
            </div>
          </div>

          <div className="bego-hard-card relative overflow-hidden bg-[#48c7df] p-5">
            <div className="grid h-full min-h-[560px] grid-rows-[auto_1fr_auto] gap-4">
              <div className="rounded-[28px] border-2 border-[#172033] bg-white p-5 shadow-[6px_6px_0_#172033]">
                <p className="text-xs font-black uppercase text-[#64748b]">Live flow</p>
                <h2 className="mt-2 text-3xl font-black tracking-[0]">From creating a room to locking the route</h2>
              </div>

              <div className="relative rounded-[34px] border-2 border-[#172033] bg-[#fff7dc] p-5 shadow-[8px_8px_0_#172033]">
                <div className="absolute left-[14%] top-[16%] h-16 w-16 rounded-full border-2 border-[#172033] bg-[#ff3b1f] shadow-[4px_4px_0_#172033]" />
                <div className="absolute right-[16%] top-[28%] h-14 w-14 rounded-full border-2 border-[#172033] bg-[#45d483] shadow-[4px_4px_0_#172033]" />
                <div className="absolute bottom-[22%] left-[28%] h-14 w-14 rounded-full border-2 border-[#172033] bg-[#f472b6] shadow-[4px_4px_0_#172033]" />
                <div className="absolute bottom-[18%] right-[18%] h-20 w-20 rounded-full border-2 border-[#172033] bg-[#f7c948] shadow-[4px_4px_0_#172033]" />
                <div className="absolute left-[22%] right-[22%] top-[48%] h-3 -rotate-6 rounded-full border-2 border-[#172033] bg-white" />
                <div className="absolute bottom-[35%] left-[38%] right-[18%] h-3 rotate-12 rounded-full border-2 border-[#172033] bg-white" />
                <div className="absolute inset-x-7 bottom-7 rounded-2xl border-2 border-[#172033] bg-white p-4 shadow-[5px_5px_0_#172033]">
                  <p className="text-xs font-black uppercase text-[#64748b]">Result</p>
                  <p className="mt-1 text-lg font-black">Top locations, voting, pickup points, and route preview.</p>
                </div>
              </div>

              <div className="grid grid-cols-3 gap-3">
                {["Location", "Optimize", "Depart"].map((item) => (
                  <div key={item} className="rounded-2xl border-2 border-[#172033] bg-white p-3 text-center font-black">
                    {item}
                  </div>
                ))}
              </div>
            </div>
          </div>
        </section>
      </div>

      <CreateRoomModal
        isOpen={isOpen}
        location={location}
        locationError={locationError}
        locationLoading={locationLoading}
        onRequestLocation={refresh}
        onClose={() => setIsOpen(false)}
        onCreate={handleCreate}
      />
    </main>
  );
}

interface CreateRoomDraft {
  hostName: string;
  defaultQuery: string;
  latitude: number;
  longitude: number;
  transportMode: TransportMode;
  mobilityRole: MemberMobilityRole;
}

function CreateRoomModal({
  isOpen,
  location,
  locationError,
  locationLoading,
  onRequestLocation,
  onClose,
  onCreate,
}: {
  isOpen: boolean;
  location: { latitude: number; longitude: number } | null;
  locationError: string | null;
  locationLoading: boolean;
  onRequestLocation: () => void;
  onClose: () => void;
  onCreate: (draft: CreateRoomDraft) => Promise<void>;
}) {
  const [hostName, setHostName] = useState("");
  const [defaultQuery, setDefaultQuery] = useState("quiet cafe with wifi");
  const [mobilityRole, setMobilityRole] = useState<MemberMobilityRole>(MemberMobilityRole.SelfTravel);
  const [transportMode, setTransportMode] = useState<TransportMode>(TransportMode.Motorbike);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!isOpen) {
    return null;
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const cleanName = hostName.trim();
    const cleanQuery = defaultQuery.trim();

    if (cleanName.length < 2) {
      setError("Name must be at least 2 characters.");
      return;
    }

    if (!location) {
      setError("Location permission is required before creating a room.");
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      await onCreate({
        hostName: cleanName,
        defaultQuery: cleanQuery,
        latitude: location.latitude,
        longitude: location.longitude,
        transportMode: mobilityRole === MemberMobilityRole.NeedsPickup ? TransportMode.Walking : transportMode,
        mobilityRole,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create room.");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-[#172033]/55 p-4" onMouseDown={(event) => event.target === event.currentTarget && !isSubmitting && onClose()}>
      <form onSubmit={submit} className="bego-hard-card max-h-[calc(100vh-32px)] w-full max-w-2xl overflow-y-auto bg-white p-5 sm:p-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <span className="bego-chip bg-[#f7c948]">New room</span>
            <h2 className="mt-3 text-3xl font-black">Create meeting group</h2>
          </div>
          <button type="button" className="bego-secondary px-4" onClick={onClose} disabled={isSubmitting}>
            Close
          </button>
        </div>

        <div className="mt-5 grid gap-4 sm:grid-cols-2">
          <label className="grid gap-2 font-bold">
            Your name
            <input className="bego-input" value={hostName} onChange={(event) => setHostName(event.target.value)} placeholder="Example: Quang" autoFocus />
          </label>
          <label className="grid gap-2 font-bold">
            Mobility role
            <select className="bego-input" value={mobilityRole} onChange={(event) => setMobilityRole(event.target.value as MemberMobilityRole)}>
              <option value={MemberMobilityRole.SelfTravel}>Self travel / can pick up others</option>
              <option value={MemberMobilityRole.NeedsPickup}>Needs pickup</option>
            </select>
          </label>
        </div>

        <label className="mt-4 grid gap-2 font-bold">
          Search criteria
          <textarea
            className="bego-input bego-textarea"
            value={defaultQuery}
            onChange={(event) => setDefaultQuery(event.target.value)}
            placeholder="Example: good hotpot restaurant, easy parking, suitable for 5 people"
            maxLength={500}
          />
        </label>

        {mobilityRole === MemberMobilityRole.SelfTravel && (
          <div className="mt-4 grid gap-2">
            <p className="font-bold">Transport mode</p>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
              {transportModes.map((mode) => (
                <button
                  key={mode}
                  type="button"
                  onClick={() => setTransportMode(mode)}
                  className={`min-h-14 rounded-2xl border-2 border-[#172033] px-2 font-black ${
                    transportMode === mode ? "bg-[#45d483]" : "bg-white"
                  }`}
                >
                  {transportModeLabels[mode]}
                </button>
              ))}
            </div>
          </div>
        )}

        <div className="mt-4 rounded-2xl border-2 border-[#172033] bg-[#f6fcff] p-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="font-black">Current location</p>
              <p className="mt-1 text-sm font-semibold text-[#64748b]">
                {location
                  ? `${location.latitude.toFixed(5)}, ${location.longitude.toFixed(5)}`
                  : locationLoading
                    ? "Getting location..."
                    : locationError || "No location"}
              </p>
            </div>
            <button type="button" className="bego-secondary" onClick={onRequestLocation} disabled={locationLoading || isSubmitting}>
              Get location
            </button>
          </div>
        </div>

        {error && <div className="mt-4 rounded-2xl border-2 border-[#d42712] bg-[#fff1f2] p-3 font-bold text-[#b42318]">{error}</div>}

        <button type="submit" className="bego-primary mt-5 w-full" disabled={isSubmitting}>
          {isSubmitting ? "Creating room..." : "Create room and enter coordination"}
        </button>
      </form>
    </div>
  );
}
