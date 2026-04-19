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

      <div className="mt-3 grid grid-cols-1 gap-2 md:grid-cols-3">
        <div className="rounded-xl bg-[#f9fcff] px-3 py-2">
          <p className="text-[11px] uppercase tracking-wide text-[#6b7280]">Generalized Cost</p>
          <p className="mt-1 text-sm font-semibold text-[#1a1a2e]">
            {formatDuration(venue.scoreBreakdown.generalizedCostSeconds)}
          </p>
        </div>
        <div className="rounded-xl bg-[#f9fcff] px-3 py-2">
          <p className="text-[11px] uppercase tracking-wide text-[#6b7280]">Wait + Walk</p>
          <p className="mt-1 text-sm font-semibold text-[#1a1a2e]">
            {formatDuration(venue.scoreBreakdown.totalWaitSeconds)} • {formatDistance(venue.totalWalkingDistanceMeters)}
          </p>
        </div>
        <div className="rounded-xl bg-[#f9fcff] px-3 py-2">
          <p className="text-[11px] uppercase tracking-wide text-[#6b7280]">API / Cache</p>
          <p className="mt-1 text-sm font-semibold text-[#1a1a2e]">
            {venue.apiCostEstimate.toFixed(1)} • {(venue.cacheHitRatio * 100).toFixed(0)}%
          </p>
        </div>
      </div>

      {venue.benchmarkComparison && (
        <div className="mt-3 rounded-xl border border-[#d7f0df] bg-[#f4fbf6] px-3 py-3 text-sm text-[#1a1a2e]">
          Hybrid {venue.benchmarkComparison.improvementPercent >= 0 ? "giảm" : "tăng"}{" "}
          {Math.abs(venue.benchmarkComparison.improvementPercent).toFixed(1)}% generalized cost so với baseline heuristic.
        </div>
      )}

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
            <p className="mt-2 text-xs text-[#6b7280]">
              Generalized cost {formatDuration(route.generalizedCostSeconds)}
            </p>

            <div className="mt-3 space-y-2">
              {route.stops.map((stop) => (
                <div key={`${route.driverId}-${stop.sequence}`} className="flex items-start gap-3">
                  <div className="mt-0.5 flex h-6 w-6 items-center justify-center rounded-full bg-[#1a1a2e] text-[11px] font-semibold text-white">
                    {stop.sequence}
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-[#1a1a2e]">{stop.label}</p>
                    <p className="text-xs text-[#6b7280]">
                      ETA {formatDuration(stop.etaSeconds)}
                      {stop.walkingDistanceMeters > 0 ? ` • đi bộ ${formatDistance(stop.walkingDistanceMeters)}` : ""}
                      {stop.waitSeconds > 0 ? ` • chờ ${formatDuration(stop.waitSeconds)}` : ""}
                      {stop.isMergedStop ? " • điểm đón chung" : ""}
                    </p>
                    <p className="text-[11px] text-[#9ca3af]">
                      {stop.stopAccessType} • cumulative {formatDistance(stop.cumulativeDistanceMeters)}
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
