"use client";

import { memo } from "react";
import { motion } from "framer-motion";

interface VotingProgressProps {
  totalVotes: number;
  totalMembers: number;
  hasVoted: boolean;
}

function VotingProgressComponent({ totalVotes, totalMembers, hasVoted }: VotingProgressProps) {
  const progress = totalMembers > 0 ? (totalVotes / totalMembers) * 100 : 0;

  return (
    <div className="bg-[#e8f9fd] rounded-xl p-4">
      <div className="flex items-center justify-between mb-2">
        <span className="text-sm font-medium text-[#1a1a2e]">Tiến độ bình chọn</span>
        <span className="text-sm text-[#6b7280]">
          {totalVotes}/{totalMembers} người
        </span>
      </div>
      
      <div className="relative h-3 bg-white rounded-full overflow-hidden">
        <motion.div
          initial={{ width: 0 }}
          animate={{ width: `${progress}%` }}
          transition={{ duration: 0.5, ease: "easeOut" }}
          className="absolute inset-y-0 left-0 bg-gradient-to-r from-[#ff1e00] to-[#ff4d33] rounded-full"
        />
      </div>

      {hasVoted && (
        <div className="flex items-center gap-1 mt-2 text-[#59ce8f]">
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
          </svg>
          <span className="text-xs font-medium">Bạn đã bình chọn</span>
        </div>
      )}

      {!hasVoted && totalVotes > 0 && (
        <p className="text-xs text-[#6b7280] mt-2">
          Đang chờ bạn bình chọn...
        </p>
      )}
    </div>
  );
}

export const VotingProgress = memo(VotingProgressComponent);
