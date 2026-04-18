"use client";

import { memo } from "react";
import { Member, PickupRequest, PickupRequestStatus } from "@/types";

interface PickupRequestPanelProps {
  members: Member[];
  pickupRequests: PickupRequest[];
  currentMemberId?: string;
  onAccept: (requestId: string, driverId: string) => Promise<void>;
  onRelease: (requestId: string) => Promise<void>;
}

function PickupRequestPanelComponent({
  members,
  pickupRequests,
  currentMemberId,
  onAccept,
  onRelease,
}: PickupRequestPanelProps) {
  const availableDrivers = members.filter((member) => member.canOfferPickup && (member.availableSeatCount || 0) > 0);
  const currentDriver = availableDrivers.find((member) => member.id === currentMemberId);

  if (pickupRequests.length === 0) {
    return (
      <div className="rounded-2xl border border-[#e8f9fd] bg-[#e8f9fd]/30 p-4">
        <h3 className="text-sm font-semibold text-[#1a1a2e]">Yêu cầu đón</h3>
        <p className="mt-2 text-sm text-[#6b7280]">Hiện chưa có ai cần được đón.</p>
      </div>
    );
  }

  return (
    <div className="rounded-2xl border border-[#e8f9fd] bg-white p-4 shadow-sm">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-[#1a1a2e]">Yêu cầu đón</h3>
        {currentDriver && (
          <span className="rounded-full bg-[#ff1e00]/10 px-2.5 py-1 text-[11px] font-medium text-[#ff1e00]">
            Còn {currentDriver.availableSeatCount ?? 0} chỗ
          </span>
        )}
      </div>

      <div className="mt-3 space-y-3">
        {pickupRequests.map((request) => {
          const isAcceptedByCurrentDriver = request.acceptedDriverId === currentMemberId;
          const isPending = request.status === PickupRequestStatus.Pending;

          return (
            <div key={request.requestId} className="rounded-xl border border-[#e8f9fd] bg-[#f9fcff] p-3">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-sm font-semibold text-[#1a1a2e]">{request.passengerName}</p>
                  <p className="mt-1 text-xs text-[#6b7280]">
                    {request.status === PickupRequestStatus.Pending && "Đang chờ tài xế nhận"}
                    {request.status === PickupRequestStatus.Accepted && `Đã được ${request.acceptedDriverName || "một tài xế"} nhận đón`}
                    {request.status === PickupRequestStatus.Cancelled && "Yêu cầu đã hủy"}
                  </p>
                </div>

                {isPending && currentDriver && currentDriver.id !== request.passengerId && (
                  <button
                    type="button"
                    onClick={() => void onAccept(request.requestId, currentDriver.id)}
                    className="rounded-lg bg-[#ff1e00] px-3 py-1.5 text-xs font-semibold text-white hover:bg-[#cc1800] transition-colors"
                  >
                    Nhận đón
                  </button>
                )}

                {!isPending && isAcceptedByCurrentDriver && (
                  <button
                    type="button"
                    onClick={() => void onRelease(request.requestId)}
                    className="rounded-lg border border-[#ffd7d1] bg-white px-3 py-1.5 text-xs font-semibold text-[#ff1e00] hover:bg-[#fff5f3] transition-colors"
                  >
                    Nhả khách
                  </button>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export const PickupRequestPanel = memo(PickupRequestPanelComponent);
