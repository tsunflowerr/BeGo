"use client";

import { memo } from "react";

interface LogoProps {
  size?: number;
  className?: string;
}

function LogoComponent({ size = 40, className = "" }: LogoProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 100 100"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      aria-label="OptiGo Logo"
    >
      {/* Background circle */}
      <circle cx="50" cy="50" r="45" fill="#5D7B6F" />
      
      {/* Inner design - stylized location pin with routes */}
      <path
        d="M50 20C38.954 20 30 28.954 30 40C30 54 50 75 50 75C50 75 70 54 70 40C70 28.954 61.046 20 50 20Z"
        fill="#D7F9FA"
      />
      
      {/* Center dot */}
      <circle cx="50" cy="40" r="8" fill="#5D7B6F" />
      
      {/* Route lines representing connections */}
      <path
        d="M25 60L40 50"
        stroke="#A4C3A2"
        strokeWidth="3"
        strokeLinecap="round"
      />
      <path
        d="M75 60L60 50"
        stroke="#A4C3A2"
        strokeWidth="3"
        strokeLinecap="round"
      />
      <path
        d="M50 80L50 72"
        stroke="#A4C3A2"
        strokeWidth="3"
        strokeLinecap="round"
      />
      
      {/* Small dots representing people */}
      <circle cx="22" cy="62" r="4" fill="#B0D4B8" />
      <circle cx="78" cy="62" r="4" fill="#B0D4B8" />
      <circle cx="50" cy="85" r="4" fill="#B0D4B8" />
    </svg>
  );
}

export const Logo = memo(LogoComponent);
