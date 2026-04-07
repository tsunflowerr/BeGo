"use client";

import { memo } from "react";
import { motion } from "framer-motion";
import { Logo } from "./Logo";

interface NavbarProps {
  user?: {
    name: string;
    avatar?: string;
  };
  onSignOut?: () => void;
}

function NavbarComponent({ user, onSignOut }: NavbarProps) {
  const currentUser = user || {
    name: "Người dùng",
    avatar: undefined,
  };

  return (
    <motion.nav
      initial={{ y: -20, opacity: 0 }}
      animate={{ y: 0, opacity: 1 }}
      transition={{ duration: 0.4, ease: "easeOut" }}
      className="sticky top-0 z-50 w-full bg-white/95 backdrop-blur-sm border-b border-[#e8f9fd] shadow-sm"
    >
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between h-16">
          {/* Left side - Logo and Name */}
          <motion.div
            className="flex items-center gap-3 cursor-pointer"
            whileHover={{ scale: 1.02 }}
            whileTap={{ scale: 0.98 }}
          >
            <Logo size={40} />
            <span className="text-xl font-bold text-[#1a1a2e] tracking-tight">
              OptiGo
            </span>
          </motion.div>

          {/* Right side - User Avatar and Sign Out */}
          <div className="flex items-center gap-4">
            <div className="flex items-center gap-3">
              <div className="relative">
                {currentUser.avatar ? (
                  <img
                    src={currentUser.avatar}
                    alt={currentUser.name}
                    className="w-9 h-9 rounded-full border-2 border-[#59ce8f] object-cover"
                  />
                ) : (
                  <div className="w-9 h-9 rounded-full bg-gradient-to-br from-[#ff1e00] to-[#ff4d33] flex items-center justify-center text-white font-medium text-sm shadow-md">
                    {currentUser.name.charAt(0).toUpperCase()}
                  </div>
                )}
                <span className="absolute bottom-0 right-0 w-2.5 h-2.5 bg-[#59ce8f] border-2 border-white rounded-full" />
              </div>

              <span className="hidden sm:block text-sm font-medium text-[#1a1a2e]">
                {currentUser.name}
              </span>
            </div>

            <motion.button
              onClick={onSignOut}
              whileHover={{ scale: 1.05 }}
              whileTap={{ scale: 0.95 }}
              className="px-4 py-2 text-sm font-medium text-[#ff1e00] bg-white border border-[#ff1e00]/30 rounded-lg hover:bg-[#ff1e00]/5 hover:border-[#ff1e00] transition-colors duration-200"
            >
              Đăng xuất
            </motion.button>
          </div>
        </div>
      </div>
    </motion.nav>
  );
}

export const Navbar = memo(NavbarComponent);
