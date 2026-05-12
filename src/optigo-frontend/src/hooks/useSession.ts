"use client";

import { useState, useEffect, useCallback, useRef } from "react";
import { api } from "@/lib/api";
import {
  Session,
  Member,
  PickupRequest,
  PickupSuggestion,
  SessionStatus,
  OptimizationResult,
  Venue,
  MemberJoinedEvent,
  MemberLeftEvent,
  OptimizationCompletedEvent,
  VoteSubmittedEvent,
  VotingCompletedEvent,
  Vote,
} from "@/types";
import { useSignalR } from "./useSignalR";

interface UseSessionOptions {
  sessionId: string;
  memberId?: string | null;
}

interface UseSessionReturn {
  // Session data
  session: Session | null;
  members: Member[];
  pickupRequests: PickupRequest[];
  pickupSuggestions: PickupSuggestion[];
  isHost: boolean;
  currentMember: Member | null;
  
  // Optimization data
  optimizationResult: OptimizationResult | null;
  topVenues: Venue[];
  winningVenueId: string | null;
  finalRoutePreview: Venue | null;
  
  // Voting
  votingProgress: { total: number; voted: number };
  hasVoted: boolean;
  
  // State
  loading: boolean;
  error: string | null;
  status: SessionStatus;
  isComputing: boolean;
  isVoting: boolean;
  isRoutePreview: boolean;
  isCompleted: boolean;
  
  // Connection
  isConnected: boolean;
  memberLeaveNotice: MemberLeftEvent | null;
  
  // Actions
  notifyMemberLeft: (payload: { memberId: string; memberName: string; isHost: boolean }) => Promise<void>;
  refreshSession: () => Promise<void>;
  startOptimization: (query?: string) => Promise<void>;
  submitVote: (venueId: string) => Promise<void>;
  acceptPickupRequest: (requestId: string, driverId: string) => Promise<void>;
  releasePickupRequest: (requestId: string) => Promise<void>;
  lockDeparture: () => Promise<void>;
}

export function useSession({ sessionId, memberId }: UseSessionOptions): UseSessionReturn {
  const [session, setSession] = useState<Session | null>(null);
  const [members, setMembers] = useState<Member[]>([]);
  const [pickupRequests, setPickupRequests] = useState<PickupRequest[]>([]);
  const [pickupSuggestions, setPickupSuggestions] = useState<PickupSuggestion[]>([]);
  const [optimizationResult, setOptimizationResult] = useState<OptimizationResult | null>(null);
  const [winningVenueId, setWinningVenueId] = useState<string | null>(null);
  const [finalRoutePreview, setFinalRoutePreview] = useState<Venue | null>(null);
  const [votingProgress, setVotingProgress] = useState({ total: 0, voted: 0 });
  const [hasVoted, setHasVoted] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isComputing, setIsComputing] = useState(false);
  const [memberLeaveNotice, setMemberLeaveNotice] = useState<MemberLeftEvent | null>(null);
  
  const loadedRef = useRef(false);

  const appendVote = useCallback((vote: Vote) => {
    setSession((prev) => {
      if (!prev || prev.votes.some((existing) => existing.memberId === vote.memberId)) {
        return prev;
      }

      return {
        ...prev,
        votes: [...prev.votes, vote],
      };
    });
  }, []);

  // Fetch session data
  const refreshSession = useCallback(async () => {
    try {
      setError(null);
      const data = await api.sessions.get(sessionId);
      setSession(data);
      setMembers(data.members || []);
      setPickupRequests(data.pickupRequests || []);
      const hasPendingPickup = (data.pickupRequests || []).some((request) => request.status === "Pending");
      setPickupSuggestions(hasPendingPickup ? await api.sessions.getPickupSuggestions(sessionId) : []);
      setOptimizationResult(data.latestOptimizationResult || null);
      setWinningVenueId(data.winningVenueId || null);
      setFinalRoutePreview(data.finalRoutePreview || null);
      setVotingProgress({ total: data.members?.length || 0, voted: data.votes?.length || 0 });
      setHasVoted(!!memberId && (data.votes || []).some((vote) => vote.memberId === memberId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không thể tải thông tin phòng");
    } finally {
      setLoading(false);
    }
  }, [memberId, sessionId]);

  // SignalR event handlers
  const handleMemberJoined = useCallback((event: MemberJoinedEvent) => {
    if (event.isHost) {
      setMemberLeaveNotice(null);
    }

    setMembers((prev) => {
      const nextMember: Member = {
        id: event.memberId,
        sessionId: event.sessionId,
        name: event.memberName,
        latitude: event.latitude,
        longitude: event.longitude,
        avatarUrl: event.avatarUrl,
        transportMode: event.transportMode,
        mobilityRole: event.mobilityRole,
        joinedAt: event.joinedAt,
        isHost: event.isHost,
      };

      if (prev.some((m) => m.id === event.memberId)) {
        return prev.map((member) => member.id === event.memberId ? nextMember : member);
      }

      return [...prev, nextMember];
    });
  }, []);

  const handleMemberLeft = useCallback((event: MemberLeftEvent) => {
    setMemberLeaveNotice(event);
  }, []);

  const handleComputingStarted = useCallback(() => {
    setIsComputing(true);
    setSession((prev) => prev ? { ...prev, status: SessionStatus.Computing } : null);
  }, []);

  const handleOptimizationCompleted = useCallback((event: OptimizationCompletedEvent) => {
    setIsComputing(false);
    setOptimizationResult(event.result);
    setWinningVenueId(null);
    setFinalRoutePreview(null);
    setHasVoted(false);
    setSession((prev) => prev ? { ...prev, status: SessionStatus.Voting } : null);
    setVotingProgress({ total: members.length, voted: 0 });
  }, [members.length]);

  const handleVoteSubmitted = useCallback((event: VoteSubmittedEvent) => {
    appendVote({ memberId: event.memberId, venueId: event.venueId });
    setVotingProgress({ total: event.totalMembers, voted: event.totalVotes });
    if (event.memberId === memberId) {
      setHasVoted(true);
    }
  }, [appendVote, memberId]);

  const handleVotingCompleted = useCallback((event: VotingCompletedEvent) => {
    setWinningVenueId(event.winningVenueId);
    setSession((prev) => prev ? { ...prev, status: SessionStatus.RoutePreview } : null);
    void refreshSession();
  }, [refreshSession]);

  const handlePickupRequestsUpdated = useCallback(() => {
    void refreshSession();
  }, [refreshSession]);

  const handleDepartureLocked = useCallback(() => {
    void refreshSession();
  }, [refreshSession]);

  const handleError = useCallback((error: { code: string; message: string }) => {
    setError(error.message);
  }, []);

  // SignalR connection
  const { isConnected, notifyMemberLeft } = useSignalR({
    sessionId,
    onMemberJoined: handleMemberJoined,
    onMemberLeft: handleMemberLeft,
    onComputingStarted: handleComputingStarted,
    onOptimizationCompleted: handleOptimizationCompleted,
    onVoteSubmitted: handleVoteSubmitted,
    onVotingCompleted: handleVotingCompleted,
    onPickupRequestsUpdated: handlePickupRequestsUpdated,
    onDepartureLocked: handleDepartureLocked,
    onError: handleError,
  });

  // Initial load
  useEffect(() => {
    if (!loadedRef.current) {
      loadedRef.current = true;
      refreshSession();
    }
  }, [refreshSession]);

  useEffect(() => {
    setHasVoted(!!memberId && (session?.votes || []).some((vote) => vote.memberId === memberId));
  }, [memberId, session?.votes]);

  // Start optimization
  const startOptimization = useCallback(async (query?: string) => {
    try {
      setIsComputing(true);
      setError(null);
      const result = await api.optimizer.findMeetingPoint(sessionId, query);
      setOptimizationResult(result);
      setWinningVenueId(null);
      setFinalRoutePreview(null);
      setHasVoted(false);
      setSession((prev) => prev ? { ...prev, status: SessionStatus.Voting } : null);
      setVotingProgress({ total: members.length, voted: 0 });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không thể tìm kiếm địa điểm");
    } finally {
      setIsComputing(false);
    }
  }, [sessionId, members.length]);

  // Submit vote
  const submitVote = useCallback(async (venueId: string) => {
    if (!memberId) {
      setError("You need to join the room before voting");
      return;
    }
    
    try {
      setError(null);
      const response = await api.vote.submit(sessionId, memberId, venueId);
      appendVote({ memberId, venueId });
      setHasVoted(true);
      
      if (response.isVotingCompleted && response.winningVenueId) {
        setWinningVenueId(response.winningVenueId);
        setSession((prev) => prev ? { ...prev, status: SessionStatus.RoutePreview } : null);
        await refreshSession();
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không thể gửi bình chọn");
    }
  }, [appendVote, sessionId, memberId, refreshSession]);

  const acceptPickupRequest = useCallback(async (requestId: string, driverId: string) => {
    try {
      setError(null);
      await api.sessions.acceptPickupRequest(sessionId, requestId, driverId);
      await refreshSession();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không thể nhận yêu cầu đón");
    }
  }, [refreshSession, sessionId]);

  const releasePickupRequest = useCallback(async (requestId: string) => {
    try {
      setError(null);
      await api.sessions.releasePickupRequest(sessionId, requestId);
      await refreshSession();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không thể nhả yêu cầu đón");
    }
  }, [refreshSession, sessionId]);

  const lockDeparture = useCallback(async () => {
    try {
      setError(null);
      await api.sessions.lockDeparture(sessionId);
      await refreshSession();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Cannot lock the trip");
    }
  }, [refreshSession, sessionId]);

  // Derived state
  const currentMember = members.find((m) => m.id === memberId) || null;
  const isHost = session?.members?.[0]?.id === memberId || members[0]?.id === memberId;
  const status = session?.status || SessionStatus.WaitingForMembers;
  const topVenues = optimizationResult?.topVenues || session?.latestOptimizationResult?.topVenues || [];
  const isVoting = status === SessionStatus.Voting;
  const isRoutePreview = status === SessionStatus.RoutePreview;
  const isCompleted = status === SessionStatus.Completed;

  return {
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
    memberLeaveNotice,
    notifyMemberLeft,
    refreshSession,
    startOptimization,
    submitVote,
    acceptPickupRequest,
    releasePickupRequest,
    lockDeparture,
  };
}
