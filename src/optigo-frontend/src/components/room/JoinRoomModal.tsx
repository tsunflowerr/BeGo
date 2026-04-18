"use client";

import { memo, useState, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";
import * as yup from "yup";
import {
  MemberMobilityRole,
  TransportMode,
  transportModeLabels,
  transportModeIcons,
} from "@/types";

interface JoinRoomModalProps {
  isOpen: boolean;
  sessionId: string;
  onClose: () => void;
  onJoin: (data: {
    memberName: string;
    latitude: number;
    longitude: number;
    transportMode: TransportMode;
    mobilityRole: MemberMobilityRole;
  }) => Promise<void>;
  initialLocation?: { latitude: number; longitude: number } | null;
  locationError?: string | null;
  locationLoading?: boolean;
}

const schema = yup.object().shape({
  memberName: yup
    .string()
    .trim()
    .required("Vui lòng nhập tên của bạn")
    .min(2, "Tên phải có ít nhất 2 ký tự")
    .max(50, "Tên không được vượt quá 50 ký tự")
    .matches(/^[\p{L}\p{N}\s\-_\.]+$/u, "Tên chỉ được chứa chữ cái, số, dấu cách, gạch ngang, gạch dưới và dấu chấm"),
  latitude: yup.number().required("Không tìm thấy vĩ độ").min(-90).max(90),
  longitude: yup.number().required("Không tìm thấy kinh độ").min(-180).max(180),
  transportMode: yup.mixed<TransportMode>().oneOf(Object.values(TransportMode) as TransportMode[]).required(),
});

function JoinRoomModalComponent({
  isOpen,
  sessionId,
  onClose,
  onJoin,
  initialLocation,
  locationError,
  locationLoading,
}: JoinRoomModalProps) {
  const [memberName, setMemberName] = useState("");
  const [mobilityRole, setMobilityRole] = useState<MemberMobilityRole>(MemberMobilityRole.NeedsPickup);
  const [transportMode, setTransportMode] = useState<TransportMode>(TransportMode.Motorbike);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();

    if (!initialLocation) {
      setError("Không thể lấy vị trí của bạn. Vui lòng cho phép truy cập vị trí.");
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const data = await schema.validate({
        memberName,
        latitude: initialLocation.latitude,
        longitude: initialLocation.longitude,
        transportMode: mobilityRole === MemberMobilityRole.NeedsPickup ? TransportMode.Walking : transportMode,
      });

      await onJoin({
        memberName: data.memberName!,
        latitude: data.latitude!,
        longitude: data.longitude!,
        transportMode: data.transportMode as TransportMode,
        mobilityRole,
      });
    } catch (err) {
      if (err instanceof yup.ValidationError) {
        setError(err.message);
      } else {
        setError(err instanceof Error ? err.message : "Không thể tham gia phòng");
      }
    } finally {
      setIsSubmitting(false);
    }
  }, [memberName, transportMode, initialLocation, mobilityRole, onJoin]);

  const transportModes = [
    TransportMode.Walking,
    TransportMode.Cycling,
    TransportMode.Motorbike,
    TransportMode.Car,
    TransportMode.Bus,
  ];

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50 backdrop-blur-sm"
        >
          <motion.div
            initial={{ scale: 0.9, opacity: 0, y: 20 }}
            animate={{ scale: 1, opacity: 1, y: 0 }}
            exit={{ scale: 0.9, opacity: 0, y: 20 }}
            transition={{ type: "spring", damping: 25, stiffness: 300 }}
            className="w-full max-w-md bg-white rounded-2xl shadow-2xl overflow-hidden"
          >
            {/* Header */}
            <div className="bg-gradient-to-r from-[#59ce8f] to-[#7dd9a7] px-6 py-4">
              <h2 className="text-xl font-semibold text-white">Tham gia phòng</h2>
              <p className="text-sm text-white/80 mt-1">Nhập thông tin để tham gia nhóm</p>
            </div>

            {/* Form */}
            <form onSubmit={handleSubmit} className="p-6 space-y-5">
              {/* Member Name */}
              <div>
                <label htmlFor="memberName" className="block text-sm font-medium text-[#1a1a2e] mb-2">
                  Tên của bạn <span className="text-[#ff1e00]">*</span>
                </label>
                <input
                  type="text"
                  id="memberName"
                  value={memberName}
                  onChange={(e) => setMemberName(e.target.value)}
                  placeholder="Nhập tên để bạn bè nhận ra bạn"
                  disabled={isSubmitting}
                  className="w-full px-4 py-3 rounded-xl border-2 border-[#e8f9fd] bg-[#e8f9fd]/30 text-[#1a1a2e] placeholder:text-[#6b7280] focus:border-[#59ce8f] focus:outline-none focus:ring-2 focus:ring-[#59ce8f]/20 transition-all disabled:opacity-50"
                  autoFocus
                />
              </div>

              {/* Location status */}
              <div>
                <label className="block text-sm font-medium text-[#1a1a2e] mb-2">
                  Vị trí của bạn
                </label>
                <div className={`p-4 rounded-xl ${
                  locationError 
                    ? "bg-red-50 border border-red-200" 
                    : initialLocation 
                      ? "bg-[#59ce8f]/10 border border-[#59ce8f]/30" 
                      : "bg-[#e8f9fd] border border-[#e8f9fd]"
                }`}>
                  {locationLoading ? (
                    <div className="flex items-center gap-2">
                      <svg className="animate-spin w-5 h-5 text-[#59ce8f]" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                      </svg>
                      <span className="text-sm text-[#6b7280]">Đang lấy vị trí...</span>
                    </div>
                  ) : locationError ? (
                    <div className="flex items-center gap-2 text-red-600">
                      <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                      </svg>
                      <span className="text-sm">{locationError}</span>
                    </div>
                  ) : initialLocation ? (
                    <div className="flex items-center gap-2 text-[#59ce8f]">
                      <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                      </svg>
                      <span className="text-sm">
                        Đã xác định vị trí: {initialLocation.latitude.toFixed(4)}, {initialLocation.longitude.toFixed(4)}
                      </span>
                    </div>
                  ) : (
                    <span className="text-sm text-[#6b7280]">Vui lòng cho phép truy cập vị trí</span>
                  )}
                </div>
              </div>

              {/* Transport Mode */}
              <div>
                <label className="block text-sm font-medium text-[#1a1a2e] mb-2">
                  Cách tham gia chuyến đi
                </label>
                <div className="grid grid-cols-2 gap-3">
                  {[
                    { role: MemberMobilityRole.NeedsPickup, title: "Cần được đón", description: "Gửi yêu cầu đón để tài xế nhận" },
                    { role: MemberMobilityRole.SelfTravel, title: "Tự di chuyển", description: "Bạn tự lái hoặc tự đi tới điểm hẹn" },
                  ].map((option) => (
                    <button
                      key={option.role}
                      type="button"
                      onClick={() => setMobilityRole(option.role)}
                      className={`rounded-xl border p-3 text-left transition-colors ${
                        mobilityRole === option.role
                          ? "border-[#ff1e00] bg-[#ff1e00]/5"
                          : "border-[#e8f9fd] bg-[#e8f9fd]/30 hover:border-[#ff1e00]/30"
                      }`}
                    >
                      <p className="text-sm font-semibold text-[#1a1a2e]">{option.title}</p>
                      <p className="mt-1 text-xs text-[#6b7280]">{option.description}</p>
                    </button>
                  ))}
                </div>
              </div>

              {mobilityRole === MemberMobilityRole.SelfTravel && (
                <div>
                  <label className="block text-sm font-medium text-[#1a1a2e] mb-2">
                    Phương tiện của bạn
                  </label>
                  <div className="grid grid-cols-5 gap-2">
                    {transportModes.map((mode) => (
                      <motion.button
                        key={mode}
                        type="button"
                        onClick={() => setTransportMode(mode)}
                        whileHover={{ scale: 1.05 }}
                        whileTap={{ scale: 0.95 }}
                        className={`flex flex-col items-center gap-1 p-3 rounded-xl transition-colors ${
                          transportMode === mode
                            ? "bg-[#ff1e00] text-white"
                            : "bg-[#e8f9fd] text-[#1a1a2e] hover:bg-[#ff1e00]/10"
                        }`}
                      >
                        <span className="text-xl">{transportModeIcons[mode]}</span>
                        <span className="text-[10px] font-medium">{transportModeLabels[mode]}</span>
                      </motion.button>
                    ))}
                  </div>
                </div>
              )}

              {/* Error message */}
              <AnimatePresence>
                {error && (
                  <motion.div
                    initial={{ opacity: 0, y: -10 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, y: -10 }}
                    className="p-3 rounded-lg bg-red-50 border border-red-200"
                  >
                    <p className="text-sm text-red-600">{error}</p>
                  </motion.div>
                )}
              </AnimatePresence>

              {/* Submit Button */}
              <motion.button
                type="submit"
                disabled={isSubmitting || !initialLocation}
                whileHover={isSubmitting ? {} : { scale: 1.02 }}
                whileTap={isSubmitting ? {} : { scale: 0.98 }}
                className="w-full py-3.5 rounded-xl font-semibold text-white bg-gradient-to-r from-[#59ce8f] to-[#7dd9a7] hover:from-[#45a572] hover:to-[#59ce8f] transition-all shadow-lg shadow-[#59ce8f]/30 disabled:opacity-70 disabled:cursor-not-allowed flex items-center justify-center gap-2"
              >
                {isSubmitting ? (
                  <>
                    <svg className="animate-spin w-5 h-5" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                    </svg>
                    <span>Đang tham gia...</span>
                  </>
                ) : (
                  <>
                    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11 16l-4-4m0 0l4-4m-4 4h14m-5 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h7a3 3 0 013 3v1" />
                    </svg>
                    <span>Tham gia phòng</span>
                  </>
                )}
              </motion.button>
            </form>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

export const JoinRoomModal = memo(JoinRoomModalComponent);
