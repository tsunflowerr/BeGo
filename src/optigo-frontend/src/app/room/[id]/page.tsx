"use client";

/* eslint-disable @next/next/no-img-element */

import Link from "next/link";
import { useParams } from "next/navigation";
import { FormEvent, ReactNode, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { signIn, signOut as signOutGoogle, useSession as useAuthSession } from "next-auth/react";
import { api } from "@/lib/api";
import {
  buildShareUrl,
  canShowLocalTestTools,
  formatCompactDistance,
  formatCompactDuration,
  getStatusMeta,
} from "@/lib/frontend-utils";
import { useGeolocation, useSession as useRoomSession } from "@/hooks";
import {
  Member,
  MemberMobilityRole,
  PickupRequest,
  PickupRequestStatus,
  PickupSuggestion,
  SessionStatus,
  TransportMode,
  Venue,
  formatDistance,
  formatDuration,
  mobilityRoleLabels,
  transportModeLabels,
} from "@/types";

const transportModes = [
  TransportMode.Walking,
  TransportMode.Cycling,
  TransportMode.Motorbike,
  TransportMode.Car,
  TransportMode.Bus,
];

const hanoiSeeds = [
  { label: "Hoan Kiem", latitude: 21.0285, longitude: 105.8542 },
  { label: "Ba Dinh", latitude: 21.0367, longitude: 105.8348 },
  { label: "Dong Da", latitude: 21.018, longitude: 105.8292 },
  { label: "Cau Giay", latitude: 21.0362, longitude: 105.7902 },
  { label: "Tay Ho", latitude: 21.07, longitude: 105.8188 },
  { label: "Hai Ba Trung", latitude: 21.0055, longitude: 105.8577 },
];

export default function RoomPage() {
  const params = useParams();
  const auth = useAuthSession();
  const sessionId = typeof params.id === "string" ? params.id : Array.isArray(params.id) ? params.id[0] ?? "" : "";
  const [memberId, setMemberId] = useState<string | null>(null);
  const [hasJoined, setHasJoined] = useState(false);
  const [showJoin, setShowJoin] = useState(false);
  const [showTestMember, setShowTestMember] = useState(false);
  const [selectedVenueId, setSelectedVenueId] = useState<string | null>(null);
  const [hostname, setHostname] = useState("");
  const toast = useToastStack();
  const previousStatus = useRef<SessionStatus | null>(null);
  const previousMemberCount = useRef(0);

  const location = useGeolocation();
  const sessionState = useRoomSession({ sessionId, memberId });
  const {
    session,
    members,
    pickupRequests,
    pickupSuggestions,
    isHost,
    currentMember,
    optimizationResult,
    topVenues,
    winningVenueId,
    finalRoutePreview,
    votingProgress,
    hasVoted,
    loading,
    error,
    status,
    isComputing,
    isVoting,
    isRoutePreview,
    isCompleted,
    isConnected,
    refreshSession,
    startOptimization,
    submitVote,
    acceptPickupRequest,
    releasePickupRequest,
    lockDeparture,
  } = sessionState;

  useEffect(() => {
    const timer = window.setTimeout(() => {
      const stored = window.localStorage.getItem(`room-${sessionId}-memberId`);
      setHostname(window.location.hostname);
      if (stored) {
        setMemberId(stored);
        setHasJoined(true);
      } else {
        setShowJoin(true);
      }
    }, 0);

    return () => window.clearTimeout(timer);
  }, [sessionId]);

  useEffect(() => {
    if (previousStatus.current !== null && previousStatus.current !== status) {
      toast.push(getStatusMeta(status).action);
    }
    previousStatus.current = status;
  }, [status, toast]);

  useEffect(() => {
    if (members.length > previousMemberCount.current && previousMemberCount.current > 0) {
      const newest = members[members.length - 1];
      if (newest && newest.id !== memberId) {
        toast.push(`${newest.name} has joined the room`);
      }
    }
    previousMemberCount.current = members.length;
  }, [members, memberId, toast]);

  const currentVoteVenueId = memberId ? session?.votes?.find((vote) => vote.memberId === memberId)?.venueId : null;
  const displaySelectedVenueId = currentVoteVenueId ?? selectedVenueId;
  const winningVenue = winningVenueId ? topVenues.find((venue) => venue.venueId === winningVenueId) ?? null : null;
  const routeVenue = finalRoutePreview ?? winningVenue;
  const visibleVenues = isRoutePreview || isCompleted ? (routeVenue ? [routeVenue] : topVenues) : topVenues;
  const unresolvedPickupCount = pickupRequests.filter((request) => request.status === PickupRequestStatus.Pending).length;
  const statusMeta = getStatusMeta(status);
  const canShowTestTools = canShowLocalTestTools({
    nodeEnv: process.env.NODE_ENV,
    hostname,
    hasJoined,
    isHost,
    status,
  });

  const switchRoomUser = useCallback(() => {
    window.localStorage.removeItem(`room-${sessionId}-memberId`);
    setMemberId(null);
    setHasJoined(false);
    setShowJoin(true);
  }, [sessionId]);

  const joinRoom = useCallback(
    async (payload: JoinDraft) => {
      const response = await api.sessions.join(sessionId, payload);
      window.localStorage.setItem(`room-${sessionId}-memberId`, response.memberId);
      setMemberId(response.memberId);
      setHasJoined(true);
      setShowJoin(false);
      toast.push(`Joined the room as ${payload.memberName}`);
      await refreshSession();
    },
    [refreshSession, sessionId, toast]
  );

  const runOptimization = useCallback(async () => {
    toast.push("Optimizing locations and routes");
    await startOptimization(session?.queryText || undefined);
  }, [session?.queryText, startOptimization, toast]);

  const voteVenue = useCallback(
    async (venueId: string) => {
      setSelectedVenueId(venueId);
      await submitVote(venueId);
      toast.push("Vote submitted");
    },
    [submitVote, toast]
  );

  const impersonate = useCallback(
    (nextMemberId: string) => {
      const nextMember = members.find((member) => member.id === nextMemberId);
      const existingVote = session?.votes?.find((vote) => vote.memberId === nextMemberId);
      setMemberId(nextMemberId);
      setHasJoined(true);
      setSelectedVenueId(existingVote?.venueId ?? null);
      window.localStorage.setItem(`room-${sessionId}-memberId`, nextMemberId);
      toast.push(`Dev impersonating ${nextMember?.name ?? "member"}`);
    },
    [members, session?.votes, sessionId, toast]
  );

  if (loading) {
    return <StateScreen title="Loading room" detail="Syncing session, members, and real-time channel." />;
  }

  if (error && !session) {
    return <StateScreen title="Room not found" detail={error} action={<Link className="bego-primary inline-flex items-center" href="/">Back to home</Link>} />;
  }

  return (
    <main className="min-h-screen px-4 py-5 text-[#172033] sm:px-6 lg:px-8">
      <div className="mx-auto flex max-w-[1500px] flex-col gap-5">
        <header className="bego-hard-card flex flex-col gap-4 px-4 py-4 lg:flex-row lg:items-center lg:justify-between">
          <div className="flex flex-wrap items-center gap-3">
            <Link href="/" className="grid h-11 w-11 place-items-center rounded-full border-2 border-[#172033] bg-[#f7c948] text-lg font-black shadow-[3px_3px_0_#172033]">
              B
            </Link>
            <div>
              <p className="text-xs font-black uppercase text-[#64748b]">Room {sessionId.slice(0, 8)}</p>
              <h1 className="text-2xl font-black tracking-[0]">{session?.queryText || "Find meeting point"}</h1>
            </div>
            <span className={`bego-chip ${statusToneClass(statusMeta.tone)}`}>{statusMeta.label}</span>
            <span className={`bego-chip ${isConnected ? "bg-[#45d483]" : "bg-white"}`}>{isConnected ? "Realtime on" : "Reconnecting"}</span>
          </div>
          <div className="flex flex-wrap gap-2">
            <Link className="bego-secondary inline-flex items-center" href="/benchmark">Benchmark</Link>
            {currentMember && <span className="bego-chip bg-white">You: {currentMember.name}</span>}
            {auth.status === "authenticated" ? (
              <button type="button" className="bego-secondary inline-flex items-center gap-2" onClick={() => void signOutGoogle()}>
                {auth.data.user?.image && (
                  <img src={auth.data.user.image} alt="" className="h-6 w-6 rounded-full border-2 border-[#172033]" referrerPolicy="no-referrer" />
                )}
                Sign out
              </button>
            ) : (
              <button type="button" className="bego-secondary" onClick={() => void signIn("google")}>Sign in with Google</button>
            )}
            <button type="button" className="bego-secondary" onClick={switchRoomUser}>Switch room user</button>
          </div>
        </header>

        {error && <div className="rounded-2xl border-2 border-[#d42712] bg-[#fff1f2] p-3 font-bold text-[#b42318]">{error}</div>}

        <section className="grid gap-5 xl:grid-cols-[360px_minmax(0,1fr)_370px]">
          <aside className="grid gap-5">
            <ControlPanel
              sessionId={sessionId}
              queryText={session?.queryText || ""}
              status={status}
              isHost={isHost}
              membersCount={members.length}
              unresolvedPickupCount={unresolvedPickupCount}
              isComputing={isComputing}
              onRunOptimization={runOptimization}
              onQueryUpdated={refreshSession}
            />
            <SharePanel sessionId={sessionId} />
          </aside>

          <section className="grid gap-5">
            <div className="bego-hard-card h-[520px] overflow-hidden bg-white p-3">
              <LiveMap
                members={members}
                venues={visibleVenues}
                geometricMedian={optimizationResult?.geometricMedian}
                routeVenue={routeVenue}
                winningVenueId={winningVenueId ?? undefined}
                isLoading={isComputing}
              />
            </div>

            {isVoting && topVenues.length > 0 && (
              <VotingSection
                venues={topVenues}
                selectedVenueId={displaySelectedVenueId}
                hasVoted={hasVoted}
                currentMemberId={memberId ?? undefined}
                votingProgress={votingProgress}
                onVote={voteVenue}
              />
            )}

            {(isRoutePreview || isCompleted) && routeVenue && (
              <RouteSection
                venue={routeVenue}
                isHost={isHost}
                canLock={isRoutePreview}
                onLock={async () => {
                  await lockDeparture();
                  toast.push("Route locked");
                }}
              />
            )}
          </section>

          <aside className="grid gap-5 content-start">
            <MembersPanel
              members={members}
              hostMemberId={session?.members?.[0]?.id}
              currentMemberId={memberId ?? undefined}
              routeVenue={routeVenue}
              isSelectable={hostname === "localhost" && isVoting}
              onSelect={impersonate}
            />
            <PickupPanel
              members={members}
              pickupRequests={pickupRequests}
              pickupSuggestions={pickupSuggestions}
              currentMemberId={memberId ?? undefined}
              onAccept={async (requestId, driverId) => {
                await acceptPickupRequest(requestId, driverId);
                toast.push("Pickup accepted");
              }}
              onRelease={async (requestId) => {
                await releasePickupRequest(requestId);
                toast.push("Pickup released");
              }}
            />
          </aside>
        </section>
      </div>

      <JoinRoomModal
        isOpen={showJoin && !hasJoined}
        location={location.latitude !== null && location.longitude !== null ? { latitude: location.latitude, longitude: location.longitude } : null}
        locationError={location.error}
        locationLoading={location.loading}
        onJoin={joinRoom}
        onClose={() => setShowJoin(false)}
        onRequestLocation={location.refresh}
        authStatus={auth.status}
        authUser={{
          name: auth.data?.user?.name ?? "",
          image: auth.data?.user?.image ?? null,
        }}
      />

      {canShowTestTools && (
        <>
          <button
            type="button"
            className="bego-primary fixed bottom-6 right-6 z-40 h-14 w-14 rounded-full p-0 text-2xl"
            aria-label="Add test member"
            onClick={() => setShowTestMember(true)}
          >
            +
          </button>
          <TestMemberModal
            isOpen={showTestMember}
            sessionId={sessionId}
            memberCount={members.length}
            onClose={() => setShowTestMember(false)}
            onAdded={refreshSession}
          />
        </>
      )}

      <ToastShelf messages={toast.messages} onRemove={toast.remove} />
    </main>
  );
}

function StateScreen({ title, detail, action }: { title: string; detail: string; action?: ReactNode }) {
  return (
    <main className="grid min-h-screen place-items-center px-4">
      <section className="bego-hard-card max-w-lg bg-white p-8 text-center">
        <div className="mx-auto h-16 w-16 animate-spin rounded-full border-4 border-[#d8e3ea] border-t-[#ff3b1f]" />
        <h1 className="mt-5 text-3xl font-black">{title}</h1>
        <p className="mt-2 font-semibold text-[#64748b]">{detail}</p>
        {action && <div className="mt-5">{action}</div>}
      </section>
    </main>
  );
}

function ControlPanel({
  sessionId,
  queryText,
  status,
  isHost,
  membersCount,
  unresolvedPickupCount,
  isComputing,
  onRunOptimization,
  onQueryUpdated,
}: {
  sessionId: string;
  queryText: string;
  status: SessionStatus;
  isHost: boolean;
  membersCount: number;
  unresolvedPickupCount: number;
  isComputing: boolean;
  onRunOptimization: () => Promise<void>;
  onQueryUpdated: () => Promise<void>;
}) {
  const [draft, setDraft] = useState(queryText);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const statusMeta = getStatusMeta(status);
  const canEdit = isHost && status === SessionStatus.WaitingForMembers;

  useEffect(() => setDraft(queryText), [queryText]);

  async function saveQuery() {
    const next = draft.trim();
    if (next.length === 0) {
      setError("Enter search criteria before saving.");
      return;
    }

    setIsSaving(true);
    setError(null);
    try {
      await api.sessions.updateQuery(sessionId, next);
      await onQueryUpdated();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save query.");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <section className="bego-hard-card bg-white p-5">
      <span className={`bego-chip ${statusToneClass(statusMeta.tone)}`}>{statusMeta.label}</span>
      <h2 className="mt-4 text-3xl font-black leading-none">Coordination board</h2>
      <p className="mt-2 text-sm font-semibold text-[#64748b]">{statusMeta.action}</p>

      <div className="mt-5 grid grid-cols-2 gap-3">
        <MetricBox label="Members" value={String(membersCount)} tone="bg-[#45d483]" />
        <MetricBox label="Needs pickup" value={String(unresolvedPickupCount)} tone="bg-[#f7c948]" />
      </div>

      <label className="mt-5 grid gap-2 font-black">
        Search criteria
        <textarea
          className="bego-input bego-textarea"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          disabled={!canEdit || isSaving}
          maxLength={500}
        />
      </label>
      {error && <p className="mt-2 text-sm font-bold text-[#b42318]">{error}</p>}
      {canEdit && (
        <button type="button" className="bego-secondary mt-3 w-full" disabled={isSaving} onClick={saveQuery}>
          {isSaving ? "Saving..." : "Save criteria"}
        </button>
      )}

      {status === SessionStatus.WaitingForMembers && (
        <button type="button" className="bego-primary mt-4 w-full" disabled={!isHost || membersCount === 0 || isComputing} onClick={() => void onRunOptimization()}>
          {isHost ? (isComputing ? "Optimizing..." : "Run optimization") : "Waiting for host to run optimization"}
        </button>
      )}
    </section>
  );
}

function SharePanel({ sessionId }: { sessionId: string }) {
  const [copied, setCopied] = useState(false);
  const [url, setUrl] = useState("");

  useEffect(() => {
    const timer = window.setTimeout(() => setUrl(buildShareUrl(window.location.origin, sessionId)), 0);
    return () => window.clearTimeout(timer);
  }, [sessionId]);

  async function copy() {
    await navigator.clipboard.writeText(url);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1600);
  }

  return (
    <section className="bego-card bg-[#fff7dc] p-5">
      <h2 className="text-xl font-black">Invite link</h2>
      <input className="bego-input mt-3" readOnly value={url} />
      <button type="button" className="bego-secondary mt-3 w-full" onClick={() => void copy()}>
        {copied ? "Copied" : "Copy room link"}
      </button>
    </section>
  );
}

function MemberAvatar({ member, sizeClass }: { member: Pick<Member, "name" | "avatarUrl">; sizeClass: string }) {
  if (member.avatarUrl) {
    return <img src={member.avatarUrl} alt="" className={`${sizeClass} rounded-full object-cover`} referrerPolicy="no-referrer" />;
  }

  return <span className={`${sizeClass} grid place-items-center rounded-full`}>{member.name.slice(0, 1).toUpperCase()}</span>;
}

function ProfileAvatar({ name, image, sizeClass }: { name: string; image?: string | null; sizeClass: string }) {
  if (image) {
    return <img src={image} alt="" className={`${sizeClass} rounded-full border-2 border-[#172033] object-cover shadow-[3px_3px_0_#172033]`} referrerPolicy="no-referrer" />;
  }

  return (
    <span className={`${sizeClass} grid place-items-center rounded-full border-2 border-[#172033] bg-[#f7c948] font-black shadow-[3px_3px_0_#172033]`}>
      {(name || "U").slice(0, 1).toUpperCase()}
    </span>
  );
}

function MembersPanel({
  members,
  hostMemberId,
  currentMemberId,
  routeVenue,
  isSelectable,
  onSelect,
}: {
  members: Member[];
  hostMemberId?: string;
  currentMemberId?: string;
  routeVenue: Venue | null;
  isSelectable: boolean;
  onSelect: (memberId: string) => void;
}) {
  const routeByMember = useMemo(
    () => new Map(routeVenue?.memberRoutes.map((route) => [route.memberId, route]) ?? []),
    [routeVenue]
  );
  const sortedMembers = [...members].sort((a, b) => {
    if (a.id === hostMemberId) return -1;
    if (b.id === hostMemberId) return 1;
    return new Date(a.joinedAt).getTime() - new Date(b.joinedAt).getTime();
  });

  return (
    <section className="bego-hard-card bg-white p-5">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-xl font-black">Members</h2>
        <span className="bego-chip bg-[#45d483]">{members.length}</span>
      </div>
      <div className="bego-scrollbar mt-4 grid max-h-[440px] gap-3 overflow-y-auto pr-1">
        {sortedMembers.map((member) => {
          const route = routeByMember.get(member.id);
          const isCurrent = member.id === currentMemberId;
          const body = (
            <>
              <div className={`grid h-11 w-11 overflow-hidden rounded-full border-2 border-[#172033] font-black text-white shadow-[3px_3px_0_#172033] ${member.id === hostMemberId ? "bg-[#ff3b1f]" : "bg-[#8b5cf6]"}`}>
                <MemberAvatar member={member} sizeClass="h-full w-full" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-1.5">
                  <p className="truncate font-black">{member.name}</p>
                  {member.id === hostMemberId && <span className="bego-chip min-h-6 bg-[#f7c948] px-2 text-[10px]">Host</span>}
                  {isCurrent && <span className="bego-chip min-h-6 bg-[#45d483] px-2 text-[10px]">You</span>}
                </div>
                <p className="mt-1 text-xs font-bold text-[#64748b]">
                  {mobilityRoleLabels[member.mobilityRole]} - {transportModeLabels[member.transportMode]}
                </p>
                <p className="mt-1 text-xs font-bold text-[#64748b]">
                  {route
                    ? `${formatDuration(route.estimatedTimeSeconds)} - ${formatDistance(route.distanceMeters)}`
                    : `${member.latitude.toFixed(4)}, ${member.longitude.toFixed(4)}`}
                </p>
                {member.canOfferPickup && <p className="mt-1 text-xs font-black text-[#166534]">{member.availableSeatCount ?? 0} seats available</p>}
                {member.driverId && <p className="mt-1 text-xs font-black text-[#d42712]">Matched with driver</p>}
              </div>
            </>
          );

          return isSelectable ? (
            <button key={member.id} type="button" className={`flex w-full items-center gap-3 rounded-2xl border-2 border-[#172033] p-3 text-left ${isCurrent ? "bg-[#fff7dc]" : "bg-white"}`} onClick={() => onSelect(member.id)}>
              {body}
            </button>
          ) : (
            <div key={member.id} className={`flex items-center gap-3 rounded-2xl border-2 border-[#172033] p-3 ${isCurrent ? "bg-[#fff7dc]" : "bg-white"}`}>
              {body}
            </div>
          );
        })}
      </div>
    </section>
  );
}

function PickupPanel({
  members,
  pickupRequests,
  pickupSuggestions,
  currentMemberId,
  onAccept,
  onRelease,
}: {
  members: Member[];
  pickupRequests: PickupRequest[];
  pickupSuggestions: PickupSuggestion[];
  currentMemberId?: string;
  onAccept: (requestId: string, driverId: string) => Promise<void>;
  onRelease: (requestId: string) => Promise<void>;
}) {
  const currentDriver = members.find((member) => member.id === currentMemberId && member.canOfferPickup && (member.availableSeatCount ?? 0) > 0);

  return (
    <section className="bego-card bg-[#f6fcff] p-5">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-xl font-black">Pickup requests</h2>
        {currentDriver && <span className="bego-chip bg-[#45d483]">{currentDriver.availableSeatCount ?? 0} seats left</span>}
      </div>
      {pickupRequests.length === 0 ? (
        <p className="mt-3 text-sm font-semibold text-[#64748b]">No members need pickup yet.</p>
      ) : (
        <div className="mt-4 grid gap-3">
          {pickupRequests.map((request) => {
            const isPending = request.status === PickupRequestStatus.Pending;
            const isMine = request.acceptedDriverId === currentMemberId;
            const suggestions = pickupSuggestions.filter((suggestion) => suggestion.passengerId === request.passengerId);
            const best = suggestions[0];

            return (
              <div key={request.requestId} className="rounded-2xl border-2 border-[#172033] bg-white p-3">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-black">{request.passengerName}</p>
                    <p className="mt-1 text-xs font-bold text-[#64748b]">
                      {isPending ? "Waiting for driver" : `Accepted by ${request.acceptedDriverName || "driver"}`}
                    </p>
                  </div>
                  {isPending && currentDriver && currentDriver.id !== request.passengerId && (
                    <button type="button" className="bego-secondary min-h-9 text-xs" onClick={() => void onAccept(request.requestId, currentDriver.id)}>
                      Accept
                    </button>
                  )}
                  {!isPending && isMine && (
                    <button type="button" className="bego-secondary min-h-9 text-xs" onClick={() => void onRelease(request.requestId)}>
                      Release
                    </button>
                  )}
                </div>
                {best && (
                  <div className="mt-3 rounded-xl border-2 border-[#d8e3ea] bg-[#fff7dc] p-2 text-xs font-bold text-[#475569]">
                    Suggestion {best.driverName}: +{formatDuration(best.estimatedDetourSeconds)}, distance to passenger {formatDistance(best.distanceToPassengerMeters)}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}

function VotingSection({
  venues,
  selectedVenueId,
  hasVoted,
  currentMemberId,
  votingProgress,
  onVote,
}: {
  venues: Venue[];
  selectedVenueId: string | null;
  hasVoted: boolean;
  currentMemberId?: string;
  votingProgress: { total: number; voted: number };
  onVote: (venueId: string) => Promise<void>;
}) {
  const progress = votingProgress.total > 0 ? Math.round((votingProgress.voted / votingProgress.total) * 100) : 0;

  return (
    <section className="bego-hard-card bg-white p-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
        <div>
          <span className="bego-chip bg-[#f472b6]">Vote</span>
          <h2 className="mt-3 text-3xl font-black">Choose meeting point</h2>
        </div>
        <div className="w-full rounded-full border-2 border-[#172033] bg-white p-1 md:w-64">
          <div className="h-5 rounded-full bg-[#45d483] text-right text-xs font-black leading-5" style={{ width: `${progress}%`, minWidth: progress > 0 ? 32 : 0 }}>
            {votingProgress.voted}/{votingProgress.total}
          </div>
        </div>
      </div>
      <div className="mt-5 grid gap-4 lg:grid-cols-3">
        {venues.map((venue, index) => (
          <VenueChoiceCard
            key={venue.venueId}
            venue={venue}
            rank={index + 1}
            selected={selectedVenueId === venue.venueId}
            disabled={hasVoted}
            currentMemberId={currentMemberId}
            onVote={onVote}
          />
        ))}
      </div>
    </section>
  );
}

function VenueChoiceCard({
  venue,
  rank,
  selected,
  disabled,
  currentMemberId,
  onVote,
}: {
  venue: Venue;
  rank: number;
  selected: boolean;
  disabled: boolean;
  currentMemberId?: string;
  onVote: (venueId: string) => Promise<void>;
}) {
  const [open, setOpen] = useState(false);
  const myRoute = currentMemberId ? venue.memberRoutes.find((route) => route.memberId === currentMemberId) : venue.memberRoutes[0];

  return (
    <article className={`overflow-hidden rounded-[18px] border-2 border-[#172033] bg-white shadow-[5px_5px_0_#d8e3ea] ${selected ? "outline outline-4 outline-[#45d483]" : ""}`}>
      <div className="relative h-44 bg-[#d8e3ea]">
        {venue.photoUrls?.[0] ? (
          <div
            role="img"
            aria-label={venue.name}
            className="h-full w-full bg-cover bg-center"
            style={{ backgroundImage: `url("${venue.photoUrls[0]}")` }}
          />
        ) : (
          <div className="grid h-full place-items-center text-sm font-black text-[#64748b]">No photo</div>
        )}
        <span className="absolute left-3 top-3 grid h-10 w-10 place-items-center rounded-full border-2 border-[#172033] bg-[#f7c948] font-black shadow-[3px_3px_0_#172033]">{rank}</span>
      </div>
      <div className="p-4">
        <h3 className="line-clamp-2 text-xl font-black">{venue.name}</h3>
        <p className="mt-1 line-clamp-2 text-sm font-semibold text-[#64748b]">{venue.address}</p>
        <div className="mt-3 grid grid-cols-2 gap-2">
          <MetricBox label="Rating" value={venue.rating.toFixed(1)} tone="bg-[#f7c948]" />
          <MetricBox label="Total time" value={formatCompactDuration(venue.totalTimeSeconds)} tone="bg-[#48c7df]" />
          <MetricBox label="Detour max" value={formatCompactDuration(venue.maxDriverDetourSeconds)} tone="bg-[#f472b6]" />
          <MetricBox label="Walk" value={formatCompactDistance(venue.totalWalkingDistanceMeters)} tone="bg-[#45d483]" />
        </div>
        {myRoute && <p className="mt-3 text-sm font-bold text-[#475569]">You: {formatDuration(myRoute.estimatedTimeSeconds)} - {formatDistance(myRoute.distanceMeters)}</p>}
        <div className="mt-4 flex gap-2">
          <button type="button" className="bego-primary flex-1" disabled={disabled} onClick={() => void onVote(venue.venueId)}>
            {selected ? "Selected" : disabled ? "Voted" : "Vote"}
          </button>
          <button type="button" className="bego-secondary" onClick={() => setOpen((value) => !value)}>
            {open ? "Hide" : "Details"}
          </button>
        </div>
        {open && (
          <div className="mt-4 grid gap-3 border-t-2 border-[#172033] pt-4">
            {venue.tradeOffSummary && <p className="rounded-xl bg-[#fff7dc] p-3 text-sm font-bold">{venue.tradeOffSummary}</p>}
            {venue.aiReviewSummary && <p className="rounded-xl bg-[#f6fcff] p-3 text-sm font-bold">{venue.aiReviewSummary}</p>}
            <div className="grid gap-2 text-sm font-semibold">
              {venue.memberRoutes.map((route) => (
                <div key={route.memberId} className="flex justify-between gap-3 rounded-xl bg-[#f8fafc] p-2">
                  <span>{route.memberName}</span>
                  <span>{formatDuration(route.estimatedTimeSeconds)} - {formatDistance(route.distanceMeters)}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </article>
  );
}

function RouteSection({ venue, isHost, canLock, onLock }: { venue: Venue; isHost: boolean; canLock: boolean; onLock: () => Promise<void> }) {
  return (
    <section className="bego-hard-card bg-white p-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
        <div>
          <span className="bego-chip bg-[#45d483]">Route preview</span>
          <h2 className="mt-3 text-3xl font-black">{venue.name}</h2>
          <p className="mt-2 font-semibold text-[#64748b]">
            Group total {formatDuration(venue.totalTimeSeconds)} - walking {formatDistance(venue.totalWalkingDistanceMeters)}
          </p>
        </div>
        {isHost && canLock && (
          <button type="button" className="bego-primary" onClick={() => void onLock()}>
            Lock route
          </button>
        )}
      </div>
      {venue.optimizationReason && <p className="mt-4 rounded-2xl border-2 border-[#172033] bg-[#fff7dc] p-4 font-bold">{venue.optimizationReason}</p>}
      <div className="mt-5 grid gap-4">
        {venue.driverRoutes.map((route) => (
          <article key={route.driverId} className="rounded-2xl border-2 border-[#172033] bg-[#f6fcff] p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h3 className="text-lg font-black">{route.driverName}</h3>
                <p className="text-sm font-bold text-[#64748b]">
                  {formatDuration(route.totalTimeSeconds)} - {formatDistance(route.totalDistanceMeters)} - {route.passengerIds.length} passengers
                </p>
              </div>
              <span className="bego-chip bg-[#f472b6]">+{formatDuration(Math.max(0, route.totalTimeSeconds - route.directTimeSeconds))}</span>
            </div>
            <div className="mt-4 grid gap-2">
              {route.stops.map((stop) => (
                <div key={`${route.driverId}-${stop.sequence}`} className="grid grid-cols-[36px_1fr] gap-3 rounded-xl bg-white p-3">
                  <span className="grid h-8 w-8 place-items-center rounded-full border-2 border-[#172033] bg-[#f7c948] font-black">{stop.sequence}</span>
                  <div>
                    <p className="font-black">{stop.label}</p>
                    <p className="text-xs font-bold text-[#64748b]">
                      ETA {formatDuration(stop.etaSeconds)}
                      {stop.walkingDistanceMeters > 0 ? ` - walking ${formatDistance(stop.walkingDistanceMeters)}` : ""}
                      {stop.waitSeconds > 0 ? ` - wait ${formatDuration(stop.waitSeconds)}` : ""}
                      {stop.isMergedStop ? " - shared pickup point" : ""}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

function LiveMap({
  members,
  venues,
  geometricMedian,
  routeVenue,
  winningVenueId,
  isLoading,
}: {
  members: Member[];
  venues: Venue[];
  geometricMedian?: { latitude: number; longitude: number };
  routeVenue: Venue | null;
  winningVenueId?: string;
  isLoading: boolean;
}) {
  const mapRef = useRef<HTMLDivElement>(null);
  const mapInstance = useRef<google.maps.Map | null>(null);
  const overlays = useRef<Array<google.maps.Marker | google.maps.Polyline>>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const hasKey = Boolean(process.env.NEXT_PUBLIC_GOOGLE_MAPS_API_KEY);

  useEffect(() => {
    if (!hasKey || mapInstance.current || !mapRef.current) return;
    let cancelled = false;
    loadMaps()
      .then(() => {
        if (cancelled || !mapRef.current) return;
        mapInstance.current = new google.maps.Map(mapRef.current, {
          center: { lat: 21.0285, lng: 105.8542 },
          zoom: 12,
          mapTypeControl: false,
          streetViewControl: false,
          fullscreenControl: false,
        });
      })
      .catch(() => setLoadError("Failed to load Google Maps"));
    return () => {
      cancelled = true;
    };
  }, [hasKey]);

  useEffect(() => {
    const map = mapInstance.current;
    if (!map) return;

    overlays.current.forEach((overlay) => overlay.setMap(null));
    overlays.current = [];
    const bounds = new google.maps.LatLngBounds();

    members.forEach((member) => {
      const marker = new google.maps.Marker({
        map,
        position: { lat: member.latitude, lng: member.longitude },
        label: member.avatarUrl ? undefined : member.name.slice(0, 1).toUpperCase(),
        icon: member.avatarUrl
          ? {
              url: member.avatarUrl,
              scaledSize: new google.maps.Size(42, 42),
              anchor: new google.maps.Point(21, 21),
            }
          : undefined,
        title: member.name,
      });
      overlays.current.push(marker);
      bounds.extend(marker.getPosition()!);
    });

    venues.forEach((venue, index) => {
      const marker = new google.maps.Marker({
        map,
        position: { lat: venue.latitude, lng: venue.longitude },
        label: venue.venueId === winningVenueId ? "W" : String(index + 1),
        title: venue.name,
      });
      overlays.current.push(marker);
      bounds.extend(marker.getPosition()!);
    });

    if (geometricMedian) {
      const marker = new google.maps.Marker({
        map,
        position: { lat: geometricMedian.latitude, lng: geometricMedian.longitude },
        label: "M",
        title: "Geometric median",
      });
      overlays.current.push(marker);
      bounds.extend(marker.getPosition()!);
    }

    routeVenue?.driverRoutes.forEach((route, index) => {
      const path = route.routePolyline.length > 1
        ? route.routePolyline.map((point) => ({ lat: point.latitude, lng: point.longitude }))
        : route.stops.map((stop) => ({ lat: stop.latitude, lng: stop.longitude }));
      if (path.length > 1) {
        const line = new google.maps.Polyline({
          map,
          path,
          strokeColor: ["#ff3b1f", "#45d483", "#8b5cf6", "#f472b6"][index % 4],
          strokeWeight: 4,
          strokeOpacity: 0.9,
        });
        overlays.current.push(line);
      }
      route.stops.forEach((stop) => bounds.extend({ lat: stop.latitude, lng: stop.longitude }));
    });

    if (!bounds.isEmpty()) {
      map.fitBounds(bounds, 56);
    }
  }, [geometricMedian, members, routeVenue, venues, winningVenueId]);

  if (!hasKey || loadError) {
    return <MapFallback members={members} venues={venues} isLoading={isLoading} error={loadError ?? "NEXT_PUBLIC_GOOGLE_MAPS_API_KEY is not configured"} />;
  }

  return (
    <div className="relative h-full overflow-hidden rounded-[18px] border-2 border-[#172033]">
      <div ref={mapRef} className="h-full w-full" />
      {isLoading && <div className="absolute inset-0 grid place-items-center bg-white/80 text-lg font-black">Optimizing...</div>}
    </div>
  );
}

function MapFallback({ members, venues, isLoading, error }: { members: Member[]; venues: Venue[]; isLoading: boolean; error: string }) {
  return (
    <div className="relative h-full overflow-hidden rounded-[18px] border-2 border-[#172033] bg-[#fff7dc]">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_16px_16px,rgba(23,32,51,0.16)_2px,transparent_2px)] [background-size:34px_34px]" />
      <div className="absolute left-[12%] top-[16%] rounded-full border-2 border-[#172033] bg-[#ff3b1f] px-3 py-2 font-black text-white shadow-[4px_4px_0_#172033]">Members {members.length}</div>
      <div className="absolute right-[10%] top-[24%] rounded-full border-2 border-[#172033] bg-[#45d483] px-3 py-2 font-black shadow-[4px_4px_0_#172033]">Venues {venues.length}</div>
      <div className="absolute bottom-[18%] left-[18%] right-[18%] rounded-2xl border-2 border-[#172033] bg-white p-4 shadow-[5px_5px_0_#172033]">
        <p className="font-black">{isLoading ? "Calculating..." : "Map fallback"}</p>
        <p className="mt-1 text-sm font-bold text-[#64748b]">{error}</p>
      </div>
    </div>
  );
}

let mapsPromise: Promise<void> | null = null;
function loadMaps() {
  if (typeof google !== "undefined" && google.maps) return Promise.resolve();
  if (mapsPromise) return mapsPromise;
  mapsPromise = new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = `https://maps.googleapis.com/maps/api/js?key=${process.env.NEXT_PUBLIC_GOOGLE_MAPS_API_KEY}&v=weekly`;
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("maps failed"));
    document.head.appendChild(script);
  });
  return mapsPromise;
}

interface JoinDraft {
  memberName: string;
  latitude: number;
  longitude: number;
  avatarUrl?: string | null;
  transportMode: TransportMode;
  mobilityRole: MemberMobilityRole;
}

interface AuthUserDraft {
  name: string;
  image: string | null;
}

function JoinRoomModal({
  isOpen,
  location,
  locationError,
  locationLoading,
  onJoin,
  onClose,
  onRequestLocation,
  authStatus,
  authUser,
}: {
  isOpen: boolean;
  location: { latitude: number; longitude: number } | null;
  locationError: string | null;
  locationLoading: boolean;
  onJoin: (draft: JoinDraft) => Promise<void>;
  onClose: () => void;
  onRequestLocation: () => void;
  authStatus: "authenticated" | "loading" | "unauthenticated";
  authUser: AuthUserDraft;
}) {
  const [memberName, setMemberName] = useState("");
  const [mobilityRole, setMobilityRole] = useState<MemberMobilityRole>(MemberMobilityRole.NeedsPickup);
  const [transportMode, setTransportMode] = useState<TransportMode>(TransportMode.Motorbike);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (isOpen && authUser.name && memberName.trim().length === 0) {
      setMemberName(authUser.name);
    }
  }, [authUser.name, isOpen, memberName]);

  if (!isOpen) return null;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (memberName.trim().length < 2) {
      setError("Name must be at least 2 characters.");
      return;
    }
    if (!location) {
      setError("Location permission is required to join.");
      return;
    }

    setIsSubmitting(true);
    setError(null);
    try {
      await onJoin({
        memberName: memberName.trim(),
        latitude: location.latitude,
        longitude: location.longitude,
        avatarUrl: authUser.image,
        mobilityRole,
        transportMode: mobilityRole === MemberMobilityRole.NeedsPickup ? TransportMode.Walking : transportMode,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to join room.");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-[#172033]/55 p-4">
      <form onSubmit={submit} className="bego-hard-card w-full max-w-xl bg-white p-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <span className="bego-chip bg-[#45d483]">Join</span>
            <h2 className="mt-3 text-3xl font-black">Join room</h2>
          </div>
          <button type="button" className="bego-secondary" onClick={onClose}>Close</button>
        </div>
        {authStatus !== "authenticated" && (
          <div className="mt-5 rounded-2xl border-2 border-[#172033] bg-[#fff7dc] p-4">
            <p className="font-black">Google sign-in required</p>
            <p className="mt-1 text-sm font-bold text-[#64748b]">Your Google profile photo will be used as your map avatar.</p>
            <button type="button" className="bego-primary mt-3" onClick={() => void signIn("google")} disabled={authStatus === "loading"}>
              {authStatus === "loading" ? "Checking..." : "Sign in with Google"}
            </button>
          </div>
        )}
        {authStatus === "authenticated" && (
          <div className="mt-5 flex items-center gap-3 rounded-2xl border-2 border-[#172033] bg-[#f6fcff] p-3">
            <ProfileAvatar name={authUser.name} image={authUser.image} sizeClass="h-12 w-12" />
            <div className="min-w-0">
              <p className="truncate font-black">{authUser.name || "Google user"}</p>
              <p className="text-xs font-bold text-[#64748b]">Google avatar active</p>
            </div>
          </div>
        )}
        <label className="mt-5 grid gap-2 font-black">
          Your name
          <input className="bego-input" value={memberName} onChange={(event) => setMemberName(event.target.value)} autoFocus />
        </label>
        <label className="mt-4 grid gap-2 font-black">
          Mobility role
          <select className="bego-input" value={mobilityRole} onChange={(event) => setMobilityRole(event.target.value as MemberMobilityRole)}>
            <option value={MemberMobilityRole.NeedsPickup}>Needs pickup</option>
            <option value={MemberMobilityRole.SelfTravel}>Self travel / has vehicle</option>
          </select>
        </label>
        {mobilityRole === MemberMobilityRole.SelfTravel && (
          <label className="mt-4 grid gap-2 font-black">
            Transport mode
            <select className="bego-input" value={transportMode} onChange={(event) => setTransportMode(Number(event.target.value) as TransportMode)}>
              {transportModes.map((mode) => <option key={mode} value={mode}>{transportModeLabels[mode]}</option>)}
            </select>
          </label>
        )}
        <div className="mt-4 rounded-2xl border-2 border-[#172033] bg-[#f6fcff] p-4">
          <p className="font-black">Location</p>
          <p className="mt-1 text-sm font-bold text-[#64748b]">
            {location ? `${location.latitude.toFixed(5)}, ${location.longitude.toFixed(5)}` : locationLoading ? "Getting location..." : locationError || "No location"}
          </p>
          <button type="button" className="bego-secondary mt-3" onClick={onRequestLocation} disabled={locationLoading}>Get location</button>
        </div>
        {error && <p className="mt-3 rounded-xl bg-[#fff1f2] p-3 font-bold text-[#b42318]">{error}</p>}
        <button type="submit" className="bego-primary mt-5 w-full" disabled={isSubmitting || !location || authStatus !== "authenticated"}>
          {isSubmitting ? "Joining room..." : "Join room"}
        </button>
      </form>
    </div>
  );
}

function TestMemberModal({ isOpen, sessionId, memberCount, onClose, onAdded }: { isOpen: boolean; sessionId: string; memberCount: number; onClose: () => void; onAdded: () => Promise<void> }) {
  const [name, setName] = useState(`Test ${memberCount + 1}`);
  const [transportMode, setTransportMode] = useState<TransportMode>(TransportMode.Motorbike);
  const [location, setLocation] = useState(randomHanoiLocation());
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (isOpen) {
      setName(`Test ${memberCount + 1}`);
      setLocation(randomHanoiLocation());
    }
  }, [isOpen, memberCount]);

  if (!isOpen) return null;

  async function addMember() {
    if (name.trim().length < 2) {
      setError("Enter test member name.");
      return;
    }
    setIsSubmitting(true);
    setError(null);
    try {
      await api.sessions.join(sessionId, {
        memberName: name.trim(),
        latitude: location.latitude,
        longitude: location.longitude,
        transportMode,
        mobilityRole: transportMode === TransportMode.Walking ? MemberMobilityRole.NeedsPickup : MemberMobilityRole.SelfTravel,
      });
      await onAdded();
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to add test member.");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-[#172033]/55 p-4">
      <section className="bego-hard-card w-full max-w-lg bg-white p-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <span className="bego-chip bg-[#f472b6]">Local dev</span>
            <h2 className="mt-3 text-3xl font-black">Add test member</h2>
          </div>
          <button type="button" className="bego-secondary" onClick={onClose}>Close</button>
        </div>
        <label className="mt-5 grid gap-2 font-black">
          Name
          <input className="bego-input" value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <label className="mt-4 grid gap-2 font-black">
          Transport mode
          <select className="bego-input" value={transportMode} onChange={(event) => setTransportMode(Number(event.target.value) as TransportMode)}>
            {transportModes.map((mode) => <option key={mode} value={mode}>{transportModeLabels[mode]}</option>)}
          </select>
        </label>
        <div className="mt-4 rounded-2xl border-2 border-[#172033] bg-[#fff7dc] p-4">
          <p className="font-black">{location.label}</p>
          <p className="mt-1 text-sm font-bold text-[#64748b]">{location.latitude.toFixed(6)}, {location.longitude.toFixed(6)}</p>
          <button type="button" className="bego-secondary mt-3" onClick={() => setLocation(randomHanoiLocation())}>Change point</button>
        </div>
        {error && <p className="mt-3 rounded-xl bg-[#fff1f2] p-3 font-bold text-[#b42318]">{error}</p>}
        <button type="button" className="bego-primary mt-5 w-full" onClick={() => void addMember()} disabled={isSubmitting}>
          {isSubmitting ? "Adding..." : "Add test member"}
        </button>
      </section>
    </div>
  );
}

function randomHanoiLocation() {
  const seed = hanoiSeeds[Math.floor(Math.random() * hanoiSeeds.length)];
  return {
    label: `${seed.label}, Ha Noi`,
    latitude: seed.latitude + (Math.random() - 0.5) * 0.012,
    longitude: seed.longitude + (Math.random() - 0.5) * 0.012,
  };
}

function MetricBox({ label, value, tone }: { label: string; value: string; tone: string }) {
  return (
    <div className={`rounded-2xl border-2 border-[#172033] p-3 ${tone}`}>
      <p className="text-[11px] font-black uppercase text-[#172033]/70">{label}</p>
      <p className="mt-1 text-lg font-black">{value}</p>
    </div>
  );
}

function statusToneClass(tone: string) {
  switch (tone) {
    case "busy":
      return "bg-[#48c7df]";
    case "vote":
      return "bg-[#f472b6]";
    case "route":
      return "bg-[#f7c948]";
    case "done":
      return "bg-[#45d483]";
    case "failed":
      return "bg-[#fff1f2]";
    default:
      return "bg-white";
  }
}

function useToastStack() {
  const [messages, setMessages] = useState<Array<{ id: string; text: string }>>([]);
  const push = useCallback((text: string) => {
    const id = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
    setMessages((current) => [...current, { id, text }].slice(-4));
    window.setTimeout(() => setMessages((current) => current.filter((message) => message.id !== id)), 4200);
  }, []);
  const remove = useCallback((id: string) => setMessages((current) => current.filter((message) => message.id !== id)), []);
  return useMemo(() => ({ messages, push, remove }), [messages, push, remove]);
}

function ToastShelf({ messages, onRemove }: { messages: Array<{ id: string; text: string }>; onRemove: (id: string) => void }) {
  return (
    <div className="fixed right-4 top-4 z-[60] grid w-[min(360px,calc(100vw-32px))] gap-2">
      {messages.map((message) => (
        <button key={message.id} type="button" className="rounded-2xl border-2 border-[#172033] bg-white p-3 text-left text-sm font-black shadow-[4px_4px_0_#172033]" onClick={() => onRemove(message.id)}>
          {message.text}
        </button>
      ))}
    </div>
  );
}
