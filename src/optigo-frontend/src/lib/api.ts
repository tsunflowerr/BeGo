import {
  Session,
  TransportMode,
  MemberMobilityRole,
  OptimizationResult,
  OutingBenchmarkReport,
  PickupSuggestion,
  VoteResponse,
  ChatMessage,
} from "@/types";
import { getSession } from "next-auth/react";

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5096";

interface CreateSessionRequest {
  hostName: string;
  latitude: number;
  longitude: number;
  avatarUrl?: string | null;
  transportMode: TransportMode;
  mobilityRole?: MemberMobilityRole;
  defaultQuery?: string;
}

interface CreateSessionResponse {
  sessionId: string;
  hostMemberId: string;
}

interface JoinSessionRequest {
  memberName: string;
  latitude: number;
  longitude: number;
  avatarUrl?: string | null;
  transportMode: TransportMode;
  mobilityRole?: MemberMobilityRole;
}

interface JoinSessionResponse {
  memberId: string;
}

class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public data?: unknown
  ) {
    super(message);
    this.name = "ApiError";
  }
}

function normalizeSessionId(value: unknown): string {
  if (typeof value === "string" && value.trim().length > 0) {
    return value;
  }

  if (
    value &&
    typeof value === "object" &&
    "sessionId" in value &&
    typeof value.sessionId === "string" &&
    value.sessionId.trim().length > 0
  ) {
    return value.sessionId;
  }

  throw new ApiError("Session ID không hợp lệ", 500, value);
}

function normalizeCreateSessionResponse(data: unknown): CreateSessionResponse {
  if (
    data &&
    typeof data === "object" &&
    "sessionId" in data &&
    typeof data.sessionId === "string" &&
    "hostMemberId" in data &&
    typeof data.hostMemberId === "string"
  ) {
    return {
      sessionId: data.sessionId,
      hostMemberId: data.hostMemberId,
    };
  }

  if (
    data &&
    typeof data === "object" &&
    "sessionId" in data &&
    data.sessionId &&
    typeof data.sessionId === "object" &&
    "sessionId" in data.sessionId &&
    typeof data.sessionId.sessionId === "string" &&
    "hostMemberId" in data.sessionId &&
    typeof data.sessionId.hostMemberId === "string"
  ) {
    return {
      sessionId: data.sessionId.sessionId,
      hostMemberId: data.sessionId.hostMemberId,
    };
  }

  throw new ApiError("Phản hồi tạo phòng không hợp lệ", 500, data);
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    throw new ApiError(
      errorData?.message ||
        errorData?.error?.message ||
        errorData?.error ||
        errorData?.Error ||
        (Array.isArray(errorData?.Details) ? errorData.Details.join(", ") : undefined) ||
        `HTTP error ${response.status}`,
      response.status,
      errorData
    );
  }
  return response.json();
}

async function authHeaders(): Promise<HeadersInit> {
  const session = await getSession();
  const idToken = (session as (typeof session & { idToken?: string }) | null)?.idToken;
  if (!idToken) {
    throw new ApiError("Phiên đăng nhập Google chưa có ID token. Hãy sign out rồi sign in lại.", 401, session);
  }

  return {
    "Content-Type": "application/json",
    Authorization: `Bearer ${idToken}`,
  };
}

export const api = {
  sessions: {
    create: async (data: CreateSessionRequest): Promise<CreateSessionResponse> => {
      const response = await fetch(`${API_BASE_URL}/api/sessions`, {
        method: "POST",
        headers: await authHeaders(),
        body: JSON.stringify(data),
      });
      return normalizeCreateSessionResponse(await handleResponse<unknown>(response));
    },

    get: async (sessionId: string): Promise<Session> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}`, {
        method: "GET",
        headers: await authHeaders(),
      });
      
      return handleResponse<Session>(response);
    },

    join: async (sessionId: string, data: JoinSessionRequest): Promise<JoinSessionResponse> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/members`, {
        method: "POST",
        headers: await authHeaders(),
        body: JSON.stringify(data),
      });
      return handleResponse<JoinSessionResponse>(response);
    },

    updateQuery: async (sessionId: string, queryText: string): Promise<void> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/query`, {
        method: "PUT",
        headers: await authHeaders(),
        body: JSON.stringify({ queryText }),
      });
      await handleResponse<{ message: string }>(response);
    },

    acceptPickupRequest: async (sessionId: string, requestId: string, driverId: string): Promise<void> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/pickup-requests/${requestId}/accept`, {
        method: "POST",
        headers: await authHeaders(),
        body: JSON.stringify({ driverId }),
      });
      await handleResponse<{ message: string }>(response);
    },

    releasePickupRequest: async (sessionId: string, requestId: string): Promise<void> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/pickup-requests/${requestId}/release`, {
        method: "POST",
        headers: await authHeaders(),
      });
      await handleResponse<{ message: string }>(response);
    },

    getPickupSuggestions: async (sessionId: string): Promise<PickupSuggestion[]> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/pickup-suggestions`, {
        method: "GET",
        headers: await authHeaders(),
      });
      return handleResponse<PickupSuggestion[]>(response);
    },

    lockDeparture: async (sessionId: string): Promise<void> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/departure/lock`, {
        method: "POST",
        headers: await authHeaders(),
      });
      await handleResponse<{ message: string }>(response);
    },
  },

  optimizer: {
    findMeetingPoint: async (sessionId: string, query?: string): Promise<OptimizationResult> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const url = new URL(`${API_BASE_URL}/api/optimizer/session/${normalizedSessionId}/optimize`);
      if (query) {
        url.searchParams.set("category", query);
      }
      const response = await fetch(url.toString(), {
        method: "POST",
        headers: await authHeaders(),
      });
      return handleResponse<OptimizationResult>(response);
    },
  },

  vote: {
    submit: async (sessionId: string, memberId: string, venueId: string): Promise<VoteResponse> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/vote/${normalizedSessionId}`, {
        method: "POST",
        headers: await authHeaders(),
        body: JSON.stringify({ memberId, venueId }),
      });
      return handleResponse<VoteResponse>(response);
    },
  },

  health: {
    check: async (): Promise<{ status: string; timestamp: string; version: string }> => {
      const response = await fetch(`${API_BASE_URL}/api/health`);
      return handleResponse(response);
    },
  },

  benchmarks: {
    runOuting: async (seed: number, scenarioCount: number): Promise<OutingBenchmarkReport> => {
      const url = new URL(`${API_BASE_URL}/api/benchmarks/outing`);
      url.searchParams.set("seed", String(seed));
      url.searchParams.set("scenarioCount", String(scenarioCount));
      const response = await fetch(url.toString(), {
        method: "GET",
        headers: await authHeaders(),
      });
      return handleResponse<OutingBenchmarkReport>(response);
    },
  },

  chat: {
    list: async (sessionId: string, take = 50): Promise<ChatMessage[]> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const url = new URL(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/chat`);
      url.searchParams.set("take", String(take));
      const response = await fetch(url.toString(), {
        method: "GET",
        headers: await authHeaders(),
      });
      return handleResponse<ChatMessage[]>(response);
    },

    send: async (sessionId: string, memberId: string, text: string): Promise<ChatMessage> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/chat`, {
        method: "POST",
        headers: await authHeaders(),
        body: JSON.stringify({ memberId, text }),
      });
      return handleResponse<ChatMessage>(response);
    },
  },
};

export { ApiError };
export { API_BASE_URL };
