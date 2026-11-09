import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import App from "./App";
import { api } from "./api";

vi.mock("./api", () => ({
  api: {
    getClasses: vi.fn(),
    getMyHolds: vi.fn(),
    createClass: vi.fn(),
    createHold: vi.fn(),
    confirmHold: vi.fn(),
    cancelHold: vi.fn(),
    getHold: vi.fn()
  },
  ApiError: class ApiError extends Error {
    status: number;
    constructor(message: string, status: number) { super(message); this.status = status; }
  }
}));

const classFixture = {
  id: "c_test",
  name: "Class 6 - Morning Shift",
  totalSeats: 10,
  heldSeats: 2,
  confirmedSeats: 3,
  availableSeats: 5
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  vi.mocked(api.getClasses).mockResolvedValue([classFixture]);
  vi.mocked(api.getMyHolds).mockResolvedValue([]);
});

describe("School admission UI", () => {
  it("disables applying until a valid 17-digit student ID is entered", async () => {
    render(<App />);
    const applyButton = await screen.findByRole("button", { name: "আবেদন করুন" });
    expect(applyButton).toBeDisabled();

    fireEvent.change(screen.getByLabelText("শিক্ষার্থীর ১৭ সংখ্যার জন্মনিবন্ধন নম্বর"), {
      target: { value: "20152691234567890" }
    });

    await waitFor(() => expect(applyButton).toBeEnabled());
  });

  it("shows the available seat count from the API", async () => {
    render(<App />);
    expect(await screen.findByText("Class 6 - Morning Shift")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
  });

  it("shows an expired hold and disables the payment action", async () => {
    localStorage.setItem("school-admission-student-id", "20152691234567890");
    vi.mocked(api.getMyHolds).mockResolvedValue([{
      holdId: "h_expired",
      classId: "c_test",
      className: "Class 6 - Morning Shift",
      status: "HELD",
      expiresAt: new Date(Date.now() - 60_000).toISOString()
    }]);

    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: /আমার আবেদন/ }));
    expect(await screen.findByText("মেয়াদ শেষ")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "ভর্তি ফি দিন" })).toBeDisabled();
  });
});
