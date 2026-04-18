"use client";

import { memo } from "react";
import { motion } from "framer-motion";
import { Member, MemberMobilityRole, mobilityRoleLabels, transportModeIcons } from "@/types";

interface MemberListProps {
  members: Member[];
  hostMemberId?: string;
  currentMemberId?: string;
  winningVenueId?: string;
  showDistances?: boolean;
  memberDistances?: Map<string, { time: number; distance: number }>;
}

function MemberListComponent({
  members,
  hostMemberId,
  currentMemberId,
  winningVenueId,
  showDistances = false,
  memberDistances,
}: MemberListProps) {
  // Sort members: host first, then by join time
  const sortedMembers = [...members].sort((a, b) => {
    if (a.id === hostMemberId) return -1;
    if (b.id === hostMemberId) return 1;
    return new Date(a.joinedAt).getTime() - new Date(b.joinedAt).getTime();
  });

  const formatTime = (seconds: number): string => {
    if (seconds < 60) return `${Math.round(seconds)} giây`;
    const mins = Math.floor(seconds / 60);
    if (mins < 60) return `${mins} phút`;
    const hours = Math.floor(mins / 60);
    const remainMins = mins % 60;
    return remainMins > 0 ? `${hours}h ${remainMins}p` : `${hours} giờ`;
  };

  const formatDistance = (meters: number): string => {
    if (meters < 1000) return `${Math.round(meters)}m`;
    return `${(meters / 1000).toFixed(1)}km`;
  };

  if (members.length === 0) {
    return (
      <div className="text-center py-8">
        <div className="w-16 h-16 mx-auto mb-3 rounded-full bg-[#e8f9fd] flex items-center justify-center">
          <svg className="w-8 h-8 text-[#6b7280]" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z" />
          </svg>
        </div>
        <p className="text-[#6b7280] text-sm">Chưa có thành viên</p>
        <p className="text-[#6b7280] text-xs mt-1">Chia sẻ link để mời bạn bè</p>
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between mb-3">
        <h3 className="font-semibold text-[#1a1a2e] text-sm">
          Thành viên ({members.length})
        </h3>
        <span className="w-2 h-2 bg-[#59ce8f] rounded-full animate-pulse" />
      </div>
      
      <div className="space-y-2 max-h-[300px] overflow-y-auto pr-1">
        {sortedMembers.map((member, index) => {
          const isHost = member.id === hostMemberId;
          const isCurrentUser = member.id === currentMemberId;
          const distanceInfo = memberDistances?.get(member.id);
          
          return (
            <motion.div
              key={member.id}
              initial={{ opacity: 0, x: 20 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: index * 0.05 }}
              className={`flex items-center gap-3 p-3 rounded-xl transition-colors ${
                isCurrentUser 
                  ? "bg-[#ff1e00]/5 border border-[#ff1e00]/20" 
                  : "bg-[#e8f9fd]/50 hover:bg-[#e8f9fd]"
              }`}
            >
              {/* Avatar */}
              <div className="relative flex-shrink-0">
                <div className={`w-10 h-10 rounded-full flex items-center justify-center text-white font-semibold text-sm ${
                  isHost 
                    ? "bg-gradient-to-br from-[#ff1e00] to-[#ff4d33]" 
                    : "bg-gradient-to-br from-[#59ce8f] to-[#7dd9a7]"
                }`}>
                  {member.name.charAt(0).toUpperCase()}
                </div>
                {isHost && (
                  <span className="absolute -top-1 -right-1 w-5 h-5 bg-yellow-400 rounded-full flex items-center justify-center text-[10px]">
                    👑
                  </span>
                )}
              </div>
              
              {/* Info */}
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="font-medium text-[#1a1a2e] text-sm truncate">
                    {member.name}
                  </span>
                  {isCurrentUser && (
                    <span className="text-[10px] px-1.5 py-0.5 bg-[#ff1e00] text-white rounded-full">
                      Bạn
                    </span>
                  )}
                </div>
                
                {showDistances && distanceInfo ? (
                  <div className="flex items-center gap-2 mt-0.5">
                    <span className="text-xs text-[#6b7280]">
                      {formatTime(distanceInfo.time)} • {formatDistance(distanceInfo.distance)}
                    </span>
                  </div>
                ) : (
                  <div className="mt-0.5 flex flex-wrap items-center gap-1.5">
                    <span className="text-sm">{transportModeIcons[member.transportMode]}</span>
                    <span className="text-xs text-[#6b7280]">{mobilityRoleLabels[member.mobilityRole]}</span>
                    {member.canOfferPickup && (
                      <span className="rounded-full bg-[#59ce8f]/10 px-1.5 py-0.5 text-[10px] font-medium text-[#2b8a57]">
                        Còn {member.availableSeatCount ?? 0} chỗ
                      </span>
                    )}
                    {!member.canOfferPickup && (
                      <span className="text-xs text-[#6b7280]">
                        {member.latitude.toFixed(4)}, {member.longitude.toFixed(4)}
                      </span>
                    )}
                    {member.driverId && (
                      <span className="rounded-full bg-[#ff1e00]/10 px-1.5 py-0.5 text-[10px] font-medium text-[#ff1e00]">
                        Đã ghép tài xế
                      </span>
                    )}
                    {!member.driverId && member.mobilityRole === MemberMobilityRole.NeedsPickup && (
                      <span className="rounded-full bg-[#fff5d6] px-1.5 py-0.5 text-[10px] font-medium text-[#9a6700]">
                        Chờ nhận đón
                      </span>
                    )}
                    {member.canOfferPickup && (
                      <span className="text-xs text-[#6b7280]">
                        {member.latitude.toFixed(4)}, {member.longitude.toFixed(4)}
                      </span>
                    )}
                  </div>
                )}
              </div>
            </motion.div>
          );
        })}
      </div>
    </div>
  );
}

export const MemberList = memo(MemberListComponent);
