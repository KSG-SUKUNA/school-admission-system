import type { ClassInfo, CreatedHold, HoldInfo, MyHoldInfo } from "./types";

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || "http://localhost:8080").replace(/\/$/, "");

export class ApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(path: string, options: RequestInit = {}, studentId?: string): Promise<T> {
  const headers = new Headers(options.headers);
  headers.set("Accept", "application/json");
  if (options.body) headers.set("Content-Type", "application/json");
  if (studentId) headers.set("X-Student-Id", studentId);

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, { ...options, headers });
  } catch {
    throw new ApiError("সার্ভারের সঙ্গে সংযোগ করা যাচ্ছে না। কিছুক্ষণ পর আবার চেষ্টা করুন।", 0);
  }

  if (response.status === 204) return undefined as T;
  const body = await response.json().catch(() => ({} as { message?: string; detail?: string }));
  if (!response.ok) {
    const message = body.message || body.detail || messageForStatus(response.status);
    throw new ApiError(message, response.status);
  }
  return body as T;
}

function messageForStatus(status: number): string {
  if (status === 400) return "তথ্য সঠিকভাবে দিন।";
  if (status === 404) return "তথ্যটি পাওয়া যায়নি।";
  if (status === 409) return "এই মুহূর্তে আবেদনটি সম্পন্ন করা যাচ্ছে না।";
  return "একটি সমস্যা হয়েছে। আবার চেষ্টা করুন।";
}

export const api = {
  getClasses: () => request<ClassInfo[]>("/classes"),
  createClass: (name: string, totalSeats: number) =>
    request<{ id: string; name: string; totalSeats: number }>("/classes", { method: "POST", body: JSON.stringify({ name, totalSeats }) }),
  createHold: (classId: string, studentId: string) =>
    request<CreatedHold>(`/classes/${classId}/holds`, { method: "POST" }, studentId),
  getHold: (holdId: string) => request<HoldInfo>(`/holds/${holdId}`),
  confirmHold: (holdId: string, studentId: string) =>
    request<{ holdId: string; status: string }>(`/holds/${holdId}/confirm`, { method: "POST" }, studentId),
  cancelHold: (holdId: string, studentId: string) =>
    request<void>(`/holds/${holdId}`, { method: "DELETE" }, studentId),
  getMyHolds: (studentId: string) => request<MyHoldInfo[]>("/me/holds", {}, studentId)
};
