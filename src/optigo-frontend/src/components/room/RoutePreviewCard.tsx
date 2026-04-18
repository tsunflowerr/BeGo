"use client";

import { memo } from "react";
import { Venue, formatDistance, formatDuration } from "@/types";

interface RoutePreviewCardProps {
  venue: Venue;
}

function RoutePreviewCardComponent({ venue }: RoutePreviewCardProps) {
  return (
    <div className="rounded-2xl border border-[#e8f9fd] bg-white p-4 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-base font-semibold text-[#1a1a2e]">Lộ trình đến {venue.name}</h3>
          <p className="mt-1 text-sm text-[#6b7280]">
            Tổng thời gian nhóm: {formatDuration(venue.totalTimeSeconds)} • Tổng đi bộ: {formatDistance(venue.totalWalkingDistanceMeters)}
          </p>
        </div>
        <span className="rounded-full bg-[#59ce8f]/10 px-2.5 py-1 text-[11px] font-medium text-[#2b8a57]">
          Detour max {formatDuration(venue.maxDriverDetourSeconds)}
        </span>
      </div>

      <div className="mt-4 space-y-4">
        {venue.driverRoutes.map((route) => (
          <div key={route.driverId} className="rounded-xl border border-[#e8f9fd] bg-[#f9fcff] p-3">
            <div className="flex items-center justify-between gap-3">
              <div>
                <p className="text-sm font-semibold text-[#1a1a2e]">{route.driverName}</p>
                <p className="mt-1 text-xs text-[#6b7280]">
                  {formatDuration(route.totalTimeSeconds)} • {formatDistance(route.totalDistanceMeters)} • {route.passengerIds.length} khách
                </p>
              </div>
              <span className="rounded-full bg-[#ff1e00]/10 px-2.5 py-1 text-[11px] font-medium text-[#ff1e00]">
                +{formatDuration(Math.max(0, route.totalTimeSeconds - route.directTimeSeconds))}
              </span>
            </div>

            <div className="mt-3 space-y-2">
              {route.stops.map((stop) => (
                <div key={`${route.driverId}-${stop.sequence}`} className="flex items-start gap-3">
                  <div className="mt-0.5 flex h-6 w-6 items-center justify-center rounded-full bg-[#1a1a2e] text-[11px] font-semibold text-white">
                    {stop.sequence}
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-[#1a1a2e]">{stop.label}</p>
                    <p className="text-xs text-[#6b7280]">
                      ETA {formatDuration(stop.etaSeconds)}{stop.walkingDistanceMeters > 0 ? ` • đi bộ ${formatDistance(stop.walkingDistanceMeters)}` : ""}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

export const RoutePreviewCard = memo(RoutePreviewCardComponent);
