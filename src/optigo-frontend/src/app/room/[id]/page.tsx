"use client";

import { useParams } from "next/navigation";
import { motion, AnimatePresence } from "framer-motion";
import { useCallback, useState, useEffect, useRef } from "react";
import {
  Navbar,
  Logo,
  ShareLinkSection,
  MemberList,
  MapView,
  VenueCard,
  JoinRoomModal,
  VotingProgress,
  FeatureSuggestion,
  ToastContainer,
  useToasts,
  QueryEditor,
} from "@/components";
import { useSession, useGeolocation } from "@/hooks";
import { api } from "@/lib/api";
import {
  TransportMode,
  SessionStatus,
} from "@/types";

export default function RoomPage() {
  const params = useParams();
  const sessionId = params.id as string;

  // Toast notifications
  const { toasts, removeToast, success, info, warning } = useToasts();
  const prevMembersCount = useRef(0);
  const prevStatus = useRef<SessionStatus | null>(null);

  // Local state
  const [memberId, setMemberId] = useState<string | null>(null);
  const [showJoinModal, setShowJoinModal] = useState(false);
  const [selectedVenueId, setSelectedVenueId] = useState<string | null>(null);
  const [hasJoined, setHasJoined] = useState(false);

  // Get geolocation
  const {
    latitude,
    longitude,
    error: locationError,
    loading: locationLoading,
  } = useGeolocation();

  // Get session data
  const {
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
  } = useSession({ sessionId, memberId });

  // Toast notifications for events
  useEffect(() => {
    // New member joined
    if (members.length > prevMembersCount.current && prevMembersCount.current > 0) {
      const newMember = members[members.length - 1];
      if (newMember && newMember.id !== memberId) {
        info(`${newMember.name} đã tham gia phòng`);
      }
    }
    prevMembersCount.current = members.length;
  }, [members, memberId, info]);

  useEffect(() => {
    // Status changes
    if (prevStatus.current !== null && prevStatus.current !== status) {
      if (status === SessionStatus.Computing) {
        info("🔍 Hệ thống đang tính toán điểm hẹn tối ưu...");
      } else if (status === SessionStatus.Voting) {
        success("✨ Đã tìm được Top 3 địa điểm! Hãy bình chọn.");
      } else if (status === SessionStatus.Completed) {
        success("🎉 Nhóm đã chọn được điểm hẹn!");
      }
    }
    prevStatus.current = status;
  }, [status, info, success]);

  // Check if user has already joined (from localStorage)
  useEffect(() => {
    const storedMemberId = localStorage.getItem(`room-${sessionId}-memberId`);
    if (storedMemberId) {
      setMemberId(storedMemberId);
      setHasJoined(true);
    } else {
      // Show join modal after a short delay
      const timer = setTimeout(() => {
        setShowJoinModal(true);
      }, 500);
      return () => clearTimeout(timer);
    }
  }, [sessionId]);

  // Handle sign out
  const handleSignOut = useCallback(() => {
    localStorage.removeItem(`room-${sessionId}-memberId`);
    setMemberId(null);
    setHasJoined(false);
    setShowJoinModal(true);
  }, [sessionId]);

  // Handle join room
  const handleJoinRoom = useCallback(async (data: {
    memberName: string;
    latitude: number;
    longitude: number;
    transportMode: TransportMode;
  }) => {
    const response = await api.sessions.join(sessionId, data);
    setMemberId(response.memberId);
    localStorage.setItem(`room-${sessionId}-memberId`, response.memberId);
    setHasJoined(true);
    setShowJoinModal(false);
    success(`Chào mừng ${data.memberName}! Bạn đã tham gia phòng.`);
    await refreshSession();
  }, [sessionId, refreshSession, success]);

  // Handle start optimization
  const handleStartOptimization = useCallback(async () => {
    info("🔍 Đang tìm kiếm điểm hẹn tối ưu...");
    await startOptimization(session?.queryText || "cafe");
  }, [startOptimization, session?.queryText, info]);

  // Handle vote
  const handleVote = useCallback(async (venueId: string) => {
    setSelectedVenueId(venueId);
    await submitVote(venueId);
    success("Đã gửi bình chọn của bạn!");
  }, [submitVote, success]);

  // Get winning venue
  const winningVenue = winningVenueId
    ? topVenues.find((v) => v.venueId === winningVenueId)
    : null;

  // Calculate member distances to winning venue
  const memberDistances = winningVenue
    ? new Map(
        winningVenue.memberRoutes.map((r) => [
          r.memberId,
          { time: r.estimatedTimeSeconds, distance: r.distanceMeters },
        ])
      )
    : undefined;

  // Render loading state
  if (loading) {
    return (
      <div className="flex flex-col min-h-screen bg-white">
        <Navbar onSignOut={handleSignOut} />
        <main className="flex-1 flex items-center justify-center">
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            className="text-center"
          >
            <motion.div
              animate={{ rotate: 360 }}
              transition={{ duration: 2, repeat: Infinity, ease: "linear" }}
              className="w-16 h-16 mx-auto border-4 border-[#e8f9fd] border-t-[#ff1e00] rounded-full mb-4"
            />
            <p className="text-[#1a1a2e] font-medium">Đang tải phòng...</p>
          </motion.div>
        </main>
      </div>
    );
  }

  // Render error state
  if (error && !session) {
    return (
      <div className="flex flex-col min-h-screen bg-white">
        <Navbar onSignOut={handleSignOut} />
        <main className="flex-1 flex items-center justify-center p-4">
          <motion.div
            initial={{ opacity: 0, scale: 0.9 }}
            animate={{ opacity: 1, scale: 1 }}
            className="text-center max-w-md"
          >
            <div className="w-20 h-20 mx-auto mb-4 bg-red-50 rounded-full flex items-center justify-center">
              <svg className="w-10 h-10 text-red-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
              </svg>
            </div>
            <h2 className="text-xl font-semibold text-[#1a1a2e] mb-2">Không tìm thấy phòng</h2>
            <p className="text-[#6b7280] mb-4">{error}</p>
            <a
              href="/"
              className="inline-flex items-center gap-2 px-6 py-3 bg-[#ff1e00] text-white rounded-xl font-medium hover:bg-[#cc1800] transition-colors"
            >
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6" />
              </svg>
              Về trang chủ
            </a>
          </motion.div>
        </main>
      </div>
    );
  }

  return (
    <div className="flex flex-col min-h-screen bg-gradient-to-br from-white via-white to-[#e8f9fd]/30">
      <Navbar
        user={currentMember ? { name: currentMember.name } : undefined}
        onSignOut={handleSignOut}
      />

      <main className="flex-1 flex flex-col items-center justify-center p-4 sm:p-6 lg:p-8">
        <motion.div
          initial={{ opacity: 0, scale: 0.95 }}
          animate={{ opacity: 1, scale: 1 }}
          transition={{ duration: 0.4 }}
          className="w-full max-w-6xl bg-white rounded-3xl shadow-2xl overflow-hidden border border-[#e8f9fd]"
        >
          {/* Header */}
          <div className="bg-gradient-to-r from-[#ff1e00] to-[#ff4d33] p-6 sm:p-8">
            <div className="flex flex-col sm:flex-row items-center justify-between gap-4">
              <div className="flex items-center gap-4">
                <Logo size={56} className="drop-shadow-lg" />
                <div className="text-center sm:text-left">
                  <h1 className="text-xl sm:text-2xl font-bold text-white">
                    {session?.queryText || "Tìm điểm hẹn"}
                  </h1>
                  <p className="text-white/80 text-sm">
                    {status === SessionStatus.WaitingForMembers && "Đang chờ thành viên"}
                    {status === SessionStatus.Computing && "Đang tìm kiếm..."}
                    {status === SessionStatus.Voting && "Đang bình chọn"}
                    {status === SessionStatus.Completed && "Đã hoàn thành"}
                  </p>
                </div>
              </div>

              {/* Connection status */}
              <div className="flex items-center gap-2">
                <span
                  className={`w-2.5 h-2.5 rounded-full ${
                    isConnected ? "bg-[#59ce8f] animate-pulse" : "bg-gray-400"
                  }`}
                />
                <span className="text-white/80 text-sm">
                  {isConnected ? "Đã kết nối" : "Đang kết nối..."}
                </span>
              </div>
            </div>
          </div>

          {/* Share link + Query Editor */}
          <div className="p-4 sm:p-6 border-b border-[#e8f9fd] space-y-4">
            <ShareLinkSection sessionId={sessionId} />
            <QueryEditor
              sessionId={sessionId}
              initialQuery={session?.queryText || ""}
              isHost={isHost}
              isEditable={status === SessionStatus.WaitingForMembers}
              onQueryUpdated={refreshSession}
            />
          </div>

          {/* Main content */}
          <div className="p-4 sm:p-6">
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
              {/* Left side - Map */}
              <div className="lg:col-span-2">
                <div className="h-[400px] lg:h-[500px]">
                  <MapView
                    members={members}
                    geometricMedian={optimizationResult?.geometricMedian}
                    venues={isCompleted && winningVenue ? [winningVenue] : topVenues}
                    winningVenueId={winningVenueId || undefined}
                    isLoading={isComputing}
                  />
                </div>
              </div>

              {/* Right side - Members */}
              <div className="lg:col-span-1">
                <MemberList
                  members={members}
                  hostMemberId={session?.members?.[0]?.id}
                  currentMemberId={memberId || undefined}
                  winningVenueId={winningVenueId || undefined}
                  showDistances={isCompleted}
                  memberDistances={memberDistances}
                />

                {/* Start button (for host only, in waiting state) */}
                {status === SessionStatus.WaitingForMembers && isHost && members.length > 0 && (
                  <motion.button
                    onClick={handleStartOptimization}
                    whileHover={{ scale: 1.02 }}
                    whileTap={{ scale: 0.98 }}
                    className="w-full mt-4 py-3.5 rounded-xl font-semibold text-white bg-gradient-to-r from-[#ff1e00] to-[#ff4d33] hover:from-[#cc1800] hover:to-[#ff1e00] transition-all shadow-lg shadow-[#ff1e00]/30 flex items-center justify-center gap-2"
                  >
                    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                    </svg>
                    <span>Bắt đầu tìm kiếm</span>
                  </motion.button>
                )}

                {/* Waiting message (for non-host) */}
                {status === SessionStatus.WaitingForMembers && !isHost && hasJoined && (
                  <div className="mt-4 p-4 bg-[#e8f9fd] rounded-xl text-center">
                    <p className="text-sm text-[#6b7280]">
                      Đang chờ chủ phòng bắt đầu tìm kiếm...
                    </p>
                  </div>
                )}
              </div>
            </div>

            {/* Voting section */}
            <AnimatePresence>
              {isVoting && topVenues.length > 0 && (
                <motion.div
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -20 }}
                  className="mt-6"
                >
                  <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 mb-4">
                    <div>
                      <h2 className="text-xl font-bold text-[#1a1a2e]">
                        Top 3 địa điểm tối ưu
                      </h2>
                      <p className="text-[#6b7280] text-sm">
                        Bình chọn địa điểm bạn muốn đến
                      </p>
                    </div>
                    <VotingProgress
                      totalVotes={votingProgress.voted}
                      totalMembers={votingProgress.total}
                      hasVoted={hasVoted}
                    />
                  </div>

                  <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                    {topVenues.map((venue, index) => (
                      <VenueCard
                        key={venue.venueId}
                        venue={venue}
                        rank={(index + 1) as 1 | 2 | 3}
                        isSelected={selectedVenueId === venue.venueId || hasVoted && venue.venueId === selectedVenueId}
                        canVote={!hasVoted}
                        currentMemberId={memberId || undefined}
                        onVote={handleVote}
                      />
                    ))}
                  </div>
                </motion.div>
              )}
            </AnimatePresence>

            {/* Completed section */}
            <AnimatePresence>
              {isCompleted && winningVenue && (
                <motion.div
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  className="mt-6"
                >
                  <div className="text-center mb-6">
                    <motion.div
                      initial={{ scale: 0 }}
                      animate={{ scale: 1 }}
                      transition={{ type: "spring", damping: 10 }}
                      className="inline-flex items-center gap-2 px-6 py-3 bg-[#59ce8f] text-white rounded-full font-semibold shadow-lg shadow-[#59ce8f]/30"
                    >
                      <span className="text-xl">🎉</span>
                      <span>Nhóm đã chọn được điểm hẹn!</span>
                    </motion.div>
                  </div>

                  <div className="max-w-lg mx-auto">
                    <VenueCard
                      venue={winningVenue}
                      rank={1}
                      isWinner
                      currentMemberId={memberId || undefined}
                    />
                  </div>

                  {/* Feature suggestions */}
                  <FeatureSuggestion />
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </motion.div>
      </main>

      {/* Toast notifications */}
      <ToastContainer toasts={toasts} onRemove={removeToast} />

      {/* Join Room Modal */}
      <JoinRoomModal
        isOpen={showJoinModal && !hasJoined}
        sessionId={sessionId}
        onClose={() => setShowJoinModal(false)}
        onJoin={handleJoinRoom}
        initialLocation={latitude && longitude ? { latitude, longitude } : null}
        locationError={locationError}
        locationLoading={locationLoading}
      />
    </div>
  );
}
