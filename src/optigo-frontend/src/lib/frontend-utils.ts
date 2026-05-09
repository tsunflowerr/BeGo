export interface StatusMeta {
  label: string;
  tone: "idle" | "busy" | "vote" | "route" | "done" | "failed";
  action: string;
}

const STATUS_META: Record<string, StatusMeta> = {
  WaitingForMembers: {
    label: "Forming group",
    tone: "idle",
    action: "Invite members, edit search requirements, process join requests.",
  },
  Computing: {
    label: "Optimizing",
    tone: "busy",
    action: "System is calculating suitable locations and routes.",
  },
  Voting: {
    label: "Voting",
    tone: "vote",
    action: "Invite members to select a location from the suggested list.",
  },
  RoutePreview: {
    label: "Route preview",
    tone: "route",
    action: "View pickup points, detours, and time before locking the trip.",
  },
  Completed: {
    label: "Ready to depart",
    tone: "done",
    action: "Route has been locked for the whole group.",
  },
  Failed: {
    label: "Need to retry",
    tone: "failed",
    action: "Check inputs or re-run optimization.",
  },
};

export function getStatusMeta(status: string | null | undefined): StatusMeta {
  return STATUS_META[status ?? ""] ?? STATUS_META.WaitingForMembers;
}

export function formatCompactDuration(seconds: number): string {
  if (!Number.isFinite(seconds)) {
    return "-";
  }

  const safeSeconds = Math.max(0, seconds);
  if (safeSeconds < 60) {
    return `${Math.round(safeSeconds)}s`;
  }

  const minutes = Math.floor(safeSeconds / 60);
  if (minutes < 60) {
    return `${minutes}m`;
  }

  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  return remainingMinutes > 0 ? `${hours}h ${remainingMinutes}m` : `${hours}h`;
}

export function formatCompactDistance(meters: number): string {
  if (!Number.isFinite(meters)) {
    return "-";
  }

  const safeMeters = Math.max(0, meters);
  if (safeMeters < 1000) {
    return `${Math.round(safeMeters)}m`;
  }

  return `${(safeMeters / 1000).toFixed(1)}km`;
}

export function formatMetricPercent(value: number, fractionDigits = 0): string {
  if (!Number.isFinite(value)) {
    return "-";
  }

  return `${(value * 100).toFixed(fractionDigits)}%`;
}

export function signedGapPercent(value: number): string {
  if (!Number.isFinite(value)) {
    return "-";
  }

  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toFixed(1)}%`;
}

export function clampBenchmarkScenarioCount(value: number): number {
  if (!Number.isFinite(value)) {
    return 1;
  }

  return Math.min(60, Math.max(1, Math.round(value)));
}

export function canShowLocalTestTools(options: {
  nodeEnv: string | undefined;
  hostname: string;
  hasJoined: boolean;
  isHost: boolean;
  status: string;
}): boolean {
  const isLocalhost = options.hostname === "localhost" || options.hostname === "127.0.0.1";
  return (
    options.nodeEnv === "development" &&
    isLocalhost &&
    options.hasJoined &&
    options.isHost &&
    options.status === "WaitingForMembers"
  );
}

export function buildShareUrl(origin: string, sessionId: string): string {
  const safeOrigin = origin.replace(/\/+$/, "");
  return `${safeOrigin}/room/${sessionId}`;
}
