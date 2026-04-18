"use client";

import { useState, useEffect, useCallback, useRef } from "react";
import * as signalR from "@microsoft/signalr";
import { API_BASE_URL } from "@/lib/api";
import {
  MemberJoinedEvent,
  ComputingStartedEvent,
  OptimizationCompletedEvent,
  VoteSubmittedEvent,
  VotingCompletedEvent,
  PickupRequestsUpdatedEvent,
  DepartureLockedEvent,
  SignalRError,
} from "@/types";

type ConnectionState = "disconnected" | "connecting" | "connected" | "reconnecting";

interface UseSignalROptions {
  sessionId: string;
  onMemberJoined?: (event: MemberJoinedEvent) => void;
  onComputingStarted?: (event: ComputingStartedEvent) => void;
  onOptimizationCompleted?: (event: OptimizationCompletedEvent) => void;
  onVoteSubmitted?: (event: VoteSubmittedEvent) => void;
  onVotingCompleted?: (event: VotingCompletedEvent) => void;
  onPickupRequestsUpdated?: (event: PickupRequestsUpdatedEvent) => void;
  onDepartureLocked?: (event: DepartureLockedEvent) => void;
  onError?: (error: SignalRError) => void;
}

export function useSignalR({
  sessionId,
  onMemberJoined,
  onComputingStarted,
  onOptimizationCompleted,
  onVoteSubmitted,
  onVotingCompleted,
  onPickupRequestsUpdated,
  onDepartureLocked,
  onError,
}: UseSignalROptions) {
  const [connectionState, setConnectionState] = useState<ConnectionState>("disconnected");
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const mountedRef = useRef(true);

  const connect = useCallback(async () => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    setConnectionState("connecting");

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}/hubs/session`, {
        withCredentials: true,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Set up event handlers
    connection.on("MemberJoined", (event: MemberJoinedEvent) => {
      if (mountedRef.current && onMemberJoined) {
        onMemberJoined(event);
      }
    });

    connection.on("ComputingStarted", (event: ComputingStartedEvent) => {
      if (mountedRef.current && onComputingStarted) {
        onComputingStarted(event);
      }
    });

    connection.on("OptimizationCompleted", (event: OptimizationCompletedEvent) => {
      if (mountedRef.current && onOptimizationCompleted) {
        onOptimizationCompleted(event);
      }
    });

    connection.on("VoteSubmitted", (event: VoteSubmittedEvent) => {
      if (mountedRef.current && onVoteSubmitted) {
        onVoteSubmitted(event);
      }
    });

    connection.on("VotingCompleted", (event: VotingCompletedEvent) => {
      if (mountedRef.current && onVotingCompleted) {
        onVotingCompleted(event);
      }
    });

    connection.on("PickupRequestsUpdated", (event: PickupRequestsUpdatedEvent) => {
      if (mountedRef.current && onPickupRequestsUpdated) {
        onPickupRequestsUpdated(event);
      }
    });

    connection.on("DepartureLocked", (event: DepartureLockedEvent) => {
      if (mountedRef.current && onDepartureLocked) {
        onDepartureLocked(event);
      }
    });

    connection.on("Error", (error: SignalRError) => {
      if (mountedRef.current && onError) {
        onError(error);
      }
    });

    connection.onreconnecting(() => {
      if (mountedRef.current) {
        setConnectionState("reconnecting");
      }
    });

    connection.onreconnected(async () => {
      if (mountedRef.current) {
        setConnectionState("connected");
        // Rejoin the session group after reconnection
        try {
          await connection.invoke("JoinSessionGroup", sessionId);
        } catch (err) {
          console.error("Failed to rejoin session group:", err);
        }
      }
    });

    connection.onclose(() => {
      if (mountedRef.current) {
        setConnectionState("disconnected");
      }
    });

    connectionRef.current = connection;

    try {
      await connection.start();
      if (mountedRef.current) {
        setConnectionState("connected");
        // Join the session group
        await connection.invoke("JoinSessionGroup", sessionId);
      }
    } catch (error) {
      console.error("SignalR connection failed:", error);
      if (mountedRef.current) {
        setConnectionState("disconnected");
      }
    }
  }, [sessionId, onMemberJoined, onComputingStarted, onOptimizationCompleted, onVoteSubmitted, onVotingCompleted, onPickupRequestsUpdated, onDepartureLocked, onError]);

  const disconnect = useCallback(async () => {
    if (connectionRef.current) {
      const connection = connectionRef.current;
      try {
        if (connection.state === signalR.HubConnectionState.Connected) {
          await connection.invoke("LeaveSessionGroup", sessionId);
        }

        if (connection.state !== signalR.HubConnectionState.Disconnected) {
          await connection.stop();
        }
      } catch (error) {
        console.error("Failed to disconnect:", error);
      }
      connectionRef.current = null;
      setConnectionState("disconnected");
    }
  }, [sessionId]);

  useEffect(() => {
    mountedRef.current = true;
    const timer = window.setTimeout(() => {
      void connect();
    }, 0);

    return () => {
      mountedRef.current = false;
      window.clearTimeout(timer);
      void disconnect();
    };
  }, [connect, disconnect]);

  return {
    connectionState,
    isConnected: connectionState === "connected",
    connect,
    disconnect,
  };
}
