"use client";

import { memo, useCallback, useEffect, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { api } from "@/lib/api";
import { MemberMobilityRole, TransportMode, transportModeIcons, transportModeLabels } from "@/types";

interface AddTestMemberFabProps {
  sessionId: string;
  existingMemberCount: number;
  onMemberAdded?: () => Promise<void> | void;
}

interface HanoiLocationSeed {
  label: string;
  latitude: number;
  longitude: number;
}

const HANOI_SEEDS: HanoiLocationSeed[] = [
  { label: "Hoan Kiem", latitude: 21.0285, longitude: 105.8542 },
  { label: "Ba Dinh", latitude: 21.0367, longitude: 105.8348 },
  { label: "Dong Da", latitude: 21.0180, longitude: 105.8292 },
  { label: "Cau Giay", latitude: 21.0362, longitude: 105.7902 },
  { label: "Tay Ho", latitude: 21.0700, longitude: 105.8188 },
  { label: "Hai Ba Trung", latitude: 21.0055, longitude: 105.8577 },
  { label: "Thanh Xuan", latitude: 20.9968, longitude: 105.8070 },
  { label: "Long Bien", latitude: 21.0481, longitude: 105.8882 },
];

function generateRandomHanoiLocation() {
  const seed = HANOI_SEEDS[Math.floor(Math.random() * HANOI_SEEDS.length)];
  const latitude = seed.latitude + (Math.random() - 0.5) * 0.012;
  const longitude = seed.longitude + (Math.random() - 0.5) * 0.012;

  return {
    label: `${seed.label}, Hà Nội`,
    latitude,
    longitude,
  };
}

function AddTestMemberFabComponent({
  sessionId,
  existingMemberCount,
  onMemberAdded,
}: AddTestMemberFabProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [mode, setMode] = useState<"random" | "manual">("random");
  const [memberName, setMemberName] = useState("");
  const [transportMode, setTransportMode] = useState<TransportMode>(TransportMode.Motorbike);
  const [latitude, setLatitude] = useState("");
  const [longitude, setLongitude] = useState("");
  const [locationLabel, setLocationLabel] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const resetDraft = useCallback(() => {
    const nextIndex = existingMemberCount + 1;
    const randomLocation = generateRandomHanoiLocation();

    setMemberName(`Test ${nextIndex}`);
    setTransportMode(TransportMode.Motorbike);
    setLatitude(randomLocation.latitude.toFixed(6));
    setLongitude(randomLocation.longitude.toFixed(6));
    setLocationLabel(randomLocation.label);
    setError(null);
    setMode("random");
  }, [existingMemberCount]);

  useEffect(() => {
    if (isOpen) {
      resetDraft();
    }
  }, [isOpen, resetDraft]);

  const handleRandomize = useCallback(() => {
    const randomLocation = generateRandomHanoiLocation();
    setLatitude(randomLocation.latitude.toFixed(6));
    setLongitude(randomLocation.longitude.toFixed(6));
    setLocationLabel(randomLocation.label);
    setMode("random");
  }, []);

  const handleSubmit = useCallback(async () => {
    const parsedLatitude = Number.parseFloat(latitude);
    const parsedLongitude = Number.parseFloat(longitude);

    if (!memberName.trim()) {
      setError("Vui lòng nhập tên thành viên test.");
      return;
    }

    if (Number.isNaN(parsedLatitude) || parsedLatitude < -90 || parsedLatitude > 90) {
      setError("Vĩ độ không hợp lệ.");
      return;
    }

    if (Number.isNaN(parsedLongitude) || parsedLongitude < -180 || parsedLongitude > 180) {
      setError("Kinh độ không hợp lệ.");
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      await api.sessions.join(sessionId, {
        memberName: memberName.trim(),
        latitude: parsedLatitude,
        longitude: parsedLongitude,
        transportMode,
        mobilityRole: transportMode === TransportMode.Walking ? MemberMobilityRole.NeedsPickup : MemberMobilityRole.SelfTravel,
      });

      await onMemberAdded?.();
      setIsOpen(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Không thể thêm thành viên test.");
    } finally {
      setIsSubmitting(false);
    }
  }, [latitude, longitude, memberName, onMemberAdded, sessionId, transportMode]);

  const transportModes = [
    TransportMode.Walking,
    TransportMode.Cycling,
    TransportMode.Motorbike,
    TransportMode.Car,
    TransportMode.Bus,
  ];

  return (
    <>
      <motion.button
        type="button"
        onClick={() => setIsOpen(true)}
        whileHover={{ scale: 1.05 }}
        whileTap={{ scale: 0.95 }}
        className="fixed bottom-6 right-6 z-40 w-14 h-14 rounded-full bg-gradient-to-r from-[#ff1e00] to-[#ff4d33] text-white shadow-2xl shadow-[#ff1e00]/30 flex items-center justify-center"
        aria-label="Thêm thành viên test"
      >
        <svg className="w-7 h-7" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
        </svg>
      </motion.button>

      <AnimatePresence>
        {isOpen && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-50 bg-black/40 backdrop-blur-sm p-4 flex items-center justify-center"
            onClick={(event) => {
              if (event.target === event.currentTarget && !isSubmitting) {
                setIsOpen(false);
              }
            }}
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.92, y: 20 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.92, y: 20 }}
              className="w-full max-w-md bg-white rounded-2xl shadow-2xl overflow-hidden"
            >
              <div className="bg-gradient-to-r from-[#1a1a2e] to-[#2d3250] px-6 py-4 flex items-center justify-between">
                <div>
                  <h3 className="text-lg font-semibold text-white">Thêm thành viên test</h3>
                  <p className="text-sm text-white/75">Tạo nhanh người giả trong khu vực Hà Nội</p>
                </div>
                <button
                  type="button"
                  onClick={() => setIsOpen(false)}
                  disabled={isSubmitting}
                  className="w-8 h-8 rounded-full bg-white/15 text-white flex items-center justify-center"
                >
                  <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>

              <div className="p-6 space-y-4">
                <div>
                  <label className="block text-sm font-medium text-[#1a1a2e] mb-2">Tên thành viên</label>
                  <input
                    type="text"
                    value={memberName}
                    onChange={(event) => setMemberName(event.target.value)}
                    className="w-full px-4 py-3 rounded-xl border-2 border-[#e8f9fd] bg-[#e8f9fd]/30 focus:border-[#ff1e00] focus:outline-none"
                    placeholder="Ví dụ: Test 2"
                    disabled={isSubmitting}
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-[#1a1a2e] mb-2">Chế độ vị trí</label>
                  <div className="grid grid-cols-2 gap-2">
                    <button
                      type="button"
                      onClick={() => {
                        setMode("random");
                        handleRandomize();
                      }}
                      className={`px-4 py-3 rounded-xl text-sm font-medium ${
                        mode === "random"
                          ? "bg-[#ff1e00] text-white"
                          : "bg-[#e8f9fd] text-[#1a1a2e]"
                      }`}
                    >
                      Ngẫu nhiên Hà Nội
                    </button>
                    <button
                      type="button"
                      onClick={() => setMode("manual")}
                      className={`px-4 py-3 rounded-xl text-sm font-medium ${
                        mode === "manual"
                          ? "bg-[#ff1e00] text-white"
                          : "bg-[#e8f9fd] text-[#1a1a2e]"
                      }`}
                    >
                      Nhập tay
                    </button>
                  </div>
                </div>

                {mode === "random" && (
                  <div className="rounded-xl bg-[#e8f9fd] p-4 space-y-2">
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-sm font-medium text-[#1a1a2e]">{locationLabel}</span>
                      <button
                        type="button"
                        onClick={handleRandomize}
                        className="text-sm font-medium text-[#ff1e00]"
                      >
                        Đổi điểm
                      </button>
                    </div>
                    <p className="text-xs text-[#6b7280]">
                      {latitude}, {longitude}
                    </p>
                  </div>
                )}

                {mode === "manual" && (
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-sm font-medium text-[#1a1a2e] mb-2">Vĩ độ</label>
                      <input
                        type="number"
                        value={latitude}
                        onChange={(event) => setLatitude(event.target.value)}
                        className="w-full px-4 py-3 rounded-xl border-2 border-[#e8f9fd] bg-[#e8f9fd]/30 focus:border-[#ff1e00] focus:outline-none"
                        step="0.000001"
                        disabled={isSubmitting}
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-[#1a1a2e] mb-2">Kinh độ</label>
                      <input
                        type="number"
                        value={longitude}
                        onChange={(event) => setLongitude(event.target.value)}
                        className="w-full px-4 py-3 rounded-xl border-2 border-[#e8f9fd] bg-[#e8f9fd]/30 focus:border-[#ff1e00] focus:outline-none"
                        step="0.000001"
                        disabled={isSubmitting}
                      />
                    </div>
                  </div>
                )}

                <div>
                  <label className="block text-sm font-medium text-[#1a1a2e] mb-2">Phương tiện</label>
                  <div className="grid grid-cols-5 gap-2">
                    {transportModes.map((modeValue) => (
                      <button
                        key={modeValue}
                        type="button"
                        onClick={() => setTransportMode(modeValue)}
                        className={`flex flex-col items-center gap-1 p-3 rounded-xl transition-colors ${
                          transportMode === modeValue
                            ? "bg-[#59ce8f] text-white"
                            : "bg-[#e8f9fd] text-[#1a1a2e]"
                        }`}
                      >
                        <span className="text-lg">{transportModeIcons[modeValue]}</span>
                        <span className="text-[10px] font-medium">{transportModeLabels[modeValue]}</span>
                      </button>
                    ))}
                  </div>
                </div>

                {error && (
                  <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-600">
                    {error}
                  </div>
                )}

                <button
                  type="button"
                  onClick={handleSubmit}
                  disabled={isSubmitting}
                  className="w-full py-3.5 rounded-xl font-semibold text-white bg-gradient-to-r from-[#59ce8f] to-[#7dd9a7] disabled:opacity-70 disabled:cursor-not-allowed"
                >
                  {isSubmitting ? "Đang thêm..." : "Thêm thành viên test"}
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  );
}

export const AddTestMemberFab = memo(AddTestMemberFabComponent);
