"use client";

import { memo, useCallback, useEffect, useRef, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Member, Venue, GeometricMedian } from "@/types";

interface MapViewProps {
  members: Member[];
  geometricMedian?: GeometricMedian;
  venues?: Venue[];
  winningVenueId?: string;
  isLoading?: boolean;
  onMemberClick?: (member: Member) => void;
}

// Default center (Ho Chi Minh City)
const DEFAULT_CENTER = { lat: 10.8231, lng: 106.6297 };

function MapViewComponent({
  members,
  geometricMedian,
  venues = [],
  winningVenueId,
  isLoading = false,
  onMemberClick,
}: MapViewProps) {
  const mapRef = useRef<HTMLDivElement>(null);
  const mapInstanceRef = useRef<google.maps.Map | null>(null);
  const markersRef = useRef<Map<string, google.maps.marker.AdvancedMarkerElement>>(new Map());
  const [mapLoaded, setMapLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Calculate bounds from all points
  const calculateBounds = useCallback(() => {
    const allPoints: { lat: number; lng: number }[] = [];
    
    members.forEach((m) => {
      if (m.latitude && m.longitude) {
        allPoints.push({ lat: m.latitude, lng: m.longitude });
      }
    });
    
    if (geometricMedian) {
      allPoints.push({ lat: geometricMedian.latitude, lng: geometricMedian.longitude });
    }
    
    venues.forEach((v) => {
      allPoints.push({ lat: v.latitude, lng: v.longitude });
    });
    
    return allPoints;
  }, [members, geometricMedian, venues]);

  // Initialize map
  useEffect(() => {
    const initMap = async () => {
      if (!mapRef.current || mapInstanceRef.current) return;

      try {
        // Check if Google Maps is loaded
        if (typeof google === "undefined" || !google.maps) {
          // Load Google Maps dynamically
          const script = document.createElement("script");
          script.src = `https://maps.googleapis.com/maps/api/js?key=${process.env.NEXT_PUBLIC_GOOGLE_MAPS_API_KEY}&libraries=marker&v=weekly`;
          script.async = true;
          script.defer = true;
          
          script.onload = () => {
            createMap();
          };
          
          script.onerror = () => {
            setError("Không thể tải Google Maps");
          };
          
          document.head.appendChild(script);
        } else {
          createMap();
        }
      } catch (err) {
        console.error("Map init error:", err);
        setError("Không thể khởi tạo bản đồ");
      }
    };

    const createMap = () => {
      if (!mapRef.current) return;
      
      const points = calculateBounds();
      const center = points.length > 0 
        ? { lat: points[0].lat, lng: points[0].lng }
        : DEFAULT_CENTER;

      const map = new google.maps.Map(mapRef.current, {
        center,
        zoom: 14,
        mapId: "optigo-map",
        disableDefaultUI: false,
        zoomControl: true,
        streetViewControl: false,
        fullscreenControl: false,
        mapTypeControl: false,
        styles: [
          {
            featureType: "poi",
            elementType: "labels",
            stylers: [{ visibility: "off" }],
          },
        ],
      });

      mapInstanceRef.current = map;
      setMapLoaded(true);
    };

    initMap();

    return () => {
      markersRef.current.forEach((marker) => {
        marker.map = null;
      });
      markersRef.current.clear();
    };
  }, [calculateBounds]);

  // Update markers when data changes
  useEffect(() => {
    if (!mapInstanceRef.current || !mapLoaded) return;

    const map = mapInstanceRef.current;

    // Clear old markers
    markersRef.current.forEach((marker) => {
      marker.map = null;
    });
    markersRef.current.clear();

    // Add member markers
    members.forEach((member) => {
      if (!member.latitude || !member.longitude) return;

      const pinElement = document.createElement("div");
      pinElement.className = "member-pin";
      pinElement.innerHTML = `
        <div style="
          position: relative;
          display: flex;
          flex-direction: column;
          align-items: center;
        ">
          <div style="
            width: 40px;
            height: 40px;
            border-radius: 50%;
            background: linear-gradient(135deg, #ff1e00, #ff4d33);
            display: flex;
            align-items: center;
            justify-content: center;
            color: white;
            font-weight: 600;
            font-size: 14px;
            box-shadow: 0 4px 12px rgba(255, 30, 0, 0.4);
            border: 3px solid white;
          ">
            ${member.name.charAt(0).toUpperCase()}
          </div>
          <div style="
            width: 0;
            height: 0;
            border-left: 8px solid transparent;
            border-right: 8px solid transparent;
            border-top: 12px solid #ff1e00;
            margin-top: -2px;
          "></div>
        </div>
      `;

      const marker = new google.maps.marker.AdvancedMarkerElement({
        map,
        position: { lat: member.latitude, lng: member.longitude },
        content: pinElement,
        title: member.name,
      });

      marker.addListener("click", () => {
        if (onMemberClick) onMemberClick(member);
      });

      markersRef.current.set(`member-${member.id}`, marker);
    });

    // Add venue markers
    venues.forEach((venue) => {
      const isWinner = venue.venueId === winningVenueId;
      const pinElement = document.createElement("div");
      pinElement.innerHTML = `
        <div style="
          position: relative;
          display: flex;
          flex-direction: column;
          align-items: center;
        ">
          <div style="
            padding: 8px 12px;
            border-radius: 20px;
            background: ${isWinner ? "#59ce8f" : "white"};
            color: ${isWinner ? "white" : "#1a1a2e"};
            font-weight: 600;
            font-size: 12px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.15);
            border: 2px solid ${isWinner ? "#59ce8f" : "#e8f9fd"};
            white-space: nowrap;
            max-width: 150px;
            overflow: hidden;
            text-overflow: ellipsis;
          ">
            ${isWinner ? "🏆 " : ""}${venue.name}
          </div>
          <div style="
            width: 0;
            height: 0;
            border-left: 6px solid transparent;
            border-right: 6px solid transparent;
            border-top: 8px solid ${isWinner ? "#59ce8f" : "white"};
            margin-top: -1px;
          "></div>
        </div>
      `;

      const marker = new google.maps.marker.AdvancedMarkerElement({
        map,
        position: { lat: venue.latitude, lng: venue.longitude },
        content: pinElement,
        title: venue.name,
      });

      markersRef.current.set(`venue-${venue.venueId}`, marker);
    });

    // Add geometric median marker
    if (geometricMedian) {
      const medianElement = document.createElement("div");
      medianElement.innerHTML = `
        <div style="
          width: 24px;
          height: 24px;
          border-radius: 50%;
          background: #e8f9fd;
          border: 3px solid #59ce8f;
          box-shadow: 0 2px 8px rgba(89, 206, 143, 0.4);
        "></div>
      `;

      const marker = new google.maps.marker.AdvancedMarkerElement({
        map,
        position: { lat: geometricMedian.latitude, lng: geometricMedian.longitude },
        content: medianElement,
        title: "Điểm trung tâm",
      });

      markersRef.current.set("median", marker);
    }

    // Fit bounds
    if (members.length > 0 || venues.length > 0) {
      const bounds = new google.maps.LatLngBounds();
      members.forEach((m) => {
        if (m.latitude && m.longitude) {
          bounds.extend({ lat: m.latitude, lng: m.longitude });
        }
      });
      venues.forEach((v) => {
        bounds.extend({ lat: v.latitude, lng: v.longitude });
      });
      if (geometricMedian) {
        bounds.extend({ lat: geometricMedian.latitude, lng: geometricMedian.longitude });
      }
      map.fitBounds(bounds, 50);
    }
  }, [members, venues, geometricMedian, winningVenueId, mapLoaded, onMemberClick]);

  return (
    <div className="relative w-full h-full min-h-[300px] rounded-2xl overflow-hidden bg-[#e8f9fd]">
      {/* Map container */}
      <div ref={mapRef} className="w-full h-full" />

      {/* Loading overlay */}
      <AnimatePresence>
        {isLoading && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="absolute inset-0 bg-white/80 backdrop-blur-sm flex flex-col items-center justify-center z-10"
          >
            <motion.div
              animate={{ rotate: 360 }}
              transition={{ duration: 2, repeat: Infinity, ease: "linear" }}
              className="w-16 h-16 border-4 border-[#e8f9fd] border-t-[#ff1e00] rounded-full mb-4"
            />
            <p className="text-[#1a1a2e] font-medium">Đang tìm kiếm địa điểm...</p>
            <p className="text-[#6b7280] text-sm mt-1">AI đang tính toán điểm hẹn tối ưu nhất</p>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Error state */}
      {error && (
        <div className="absolute inset-0 bg-[#e8f9fd] flex flex-col items-center justify-center">
          <svg className="w-12 h-12 text-[#ff1e00] mb-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
          </svg>
          <p className="text-[#1a1a2e] font-medium">{error}</p>
        </div>
      )}

      {/* No data placeholder */}
      {!error && !isLoading && members.length === 0 && (
        <div className="absolute inset-0 bg-[#e8f9fd] flex flex-col items-center justify-center">
          <svg className="w-16 h-16 text-[#59ce8f] mb-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 20l-5.447-2.724A1 1 0 013 16.382V5.618a1 1 0 011.447-.894L9 7m0 13l6-3m-6 3V7m6 10l4.553 2.276A1 1 0 0021 18.382V7.618a1 1 0 00-.553-.894L15 4m0 13V4m0 0L9 7" />
          </svg>
          <p className="text-[#1a1a2e] font-medium">Đang chờ thành viên</p>
          <p className="text-[#6b7280] text-sm mt-1">Vị trí sẽ hiện khi có người tham gia</p>
        </div>
      )}
    </div>
  );
}

export const MapView = memo(MapViewComponent);
