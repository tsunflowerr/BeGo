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
  RoutePreview = "RoutePreview",
  Completed = "Completed",
  Failed = "Failed",
}

export enum MemberMobilityRole {
  SelfTravel = "SelfTravel",
  NeedsPickup = "NeedsPickup",
}

export enum PickupRequestStatus {
  Pending = "Pending",
  Accepted = "Accepted",
  Cancelled = "Cancelled",
}

// Member interface
export interface Member {
  id: string;
  sessionId: string;
  name: string;
  latitude: number;
  longitude: number;
  transportMode: TransportMode;
  mobilityRole: MemberMobilityRole;
  driverId?: string | null;
  canOfferPickup?: boolean;
  availableSeatCount?: number;
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
  votes: Vote[];
  pickupRequests: PickupRequest[];
  nominatedVenueIds: string[];
  winningVenueId?: string | null;
  latestOptimizationResult?: OptimizationResult | null;
  finalRoutePreview?: Venue | null;
  departureLockedAt?: string | null;
}

export interface PickupRequest {
  requestId: string;
  passengerId: string;
  passengerName: string;
  status: PickupRequestStatus;
  acceptedDriverId?: string | null;
  acceptedDriverName?: string | null;
  createdAt: string;
  updatedAt: string;
}

// Member route info for a venue
export interface MemberRoute {
  memberId: string;
  memberName: string;
  estimatedTimeSeconds: number;
  distanceMeters: number;
  driverId?: string | null;
  walkingDistanceMeters?: number;
}

export interface RouteStop {
  sequence: number;
  stopType: string;
  label: string;
  latitude: number;
  longitude: number;
  etaSeconds: number;
  distanceFromPreviousMeters: number;
  walkingDistanceMeters: number;
  passengerIds: string[];
}

export interface DriverRoute {
  driverId: string;
  driverName: string;
  totalTimeSeconds: number;
  totalDistanceMeters: number;
  directTimeSeconds: number;
  directDistanceMeters: number;
  passengerIds: string[];
  stops: RouteStop[];
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
  maxDriverDetourSeconds: number;
  totalWalkingDistanceMeters: number;
  memberRoutes: MemberRoute[];
  driverRoutes: DriverRoute[];
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
  mobilityRole: MemberMobilityRole;
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

export interface PickupRequestsUpdatedEvent {
  sessionId: string;
  timestamp: string;
}

export interface DepartureLockedEvent {
  sessionId: string;
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

export const mobilityRoleLabels: Record<MemberMobilityRole, string> = {
  [MemberMobilityRole.SelfTravel]: "Tự di chuyển",
  [MemberMobilityRole.NeedsPickup]: "Cần đón",
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
