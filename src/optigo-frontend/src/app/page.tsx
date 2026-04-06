"use client";

import { useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import { Navbar, HeroSection, PhaseButton, CreateRoomModal } from "@/components";
import { api } from "@/lib/api";

// Phase icons
const LocationIcon = () => (
  <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 11a3 3 0 11-6 0 3 3 0 016 0z" />
  </svg>
);

const RouteIcon = () => (
  <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 7h12m0 0l-4-4m4 4l-4 4m0 6H4m0 0l4 4m-4-4l4-4" />
  </svg>
);

const CalendarIcon = () => (
  <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
  </svg>
);

export default function Home() {
  const router = useRouter();
  const [isModalOpen, setIsModalOpen] = useState(false);

  const handleOpenModal = useCallback(() => {
    setIsModalOpen(true);
  }, []);

  const handleCloseModal = useCallback(() => {
    setIsModalOpen(false);
  }, []);

  const handleCreateRoom = useCallback(async (data: { hostName: string; defaultQuery: string }) => {
    const response = await api.sessions.create({
      hostName: data.hostName,
      defaultQuery: data.defaultQuery,
    });
    
    router.push(`/room/${response.sessionId}`);
  }, [router]);

  const handleSignOut = useCallback(() => {
    // TODO: Implement actual sign out when Google auth is configured
    console.log("Sign out clicked");
  }, []);

  return (
    <div className="flex flex-col min-h-screen">
      <Navbar onSignOut={handleSignOut} />
      
      <main className="flex-1 flex flex-col lg:flex-row max-w-7xl mx-auto w-full px-4 sm:px-6 lg:px-8 py-8 gap-8">
        {/* Left side - Hero Section */}
        <div className="flex-1 flex items-center">
          <HeroSection />
        </div>

        {/* Right side - Phase Buttons */}
        <div className="w-full lg:w-96 flex flex-col justify-center gap-4">
          <PhaseButton
            phase={1}
            title="Tìm Điểm Hẹn"
            description="Tìm địa điểm tối ưu cho mọi người trong nhóm dựa trên thời gian di chuyển thực tế"
            icon={<LocationIcon />}
            onClick={handleOpenModal}
          />
          
          <PhaseButton
            phase={2}
            title="Đi Chung Xe"
            description="Tối ưu hóa việc di chuyển chung, đề xuất phương án đón nhau hiệu quả nhất"
            icon={<RouteIcon />}
            comingSoon
          />
          
          <PhaseButton
            phase={3}
            title="Lập Lịch Trình"
            description="Xây dựng kế hoạch hoạt động cho cả nhóm với AI hỗ trợ thông minh"
            icon={<CalendarIcon />}
            comingSoon
          />
        </div>
      </main>

      {/* Create Room Modal */}
      <CreateRoomModal
        isOpen={isModalOpen}
        onClose={handleCloseModal}
        onSubmit={handleCreateRoom}
      />
    </div>
  );
}
