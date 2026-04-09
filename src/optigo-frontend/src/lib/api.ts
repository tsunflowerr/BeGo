import {
  Session,
  TransportMode,
  OptimizationResult,
  VoteResponse,
} from "@/types";

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5096";

interface CreateSessionRequest {
  hostName: string;
  latitude: number;
  longitude: number;
  transportMode: TransportMode;
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
  transportMode: TransportMode;
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

export const api = {
  sessions: {
    create: async (data: CreateSessionRequest): Promise<CreateSessionResponse> => {
      const response = await fetch(`${API_BASE_URL}/api/sessions`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(data),
      });
      return normalizeCreateSessionResponse(await handleResponse<unknown>(response));
    },

    get: async (sessionId: string): Promise<Session> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}`, {
        method: "GET",
        headers: {
          "Content-Type": "application/json",
        },
      });
      
      return handleResponse<Session>(response);
    },

    join: async (sessionId: string, data: JoinSessionRequest): Promise<JoinSessionResponse> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/members`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(data),
      });
      return handleResponse<JoinSessionResponse>(response);
    },

    updateQuery: async (sessionId: string, queryText: string): Promise<void> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/sessions/${normalizedSessionId}/query`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ queryText }),
      });
      await handleResponse<{ message: string }>(response);
    },
  },

  optimizer: {
    findMeetingPoint: async (sessionId: string, category?: string): Promise<OptimizationResult> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const url = new URL(`${API_BASE_URL}/api/optimizer/session/${normalizedSessionId}/optimize`);
      if (category) {
        url.searchParams.set("category", category);
      }
      const response = await fetch(url.toString(), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
      });
      return handleResponse<OptimizationResult>(response);
    },
  },

  vote: {
    submit: async (sessionId: string, memberId: string, venueId: string): Promise<VoteResponse> => {
      const normalizedSessionId = normalizeSessionId(sessionId);
      const response = await fetch(`${API_BASE_URL}/api/vote/${normalizedSessionId}`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
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
};

export { ApiError };
export { API_BASE_URL };
