const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5000";

interface CreateSessionRequest {
  hostName: string;
  defaultQuery?: string;
}

interface CreateSessionResponse {
  sessionId: string;
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
      errorData?.message || `HTTP error ${response.status}`,
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

    join: async (
      sessionId: string,
      data: {
        memberName: string;
        latitude: number;
        longitude: number;
        transportMode: number;
      }
    ) => {
      const response = await fetch(`${API_BASE_URL}/api/sessions/${sessionId}/members`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify(data),
      });
      return handleResponse<{ memberId: string }>(response);
    },
  },
};

export { ApiError };
