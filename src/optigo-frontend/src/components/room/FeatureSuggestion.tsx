"use client";

import { memo } from "react";
import { motion } from "framer-motion";

interface FeatureSuggestionProps {
  onExplore?: (feature: string) => void;
}

const features = [
  {
    id: "carpooling",
    title: "Đi Chung Xe",
    description: "Tối ưu hóa việc di chuyển chung, tiết kiệm chi phí và thân thiện môi trường",
    icon: "🚗",
    color: "from-blue-500 to-blue-600",
    comingSoon: true,
  },
  {
    id: "scheduling",
    title: "Lập Lịch Trình",
    description: "Xây dựng kế hoạch hoạt động cho cả nhóm với AI hỗ trợ",
    icon: "📅",
    color: "from-purple-500 to-purple-600",
    comingSoon: true,
  },
];

function FeatureSuggestionComponent({ onExplore }: FeatureSuggestionProps) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      className="mt-6 p-4 bg-[#e8f9fd]/50 rounded-2xl"
    >
      <div className="flex items-center gap-2 mb-4">
        <span className="text-xl">✨</span>
        <h3 className="font-semibold text-[#1a1a2e]">Khám phá thêm</h3>
      </div>
      
      <p className="text-sm text-[#6b7280] mb-4">
        Bạn có muốn sử dụng các tính năng khác của OptiGo không?
      </p>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        {features.map((feature) => (
          <motion.button
            key={feature.id}
            onClick={() => onExplore?.(feature.id)}
            whileHover={{ scale: 1.02 }}
            whileTap={{ scale: 0.98 }}
            className="relative p-4 bg-white rounded-xl text-left shadow-sm hover:shadow-md transition-all group overflow-hidden"
            disabled={feature.comingSoon}
          >
            {/* Background gradient on hover */}
            <div className={`absolute inset-0 bg-gradient-to-br ${feature.color} opacity-0 group-hover:opacity-5 transition-opacity`} />
            
            <div className="flex items-start gap-3">
              <span className="text-2xl">{feature.icon}</span>
              <div>
                <div className="flex items-center gap-2">
                  <h4 className="font-medium text-[#1a1a2e]">{feature.title}</h4>
                  {feature.comingSoon && (
                    <span className="text-[10px] px-1.5 py-0.5 bg-[#e8f9fd] text-[#6b7280] rounded-full">
                      Sắp ra mắt
                    </span>
                  )}
                </div>
                <p className="text-xs text-[#6b7280] mt-0.5 line-clamp-2">
                  {feature.description}
                </p>
              </div>
            </div>
          </motion.button>
        ))}
      </div>
    </motion.div>
  );
}

export const FeatureSuggestion = memo(FeatureSuggestionComponent);
