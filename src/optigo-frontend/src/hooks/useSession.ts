"use client";

import { useState, useEffect, useCallback, useRef } from "react";
import { api } from "@/lib/api";
import {
  Session,
  Member,
  SessionStatus,
  OptimizationResult,
  Venue,
  MemberJoinedEvent,
  OptimizationCompletedEvent,
  VoteSubmittedEvent,
  VotingCompletedEvent,
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
  isHost: boolean;
  currentMember: Member | null;
  
  // Optimization data
  optimizationResult: OptimizationResult | null;
  topVenues: Venue[];
  winningVenueId: string | null;
  
  // Voting
  votingProgress: { total: number; voted: number };
  hasVoted: boolean;
  
  // State
  loading: boolean;
  error: string | null;
  status: SessionStatus;
  isComputing: boolean;
  isVoting: boolean;
  isCompleted: boolean;
  
  // Connection
  isConnected: boolean;
  
  // Actions
  refreshSession: () => Promise<void>;
  startOptimization: (query?: string) => Promise<void>;
  submitVote: (venueId: string) => Promise<void>;
}

export function useSession({ sessionId, memberId }: UseSessionOptions): UseSessionReturn {
  const [session, setSession] = useState<Session | null>(null);
  const [members, setMembers] = useState<Member[]>([]);
  const [optimizationResult, setOptimizationResult] = useState<OptimizationResult | null>(null);
  const [winningVenueId, setWinningVenueId] = useState<string | null>(null);
  const [votingProgress, setVotingProgress] = useState({ total: 0, voted: 0 });
  const [hasVoted, setHasVoted] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isComputing, setIsComputing] = useState(false);
  
  const loadedRef = useRef(false);

  // SignalR event handlers
  const handleMemberJoined = useCallback((event: MemberJoinedEvent) => {
    setMembers((prev) => {
      const nextMember: Member = {
        id: event.memberId,
        sessionId: event.sessionId,
        name: event.memberName,
        latitude: event.latitude,
        longitude: event.longitude,
        transportMode: event.transportMode,
        joinedAt: event.joinedAt,
        isHost: event.isHost,
      };

      if (prev.some((m) => m.id === event.memberId)) {
        return prev.map((member) => member.id === event.memberId ? nextMember : member);
      }

      return [...prev, nextMember];
    });
  }, []);

  const handleComputingStarted = useCallback(() => {
    setIsComputing(true);
    setSession((prev) => prev ? { ...prev, status: SessionStatus.Computing } : null);
  }, []);

  const handleOptimizationCompleted = useCallback((event: OptimizationCompletedEvent) => {
    setIsComputing(false);
    setOptimizationResult(event.result);
    setWinningVenueId(null);
    setHasVoted(false);
    setSession((prev) => prev ? { ...prev, status: SessionStatus.Voting } : null);
    setVotingProgress({ total: members.length, voted: 0 });
  }, [members.length]);

  const handleVoteSubmitted = useCallback((event: VoteSubmittedEvent) => {
    setVotingProgress({ total: event.totalMembers, voted: event.totalVotes });
    if (event.memberId === memberId) {
      setHasVoted(true);
    }
  }, [memberId]);

  const handleVotingCompleted = useCallback((event: VotingCompletedEvent) => {
    setWinningVenueId(event.winningVenueId);
    setSession((prev) => prev ? { ...prev, status: SessionStatus.Completed } : null);
  }, []);

  const handleError = useCallback((error: { code: string; message: string }) => {
    setError(error.message);
  }, []);

  // SignalR connection
  const { isConnected } = useSignalR({
    sessionId,
    onMemberJoined: handleMemberJoined,
    onComputingStarted: handleComputingStarted,
    onOptimizationCompleted: handleOptimizationCompleted,
    onVoteSubmitted: handleVoteSubmitted,
    onVotingCompleted: handleVotingCompleted,
    onError: handleError,
  });

  // Fetch session data
  const refreshSession = useCallback(async () => {
    try {
      setError(null);
      const data = await api.sessions.get(sessionId);
      setSession(data);
      setMembers(data.members || []);
      
      if (data.status === SessionStatus.Voting || data.status === SessionStatus.Completed) {
        // Session already has results, we might need to fetch them
        // For now, we rely on SignalR to deliver results
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không thể tải thông tin phòng");
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  // Initial load
  useEffect(() => {
    if (!loadedRef.current) {
      loadedRef.current = true;
      refreshSession();
    }
  }, [refreshSession]);

  // Start optimization
  const startOptimization = useCallback(async (query?: string) => {
    try {
      setIsComputing(true);
      setError(null);
      const result = await api.optimizer.findMeetingPoint(sessionId, query);
      setOptimizationResult(result);
      setWinningVenueId(null);
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
      setError("Bạn cần tham gia phòng trước khi bình chọn");
      return;
    }
    
    try {
      setError(null);
      const response = await api.vote.submit(sessionId, memberId, venueId);
      setHasVoted(true);
      
      if (response.isVotingCompleted && response.winningVenueId) {
        setWinningVenueId(response.winningVenueId);
        setSession((prev) => prev ? { ...prev, status: SessionStatus.Completed } : null);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không thể gửi bình chọn");
    }
  }, [sessionId, memberId]);

  // Derived state
  const currentMember = members.find((m) => m.id === memberId) || null;
  const isHost = session?.members?.[0]?.id === memberId || members[0]?.id === memberId;
  const status = session?.status || SessionStatus.WaitingForMembers;
  const topVenues = optimizationResult?.topVenues || [];
  const isVoting = status === SessionStatus.Voting;
  const isCompleted = status === SessionStatus.Completed;

  return {
    session,
    members,
    isHost,
    currentMember,
    optimizationResult,
    topVenues,
    winningVenueId,
    votingProgress,
    hasVoted,
    loading,
    error,
    status,
    isComputing,
    isVoting,
    isCompleted,
    isConnected,
    refreshSession,
    startOptimization,
    submitVote,
  };
}
