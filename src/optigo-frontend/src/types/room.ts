// Transport modes enum matching backend
export enum TransportMode {
  Walking = 0,
  Cycling = 1,
  Motorbike = 2,
  Car = 3,
  Bus = 4,
}

// Session status enum matching backend
export enum SessionStatus {
  WaitingForMembers = "WaitingForMembers",
  Computing = "Computing",
  Voting = "Voting",
  Completed = "Completed",
  Failed = "Failed",
}

// Member interface
export interface Member {
  id: string;
  sessionId: string;
  name: string;
  latitude: number;
  longitude: number;
  transportMode: TransportMode;
  joinedAt: string;
  isHost?: boolean;
  avatar?: string;
}

// Session interface
export interface Session {
  id: string;
  hostName: string;
  status: SessionStatus;
  queryText?: string;
  createdAt: string;
  expiresAt: string;
  members: Member[];
  nominatedVenueIds: string[];
}

// Member route info for a venue
export interface MemberRoute {
  memberId: string;
  memberName: string;
  estimatedTimeSeconds: number;
  distanceMeters: number;
}

// Review from Google Places
export interface VenueReview {
  authorName: string;
  rating: number;
  text: string;
  relativeTime: string;
}

// Top venue with full details
export interface Venue {
  venueId: string;
  name: string;
  category: string;
  latitude: number;
  longitude: number;
  address: string;
  rating: number;
  reviewCount: number;
  priceLevel?: number;
  totalTimeSeconds: number;
  finalScore: number;
  memberRoutes: MemberRoute[];
  photoUrls: string[];
  aiReviewSummary?: string;
  topReviews: VenueReview[];
}

// Geometric median result
export interface GeometricMedian {
  latitude: number;
  longitude: number;
}

// Full optimization result from backend
export interface OptimizationResult {
  geometricMedian: GeometricMedian;
  topVenues: Venue[];
}

// Vote interface
export interface Vote {
  memberId: string;
  venueId: string;
}

// Vote response
export interface VoteResponse {
  message: string;
  isVotingCompleted: boolean;
  winningVenueId?: string;
}

// SignalR Events
export interface MemberJoinedEvent {
  sessionId: string;
  memberId: string;
  memberName: string;
  latitude: number;
  longitude: number;
  transportMode: TransportMode;
  joinedAt: string;
  isHost: boolean;
  totalMembers: number;
}

export interface ComputingStartedEvent {
  sessionId: string;
  message: string;
  timestamp: string;
}

export interface OptimizationCompletedEvent {
  sessionId: string;
  result: OptimizationResult;
  timestamp: string;
}

export interface VoteSubmittedEvent {
  sessionId: string;
  memberId: string;
  venueId: string;
  totalVotes: number;
  totalMembers: number;
  progress: number;
}

export interface VotingCompletedEvent {
  sessionId: string;
  winningVenueId: string;
  message: string;
  timestamp: string;
}

export interface SignalRError {
  code: string;
  message: string;
}

// Transport mode helpers
export const transportModeLabels: Record<TransportMode, string> = {
  [TransportMode.Walking]: "Đi bộ",
  [TransportMode.Cycling]: "Xe đạp",
  [TransportMode.Motorbike]: "Xe máy",
  [TransportMode.Car]: "Ô tô",
  [TransportMode.Bus]: "Xe buýt",
};

export const transportModeIcons: Record<TransportMode, string> = {
  [TransportMode.Walking]: "🚶",
  [TransportMode.Cycling]: "🚴",
  [TransportMode.Motorbike]: "🏍️",
  [TransportMode.Car]: "🚗",
  [TransportMode.Bus]: "🚌",
};

// Helper to format time
export function formatDuration(seconds: number): string {
  if (seconds < 60) {
    return `${Math.round(seconds)} giây`;
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes} phút`;
  }
  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  if (remainingMinutes === 0) {
    return `${hours} giờ`;
  }
  return `${hours} giờ ${remainingMinutes} phút`;
}

// Helper to format distance
export function formatDistance(meters: number): string {
  if (meters < 1000) {
    return `${Math.round(meters)} m`;
  }
  const km = meters / 1000;
  return `${km.toFixed(1)} km`;
}
