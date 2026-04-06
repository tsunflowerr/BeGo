"use client";

import { memo } from "react";
import { motion } from "framer-motion";
import { Logo } from "./Logo";

function HeroSectionComponent() {
  const features = [
    {
      icon: (
        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 11a3 3 0 11-6 0 3 3 0 016 0z" />
        </svg>
      ),
      text: "Tìm điểm hẹn tối ưu cho nhóm",
    },
    {
      icon: (
        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 7h12m0 0l-4-4m4 4l-4 4m0 6H4m0 0l4 4m-4-4l4-4" />
        </svg>
      ),
      text: "Tối ưu hóa di chuyển chung",
    },
    {
      icon: (
        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-3 7h3m-3 4h3m-6-4h.01M9 16h.01" />
        </svg>
      ),
      text: "Lập lịch trình nhóm thông minh",
    },
    {
      icon: (
        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
        </svg>
      ),
      text: "Hỗ trợ AI thời gian thực",
    },
  ];

  return (
    <motion.div
      initial={{ opacity: 0, x: -30 }}
      animate={{ opacity: 1, x: 0 }}
      transition={{ duration: 0.5, ease: "easeOut" }}
      className="flex-1 flex flex-col justify-center p-8 lg:p-12"
    >
      {/* Illustration */}
      <div className="relative mb-8">
        <motion.div
          animate={{
            y: [0, -10, 0],
          }}
          transition={{
            duration: 4,
            repeat: Infinity,
            ease: "easeInOut",
          }}
          className="flex justify-center"
        >
          <div className="relative">
            <Logo size={160} className="drop-shadow-2xl" />
            
            {/* Animated rings */}
            <motion.div
              animate={{ scale: [1, 1.4, 1], opacity: [0.5, 0, 0.5] }}
              transition={{ duration: 2, repeat: Infinity }}
              className="absolute inset-0 border-2 border-[#A4C3A2] rounded-full"
            />
            <motion.div
              animate={{ scale: [1, 1.6, 1], opacity: [0.3, 0, 0.3] }}
              transition={{ duration: 2, repeat: Infinity, delay: 0.5 }}
              className="absolute inset-0 border border-[#B0D4B8] rounded-full"
            />
          </div>
        </motion.div>
      </div>

      {/* Title and Description */}
      <div className="text-center lg:text-left">
        <motion.h1
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.2 }}
          className="text-3xl lg:text-4xl font-bold text-[#5D7B6F] mb-4"
        >
          Gặp nhau dễ dàng hơn bao giờ hết
        </motion.h1>
        
        <motion.p
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.3 }}
          className="text-lg text-[#5D7B6F]/70 mb-8 max-w-lg mx-auto lg:mx-0"
        >
          OptiGo giúp nhóm của bạn tìm điểm hẹn tối ưu, lên kế hoạch di chuyển 
          và xây dựng lịch trình hoàn hảo chỉ với vài thao tác đơn giản.
        </motion.p>

        {/* Feature list */}
        <motion.ul
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ delay: 0.4 }}
          className="space-y-3"
        >
          {features.map((feature, index) => (
            <motion.li
              key={index}
              initial={{ opacity: 0, x: -20 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: 0.4 + index * 0.1 }}
              className="flex items-center gap-3 text-[#5D7B6F]"
            >
              <span className="flex-shrink-0 w-8 h-8 bg-[#B0D4B8]/30 rounded-lg flex items-center justify-center text-[#5D7B6F]">
                {feature.icon}
              </span>
              <span className="text-sm lg:text-base">{feature.text}</span>
            </motion.li>
          ))}
        </motion.ul>
      </div>
    </motion.div>
  );
}

export const HeroSection = memo(HeroSectionComponent);
