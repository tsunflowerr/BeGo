"use client";

import { useParams } from "next/navigation";
import { motion } from "framer-motion";
import { Navbar, Logo } from "@/components";
import { useCallback } from "react";

export default function RoomPage() {
  const params = useParams();
  const roomId = params.id as string;

  const handleSignOut = useCallback(() => {
    console.log("Sign out clicked");
  }, []);

  const handleCopyLink = useCallback(() => {
    const link = window.location.href;
    navigator.clipboard.writeText(link);
  }, []);

  return (
    <div className="flex flex-col min-h-screen">
      <Navbar onSignOut={handleSignOut} />

      <main className="flex-1 flex flex-col items-center justify-center p-8">
        <motion.div
          initial={{ opacity: 0, scale: 0.9 }}
          animate={{ opacity: 1, scale: 1 }}
          transition={{ duration: 0.4 }}
          className="w-full max-w-2xl bg-white rounded-3xl shadow-xl overflow-hidden"
        >
          {/* Header */}
          <div className="bg-gradient-to-r from-[#5D7B6F] to-[#A4C3A2] p-8 text-center">
            <motion.div
              initial={{ y: -20, opacity: 0 }}
              animate={{ y: 0, opacity: 1 }}
              transition={{ delay: 0.2 }}
            >
              <Logo size={80} className="mx-auto mb-4 drop-shadow-lg" />
              <h1 className="text-2xl font-bold text-white mb-2">
                Phòng đã được tạo!
              </h1>
              <p className="text-white/80">
                Chia sẻ link để mời bạn bè tham gia
              </p>
            </motion.div>
          </div>

          {/* Content */}
          <div className="p-8 space-y-6">
            {/* Room ID */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.3 }}
              className="text-center"
            >
              <p className="text-sm text-[#5D7B6F]/60 mb-2">Mã phòng</p>
              <p className="font-mono text-lg text-[#5D7B6F] bg-[#D7F9FA]/50 px-4 py-2 rounded-lg inline-block">
                {roomId}
              </p>
            </motion.div>

            {/* Share Link */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.4 }}
              className="space-y-3"
            >
              <p className="text-sm text-[#5D7B6F]/60 text-center">Link chia sẻ</p>
              <div className="flex gap-2">
                <input
                  type="text"
                  readOnly
                  value={typeof window !== "undefined" ? window.location.href : ""}
                  className="flex-1 px-4 py-3 rounded-xl border-2 border-[#B0D4B8] bg-[#D7F9FA]/30 text-[#5D7B6F] text-sm truncate"
                />
                <motion.button
                  onClick={handleCopyLink}
                  whileHover={{ scale: 1.05 }}
                  whileTap={{ scale: 0.95 }}
                  className="px-4 py-3 rounded-xl bg-[#5D7B6F] text-white font-medium hover:bg-[#5D7B6F]/90 transition-colors flex items-center gap-2"
                >
                  <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
                  </svg>
                  <span className="hidden sm:inline">Sao chép</span>
                </motion.button>
              </div>
            </motion.div>

            {/* Status */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.5 }}
              className="bg-[#EAE7D6]/50 rounded-xl p-6 text-center"
            >
              <div className="flex items-center justify-center gap-2 mb-3">
                <span className="w-3 h-3 bg-green-500 rounded-full animate-pulse" />
                <span className="text-[#5D7B6F] font-medium">Đang chờ thành viên</span>
              </div>
              <p className="text-sm text-[#5D7B6F]/60">
                Khi các thành viên tham gia, họ sẽ xuất hiện ở đây. <br />
                Bạn có thể bắt đầu tìm điểm hẹn khi đủ người.
              </p>
            </motion.div>

            {/* Members placeholder */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.6 }}
              className="space-y-3"
            >
              <p className="text-sm font-medium text-[#5D7B6F]">Thành viên (0)</p>
              <div className="border-2 border-dashed border-[#B0D4B8]/50 rounded-xl p-8 text-center">
                <svg className="w-12 h-12 mx-auto text-[#B0D4B8]/50 mb-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
                </svg>
                <p className="text-[#5D7B6F]/50 text-sm">
                  Chưa có thành viên nào tham gia
                </p>
              </div>
            </motion.div>
          </div>
        </motion.div>
      </main>
    </div>
  );
}
