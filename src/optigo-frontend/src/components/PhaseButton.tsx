"use client";

import { memo, ReactNode } from "react";
import { motion } from "framer-motion";

interface PhaseButtonProps {
  phase: 1 | 2 | 3;
  title: string;
  description: string;
  icon: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  comingSoon?: boolean;
}

function PhaseButtonComponent({
  phase,
  title,
  description,
  icon,
  onClick,
  disabled = false,
  comingSoon = false,
}: PhaseButtonProps) {
  const isDisabled = disabled || comingSoon;

  return (
    <motion.button
      onClick={isDisabled ? undefined : onClick}
      disabled={isDisabled}
      initial={{ opacity: 0, x: 20 }}
      animate={{ opacity: 1, x: 0 }}
      transition={{ duration: 0.4, delay: phase * 0.1 }}
      whileHover={isDisabled ? {} : { scale: 1.02, y: -2 }}
      whileTap={isDisabled ? {} : { scale: 0.98 }}
      className={`
        relative w-full p-5 rounded-2xl text-left transition-all duration-300
        border-2 overflow-hidden group
        ${
          isDisabled
            ? "bg-gray-50 border-gray-200 cursor-not-allowed"
            : "bg-white border-[#e8f9fd] hover:border-[#ff1e00] hover:shadow-lg hover:shadow-[#ff1e00]/10 cursor-pointer"
        }
      `}
    >
      {/* Phase number badge */}
      <div
        className={`
          absolute top-3 right-3 w-8 h-8 rounded-full flex items-center justify-center
          text-sm font-bold transition-colors duration-300
          ${
            isDisabled
              ? "bg-gray-200 text-gray-400"
              : "bg-gradient-to-br from-[#ff1e00] to-[#ff4d33] text-white group-hover:from-[#59ce8f] group-hover:to-[#7dd9a7]"
          }
        `}
      >
        {phase}
      </div>

      {/* Icon */}
      <div
        className={`
          w-12 h-12 rounded-xl flex items-center justify-center mb-3
          transition-all duration-300
          ${
            isDisabled
              ? "bg-gray-100 text-gray-400"
              : "bg-[#e8f9fd] text-[#ff1e00] group-hover:bg-[#ff1e00] group-hover:text-white"
          }
        `}
      >
        {icon}
      </div>

      {/* Title */}
      <h3
        className={`
          text-lg font-semibold mb-1 transition-colors duration-300
          ${isDisabled ? "text-gray-400" : "text-[#1a1a2e]"}
        `}
      >
        {title}
      </h3>

      {/* Description */}
      <p
        className={`
          text-sm transition-colors duration-300
          ${isDisabled ? "text-gray-400" : "text-[#6b7280]"}
        `}
      >
        {description}
      </p>

      {/* Coming Soon badge */}
      {comingSoon && (
        <span className="absolute bottom-3 right-3 px-2 py-1 text-xs font-medium bg-[#e8f9fd] text-[#6b7280] rounded-full">
          Sắp ra mắt
        </span>
      )}

      {/* Hover gradient overlay */}
      {!isDisabled && (
        <div className="absolute inset-0 bg-gradient-to-br from-transparent to-[#ff1e00]/0 group-hover:to-[#ff1e00]/5 transition-all duration-300 pointer-events-none" />
      )}
    </motion.button>
  );
}

export const PhaseButton = memo(PhaseButtonComponent);
