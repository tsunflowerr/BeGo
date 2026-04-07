"use client";

import { useState, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";
import * as yup from "yup";
import { api } from "@/lib/api";

interface QueryEditorProps {
  sessionId: string;
  initialQuery: string;
  isHost: boolean;
  isEditable: boolean;
  onQueryUpdated?: (newQuery: string) => void;
}

const schema = yup.object().shape({
  query: yup
    .string()
    .trim()
    .required("Vui lòng nhập yêu cầu tìm kiếm")
    .max(500, "Yêu cầu không được vượt quá 500 ký tự"),
});

export function QueryEditor({
  sessionId,
  initialQuery,
  isHost,
  isEditable,
  onQueryUpdated,
}: QueryEditorProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [query, setQuery] = useState(initialQuery);
  const [tempQuery, setTempQuery] = useState(initialQuery);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSave = useCallback(async () => {
    setIsSaving(true);
    setError(null);

    try {
      const data = await schema.validate({ query: tempQuery });

      await api.sessions.updateQuery(sessionId, data.query!);
      setQuery(data.query!);
      setIsEditing(false);
      onQueryUpdated?.(data.query!);
    } catch (err) {
      if (err instanceof yup.ValidationError) {
        setError(err.message);
      } else {
        setError(err instanceof Error ? err.message : "Không thể cập nhật");
      }
    } finally {
      setIsSaving(false);
    }
  }, [sessionId, tempQuery, onQueryUpdated]);

  const handleCancel = useCallback(() => {
    setTempQuery(query);
    setIsEditing(false);
    setError(null);
  }, [query]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        handleSave();
      } else if (e.key === "Escape") {
        handleCancel();
      }
    },
    [handleSave, handleCancel]
  );

  // Example suggestions for Vietnamese natural language queries
  const suggestions = [
    "quán cà phê yên tĩnh có wifi",
    "quán ăn vặt gần trung tâm",
    "nhà hàng lẩu ngon giá rẻ",
    "quán trà sữa không gian đẹp",
  ];

  return (
    <div className="bg-white rounded-xl p-4 shadow-sm border border-[#e8f9fd]">
      <div className="flex items-center gap-2 mb-2">
        <span className="text-xl">🔍</span>
        <span className="font-medium text-gray-700">Yêu cầu tìm kiếm</span>
        {isHost && isEditable && !isEditing && (
          <button
            onClick={() => setIsEditing(true)}
            className="ml-auto text-sm text-[#ff1e00] hover:text-[#ff1e00]/80 transition-colors flex items-center gap-1"
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z"
              />
            </svg>
            Chỉnh sửa
          </button>
        )}
      </div>

      <AnimatePresence mode="wait">
        {isEditing ? (
          <motion.div
            key="editing"
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className="space-y-3"
          >
            <textarea
              value={tempQuery}
              onChange={(e) => setTempQuery(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Nhập yêu cầu bằng tiếng Việt tự nhiên..."
              className="w-full p-3 border border-gray-200 rounded-lg focus:ring-2 focus:ring-[#ff1e00]/20 focus:border-[#ff1e00] outline-none resize-none text-gray-700"
              rows={2}
              autoFocus
            />

            {/* Suggestions */}
            <div className="flex flex-wrap gap-2">
              {suggestions.map((suggestion) => (
                <button
                  key={suggestion}
                  onClick={() => setTempQuery(suggestion)}
                  className="text-xs px-2 py-1 bg-[#e8f9fd] text-gray-600 rounded-full hover:bg-[#e8f9fd]/70 transition-colors"
                >
                  {suggestion}
                </button>
              ))}
            </div>

            {error && (
              <p className="text-sm text-red-500 flex items-center gap-1">
                <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                  <path
                    fillRule="evenodd"
                    d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z"
                    clipRule="evenodd"
                  />
                </svg>
                {error}
              </p>
            )}

            <div className="flex gap-2 justify-end">
              <button
                onClick={handleCancel}
                disabled={isSaving}
                className="px-4 py-2 text-sm text-gray-600 hover:text-gray-800 transition-colors disabled:opacity-50"
              >
                Hủy
              </button>
              <button
                onClick={handleSave}
                disabled={isSaving}
                className="px-4 py-2 text-sm bg-[#ff1e00] text-white rounded-lg hover:bg-[#ff1e00]/90 transition-colors disabled:opacity-50 flex items-center gap-2"
              >
                {isSaving ? (
                  <>
                    <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                      <circle
                        className="opacity-25"
                        cx="12"
                        cy="12"
                        r="10"
                        stroke="currentColor"
                        strokeWidth="4"
                      />
                      <path
                        className="opacity-75"
                        fill="currentColor"
                        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
                      />
                    </svg>
                    Đang lưu...
                  </>
                ) : (
                  "Lưu"
                )}
              </button>
            </div>
          </motion.div>
        ) : (
          <motion.div
            key="display"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
          >
            <p className="text-gray-600 italic">
              {query || (
                <span className="text-gray-400">
                  Chưa có yêu cầu tìm kiếm. {isHost && isEditable && "Nhấn chỉnh sửa để thêm."}
                </span>
              )}
            </p>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
