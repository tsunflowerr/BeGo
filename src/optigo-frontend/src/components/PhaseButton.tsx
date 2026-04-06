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
            ? "bg-[#EAE7D6]/50 border-[#B0D4B8]/30 cursor-not-allowed"
            : "bg-white/90 border-[#B0D4B8] hover:border-[#5D7B6F] hover:shadow-lg hover:shadow-[#B0D4B8]/30 cursor-pointer"
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
              ? "bg-[#B0D4B8]/30 text-[#5D7B6F]/50"
              : "bg-[#5D7B6F] text-white group-hover:bg-[#A4C3A2]"
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
              ? "bg-[#B0D4B8]/20 text-[#5D7B6F]/40"
              : "bg-[#D7F9FA] text-[#5D7B6F] group-hover:bg-[#B0D4B8] group-hover:text-white"
          }
        `}
      >
        {icon}
      </div>

      {/* Title */}
      <h3
        className={`
          text-lg font-semibold mb-1 transition-colors duration-300
          ${isDisabled ? "text-[#5D7B6F]/50" : "text-[#5D7B6F]"}
        `}
      >
        {title}
      </h3>

      {/* Description */}
      <p
        className={`
          text-sm transition-colors duration-300
          ${isDisabled ? "text-[#5D7B6F]/40" : "text-[#5D7B6F]/70"}
        `}
      >
        {description}
      </p>

      {/* Coming Soon badge */}
      {comingSoon && (
        <span className="absolute bottom-3 right-3 px-2 py-1 text-xs font-medium bg-[#B0D4B8]/30 text-[#5D7B6F]/60 rounded-full">
          Sắp ra mắt
        </span>
      )}

      {/* Hover gradient overlay */}
      {!isDisabled && (
        <div className="absolute inset-0 bg-gradient-to-br from-[#D7F9FA]/0 to-[#B0D4B8]/0 group-hover:from-[#D7F9FA]/10 group-hover:to-[#B0D4B8]/20 transition-all duration-300 pointer-events-none" />
      )}
    </motion.button>
  );
}

export const PhaseButton = memo(PhaseButtonComponent);
