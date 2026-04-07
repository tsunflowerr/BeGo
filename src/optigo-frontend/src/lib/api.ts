import {
  Session,
  SessionStatus,
  TransportMode,
  OptimizationResult,
  VoteResponse,
} from "@/types";

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5096";

interface CreateSessionRequest {
  hostName: string;
  defaultQuery?: string;
}

interface CreateSessionResponse {
  sessionId: string;
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

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const errorData = await response.json().catch(() => null);
    throw new ApiError(
      errorData?.message || errorData?.error || `HTTP error ${response.status}`,
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
      return handleResponse<CreateSessionResponse>(response);
    },

    get: async (sessionId: string): Promise<Session> => {
      const response = await fetch(`${API_BASE_URL}/api/sessions/${sessionId}`, {
        method: "GET",
        headers: {
          "Content-Type": "application/json",
        },
      });
      
      return handleResponse<Session>(response);
    },

    join: async (sessionId: string, data: JoinSessionRequest): Promise<JoinSessionResponse> => {
      const response = await fetch(`${API_BASE_URL}/api/sessions/${sessionId}/members`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(data),
      });
      return handleResponse<JoinSessionResponse>(response);
    },

    updateQuery: async (sessionId: string, queryText: string): Promise<void> => {
      const response = await fetch(`${API_BASE_URL}/api/sessions/${sessionId}/query`, {
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
      const url = new URL(`${API_BASE_URL}/api/optimizer/session/${sessionId}/optimize`);
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
      const response = await fetch(`${API_BASE_URL}/api/vote/${sessionId}`, {
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
