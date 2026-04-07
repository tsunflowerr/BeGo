"use client";

import { memo, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Venue, formatDuration, formatDistance } from "@/types";

interface VenueCardProps {
  venue: Venue;
  rank: 1 | 2 | 3;
  isSelected?: boolean;
  isWinner?: boolean;
  canVote?: boolean;
  currentMemberId?: string;
  onVote?: (venueId: string) => void;
  onViewDetails?: (venue: Venue) => void;
}

const rankColors = {
  1: { bg: "bg-gradient-to-br from-yellow-400 to-amber-500", text: "text-amber-700" },
  2: { bg: "bg-gradient-to-br from-gray-300 to-gray-400", text: "text-gray-600" },
  3: { bg: "bg-gradient-to-br from-amber-600 to-amber-700", text: "text-amber-800" },
};

function VenueCardComponent({
  venue,
  rank,
  isSelected = false,
  isWinner = false,
  canVote = false,
  currentMemberId,
  onVote,
  onViewDetails,
}: VenueCardProps) {
  const [showDetails, setShowDetails] = useState(false);
  
  // Get current user's route info
  const myRoute = currentMemberId 
    ? venue.memberRoutes.find((r) => r.memberId === currentMemberId)
    : venue.memberRoutes[0];

  const renderStars = (rating: number) => {
    const fullStars = Math.floor(rating);
    const hasHalf = rating - fullStars >= 0.5;
    const stars = [];

    for (let i = 0; i < 5; i++) {
      if (i < fullStars) {
        stars.push(
          <svg key={i} className="w-4 h-4 text-yellow-400" fill="currentColor" viewBox="0 0 20 20">
            <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
          </svg>
        );
      } else if (i === fullStars && hasHalf) {
        stars.push(
          <svg key={i} className="w-4 h-4 text-yellow-400" fill="currentColor" viewBox="0 0 20 20">
            <defs>
              <linearGradient id="half">
                <stop offset="50%" stopColor="currentColor" />
                <stop offset="50%" stopColor="#e5e7eb" />
              </linearGradient>
            </defs>
            <path fill="url(#half)" d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
          </svg>
        );
      } else {
        stars.push(
          <svg key={i} className="w-4 h-4 text-gray-200" fill="currentColor" viewBox="0 0 20 20">
            <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
          </svg>
        );
      }
    }
    return stars;
  };

  return (
    <motion.div
      layout
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: (rank - 1) * 0.1 }}
      className={`relative bg-white rounded-2xl shadow-lg overflow-hidden transition-all duration-300 ${
        isSelected ? "ring-2 ring-[#ff1e00] ring-offset-2" : ""
      } ${isWinner ? "ring-2 ring-[#59ce8f] ring-offset-2" : ""}`}
    >
      {/* Winner badge */}
      {isWinner && (
        <div className="absolute top-3 right-3 z-10 px-3 py-1 bg-[#59ce8f] text-white text-xs font-bold rounded-full flex items-center gap-1">
          <span>🏆</span>
          <span>Địa điểm được chọn</span>
        </div>
      )}

      {/* Rank badge */}
      <div className={`absolute top-3 left-3 z-10 w-8 h-8 ${rankColors[rank].bg} rounded-full flex items-center justify-center text-white font-bold shadow-md`}>
        {rank}
      </div>

      {/* Photo */}
      <div className="relative h-40 bg-[#e8f9fd]">
        {venue.photoUrls && venue.photoUrls.length > 0 ? (
          <img
            src={venue.photoUrls[0]}
            alt={venue.name}
            className="w-full h-full object-cover"
          />
        ) : (
          <div className="w-full h-full flex items-center justify-center">
            <svg className="w-12 h-12 text-[#6b7280]" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
            </svg>
          </div>
        )}
      </div>

      {/* Content */}
      <div className="p-4">
        <h3 className="font-semibold text-[#1a1a2e] text-lg mb-1 line-clamp-1">
          {venue.name}
        </h3>

        {/* Rating */}
        <div className="flex items-center gap-2 mb-2">
          <div className="flex items-center">
            {renderStars(venue.rating)}
          </div>
          <span className="text-sm text-[#6b7280]">
            {venue.rating.toFixed(1)} ({venue.reviewCount} đánh giá)
          </span>
        </div>

        {/* Price level & Total time */}
        <div className="flex items-center gap-3 mb-2 text-sm">
          {venue.priceLevel && (
            <span className="text-[#59ce8f] font-medium">
              {"$".repeat(venue.priceLevel)}
              <span className="text-gray-300">{"$".repeat(4 - venue.priceLevel)}</span>
            </span>
          )}
          <span className="text-[#6b7280]">
            Tổng: {formatDuration(venue.totalTimeSeconds)}
          </span>
        </div>

        {/* Address */}
        <p className="text-sm text-[#6b7280] mb-3 line-clamp-2">
          <svg className="w-4 h-4 inline-block mr-1 -mt-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
          </svg>
          {venue.address}
        </p>

        {/* Distance to user */}
        {myRoute && (
          <div className="flex items-center gap-4 mb-4 p-2 bg-[#e8f9fd] rounded-lg">
            <div className="flex items-center gap-1">
              <svg className="w-4 h-4 text-[#ff1e00]" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <span className="text-sm font-medium text-[#1a1a2e]">
                {formatDuration(myRoute.estimatedTimeSeconds)}
              </span>
            </div>
            <div className="flex items-center gap-1">
              <svg className="w-4 h-4 text-[#59ce8f]" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6" />
              </svg>
              <span className="text-sm font-medium text-[#1a1a2e]">
                {formatDistance(myRoute.distanceMeters)}
              </span>
            </div>
          </div>
        )}

        {/* Actions */}
        <div className="flex items-center gap-2">
          {canVote && onVote && (
            <motion.button
              onClick={() => onVote(venue.venueId)}
              whileHover={{ scale: 1.02 }}
              whileTap={{ scale: 0.98 }}
              className={`flex-1 py-2.5 rounded-xl font-semibold text-sm flex items-center justify-center gap-2 transition-colors ${
                isSelected
                  ? "bg-[#ff1e00] text-white"
                  : "bg-[#e8f9fd] text-[#1a1a2e] hover:bg-[#ff1e00]/10"
              }`}
            >
              {isSelected ? (
                <>
                  <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                  </svg>
                  <span>Đã chọn</span>
                </>
              ) : (
                <>
                  <span>Bình chọn</span>
                </>
              )}
            </motion.button>
          )}

          <motion.button
            onClick={() => setShowDetails(!showDetails)}
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            className="px-4 py-2.5 rounded-xl bg-white border border-[#e8f9fd] text-[#6b7280] text-sm hover:bg-[#e8f9fd] transition-colors"
          >
            {showDetails ? "Ẩn" : "Chi tiết"}
          </motion.button>
        </div>
      </div>

      {/* Expanded details */}
      <AnimatePresence>
        {showDetails && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: "auto", opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="overflow-hidden"
          >
            <div className="px-4 pb-4 border-t border-[#e8f9fd] pt-4">
              {/* AI Summary */}
              {venue.aiReviewSummary && (
                <div className="mb-4">
                  <h4 className="text-sm font-semibold text-[#1a1a2e] mb-2 flex items-center gap-1">
                    <span>✨</span> Tóm tắt AI
                  </h4>
                  <p className="text-sm text-[#6b7280] bg-[#e8f9fd]/50 p-3 rounded-lg">
                    {venue.aiReviewSummary}
                  </p>
                </div>
              )}

              {/* Reviews */}
              {venue.topReviews && venue.topReviews.length > 0 && (
                <div>
                  <h4 className="text-sm font-semibold text-[#1a1a2e] mb-2">
                    Đánh giá nổi bật
                  </h4>
                  <div className="space-y-2 max-h-48 overflow-y-auto">
                    {venue.topReviews.slice(0, 3).map((review, idx) => (
                      <div key={idx} className="bg-gray-50 p-3 rounded-lg">
                        <div className="flex items-center gap-2 mb-1">
                          <span className="font-medium text-sm text-[#1a1a2e]">
                            {review.authorName}
                          </span>
                          <div className="flex">
                            {Array.from({ length: 5 }).map((_, i) => (
                              <svg
                                key={i}
                                className={`w-3 h-3 ${i < review.rating ? "text-yellow-400" : "text-gray-200"}`}
                                fill="currentColor"
                                viewBox="0 0 20 20"
                              >
                                <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
                              </svg>
                            ))}
                          </div>
                        </div>
                        <p className="text-xs text-[#6b7280] line-clamp-3">
                          {review.text}
                        </p>
                        <p className="text-[10px] text-[#9ca3af] mt-1">
                          {review.relativeTime}
                        </p>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* All member routes */}
              <div className="mt-4">
                <h4 className="text-sm font-semibold text-[#1a1a2e] mb-2">
                  Thời gian di chuyển của mọi người
                </h4>
                <div className="space-y-1">
                  {venue.memberRoutes.map((route) => (
                    <div
                      key={route.memberId}
                      className="flex items-center justify-between py-1.5 px-2 rounded-lg hover:bg-gray-50"
                    >
                      <span className="text-sm text-[#1a1a2e]">{route.memberName}</span>
                      <span className="text-sm text-[#6b7280]">
                        {formatDuration(route.estimatedTimeSeconds)} • {formatDistance(route.distanceMeters)}
                      </span>
                    </div>
                  ))}
                </div>
              </div>

              {/* Photos gallery */}
              {venue.photoUrls && venue.photoUrls.length > 1 && (
                <div className="mt-4">
                  <h4 className="text-sm font-semibold text-[#1a1a2e] mb-2">Hình ảnh</h4>
                  <div className="flex gap-2 overflow-x-auto pb-2">
                    {venue.photoUrls.slice(1, 5).map((url, idx) => (
                      <img
                        key={idx}
                        src={url}
                        alt={`${venue.name} ${idx + 2}`}
                        className="w-20 h-20 rounded-lg object-cover flex-shrink-0"
                      />
                    ))}
                  </div>
                </div>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </motion.div>
  );
}

export const VenueCard = memo(VenueCardComponent);
