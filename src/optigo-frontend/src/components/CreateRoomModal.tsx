"use client";

import { memo, useState, useCallback, useEffect, useRef } from "react";
import { motion, AnimatePresence } from "framer-motion";
import * as yup from "yup";
import { MemberMobilityRole, TransportMode } from "@/types";

interface CreateRoomModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (data: {
    hostName: string;
    defaultQuery: string;
    latitude: number;
    longitude: number;
    transportMode: number;
    mobilityRole: MemberMobilityRole;
  }) => Promise<void>;
  initialLocation?: {
    latitude: number;
    longitude: number;
  } | null;
  locationError?: string | null;
  locationLoading?: boolean;
  onRequestLocation?: () => void;
}

const schema = yup.object().shape({
  hostName: yup
    .string()
    .trim()
    .required("Vui lòng nhập tên của bạn")
    .max(100, "Tên không được vượt quá 100 ký tự")
    .matches(/^[\p{L}\p{N}\s\-_\.]+$/u, "Tên chỉ được chứa chữ cái, số, dấu cách, gạch ngang, gạch dưới và dấu chấm"),
  defaultQuery: yup
    .string()
    .trim()
    .max(500, "Mô tả không được vượt quá 500 ký tự"),
});

function CreateRoomModalComponent({
  isOpen,
  onClose,
  onSubmit,
  initialLocation,
  locationError,
  locationLoading,
  onRequestLocation,
}: CreateRoomModalProps) {
  const [hostName, setHostName] = useState("");
  const [defaultQuery, setDefaultQuery] = useState("");
  const [mobilityRole, setMobilityRole] = useState<MemberMobilityRole>(MemberMobilityRole.SelfTravel);
  const [transportMode, setTransportMode] = useState(TransportMode.Motorbike);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Focus input when modal opens
  useEffect(() => {
    if (isOpen && inputRef.current) {
      setTimeout(() => inputRef.current?.focus(), 100);
    }
  }, [isOpen]);

  // Reset form when modal closes
  useEffect(() => {
    if (!isOpen) {
      setHostName("");
      setDefaultQuery("");
      setMobilityRole(MemberMobilityRole.SelfTravel);
      setTransportMode(TransportMode.Motorbike);
      setError(null);
    }
  }, [isOpen]);

  const handleSubmit = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    
    setIsLoading(true);
    setError(null);

    try {
      const data = await schema.validate({ hostName, defaultQuery });
      if (!initialLocation) {
        throw new Error("Cần chia sẻ vị trí trước khi tạo phòng");
      }

      await onSubmit({
        hostName: data.hostName!,
        defaultQuery: data.defaultQuery || "",
        latitude: initialLocation.latitude,
        longitude: initialLocation.longitude,
        transportMode: mobilityRole === MemberMobilityRole.NeedsPickup ? TransportMode.Walking : transportMode,
        mobilityRole,
      });
    } catch (err) {
      if (err instanceof yup.ValidationError) {
        setError(err.message);
      } else {
        setError(err instanceof Error ? err.message : "Có lỗi xảy ra, vui lòng thử lại");
      }
    } finally {
      setIsLoading(false);
    }
  }, [hostName, defaultQuery, onSubmit, initialLocation, mobilityRole, transportMode]);

  const handleBackdropClick = useCallback((e: React.MouseEvent) => {
    if (e.target === e.currentTarget && !isLoading) {
      onClose();
    }
  }, [onClose, isLoading]);

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.2 }}
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40 backdrop-blur-sm"
          onClick={handleBackdropClick}
        >
          <motion.div
            initial={{ scale: 0.9, opacity: 0, y: 20 }}
            animate={{ scale: 1, opacity: 1, y: 0 }}
            exit={{ scale: 0.9, opacity: 0, y: 20 }}
            transition={{ type: "spring", damping: 25, stiffness: 300 }}
            className="w-full max-w-md bg-white rounded-2xl shadow-2xl overflow-hidden"
          >
            {/* Header */}
            <div className="bg-gradient-to-r from-[#ff1e00] to-[#ff4d33] px-6 py-4">
              <div className="flex items-center justify-between">
                <div>
                  <h2 className="text-xl font-semibold text-white">Tạo phòng mới</h2>
                  <p className="text-sm text-white/80 mt-1">Phase 1: Tìm điểm hẹn tối ưu</p>
                </div>
                <button
                  onClick={onClose}
                  disabled={isLoading}
                  className="w-8 h-8 flex items-center justify-center rounded-full bg-white/20 hover:bg-white/30 transition-colors disabled:opacity-50"
                >
                  <svg className="w-5 h-5 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>
            </div>

            {/* Form */}
            <form onSubmit={handleSubmit} className="p-6 space-y-5">
              {/* Host Name */}
              <div>
                <label htmlFor="hostName" className="block text-sm font-medium text-[#1a1a2e] mb-2">
                  Tên của bạn <span className="text-[#ff1e00]">*</span>
                </label>
                <input
                  ref={inputRef}
                  type="text"
                  id="hostName"
                  value={hostName}
                  onChange={(e) => setHostName(e.target.value)}
                  placeholder="Nhập tên để bạn bè nhận ra bạn"
                  disabled={isLoading}
                  className="w-full px-4 py-3 rounded-xl border-2 border-[#e8f9fd] bg-[#e8f9fd]/30 text-[#1a1a2e] placeholder:text-[#6b7280] focus:border-[#ff1e00] focus:outline-none focus:ring-2 focus:ring-[#ff1e00]/20 transition-all disabled:opacity-50"
                />
              </div>

              {/* Default Query */}
              <div>
                <label htmlFor="defaultQuery" className="block text-sm font-medium text-[#1a1a2e] mb-2">
                  Mô tả nơi muốn đến <span className="text-[#6b7280]">(tùy chọn)</span>
                </label>
                <textarea
                  id="defaultQuery"
                  value={defaultQuery}
                  onChange={(e) => setDefaultQuery(e.target.value)}
                  placeholder="Ví dụ: quán cà phê yên tĩnh có wifi, giá sinh viên..."
                  rows={3}
                  disabled={isLoading}
                  className="w-full px-4 py-3 rounded-xl border-2 border-[#e8f9fd] bg-[#e8f9fd]/30 text-[#1a1a2e] placeholder:text-[#6b7280] focus:border-[#ff1e00] focus:outline-none focus:ring-2 focus:ring-[#ff1e00]/20 transition-all resize-none disabled:opacity-50"
                />
                <p className="mt-1.5 text-xs text-[#6b7280]">
                  AI sẽ giúp tìm địa điểm phù hợp dựa trên mô tả của bạn
                </p>
              </div>

              <div>
                <label className="block text-sm font-medium text-[#1a1a2e] mb-2">
                  Vai trò di chuyển
                </label>
                <div className="grid grid-cols-2 gap-3">
                  {[
                    { role: MemberMobilityRole.SelfTravel, title: "Tự di chuyển", description: "Bạn có thể tự đi và nhận đón thêm nếu có xe" },
                    { role: MemberMobilityRole.NeedsPickup, title: "Cần được đón", description: "Tạo phòng nhưng vẫn chờ người khác nhận đón" },
                  ].map((option) => (
                    <button
                      key={option.role}
                      type="button"
                      onClick={() => setMobilityRole(option.role)}
                      disabled={isLoading}
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
                  <label htmlFor="transportMode" className="block text-sm font-medium text-[#1a1a2e] mb-2">
                    Phương tiện di chuyển
                  </label>
                  <select
                    id="transportMode"
                    value={transportMode}
                    onChange={(e) => setTransportMode(Number(e.target.value) as TransportMode)}
                    disabled={isLoading}
                    className="w-full px-4 py-3 rounded-xl border-2 border-[#e8f9fd] bg-[#e8f9fd]/30 text-[#1a1a2e] focus:border-[#ff1e00] focus:outline-none focus:ring-2 focus:ring-[#ff1e00]/20 transition-all disabled:opacity-50"
                  >
                    <option value={TransportMode.Walking}>Đi bộ</option>
                    <option value={TransportMode.Cycling}>Xe đạp</option>
                    <option value={TransportMode.Motorbike}>Xe máy</option>
                    <option value={TransportMode.Car}>Ô tô</option>
                    <option value={TransportMode.Bus}>Xe buýt</option>
                  </select>
                </div>
              )}

              <div className="rounded-xl border border-[#e8f9fd] bg-[#e8f9fd]/30 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <p className="text-sm font-medium text-[#1a1a2e]">Vị trí hiện tại</p>
                    <p className="text-xs text-[#6b7280] mt-1">
                      {initialLocation
                        ? `${initialLocation.latitude.toFixed(5)}, ${initialLocation.longitude.toFixed(5)}`
                        : locationLoading
                          ? "Đang lấy vị trí..."
                          : locationError || "Chưa có vị trí"}
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={onRequestLocation}
                    disabled={isLoading || locationLoading}
                    className="px-3 py-2 rounded-lg text-sm font-medium text-[#ff1e00] bg-white border border-[#ffd7d1] hover:bg-[#fff5f3] transition-colors disabled:opacity-50"
                  >
                    Lấy vị trí
                  </button>
                </div>
              </div>

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
                disabled={isLoading}
                whileHover={isLoading ? {} : { scale: 1.02 }}
                whileTap={isLoading ? {} : { scale: 0.98 }}
                className="w-full py-3.5 rounded-xl font-semibold text-white bg-gradient-to-r from-[#ff1e00] to-[#ff4d33] hover:from-[#cc1800] hover:to-[#ff1e00] transition-all shadow-lg shadow-[#ff1e00]/30 disabled:opacity-70 disabled:cursor-not-allowed flex items-center justify-center gap-2"
              >
                {isLoading ? (
                  <>
                    <svg className="animate-spin w-5 h-5" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                    </svg>
                    <span>Đang tạo phòng...</span>
                  </>
                ) : (
                  <>
                    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
                    </svg>
                    <span>Tạo phòng</span>
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

export const CreateRoomModal = memo(CreateRoomModalComponent);
