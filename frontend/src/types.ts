export type HoldStatus = "HELD" | "CONFIRMED" | "EXPIRED" | "CANCELLED";

export interface ClassInfo {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}

export interface HoldInfo {
  holdId: string;
  classId: string;
  status: HoldStatus;
  expiresAt: string;
}

export interface MyHoldInfo extends HoldInfo {
  className: string;
}

export interface CreatedHold extends HoldInfo {
  studentId: string;
}
