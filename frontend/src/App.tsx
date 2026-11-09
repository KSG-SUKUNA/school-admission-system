import { useCallback, useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { api, ApiError } from "./api";
import type { ClassInfo, HoldStatus, MyHoldInfo } from "./types";

const STUDENT_ID_KEY = "school-admission-student-id";
const studentIdPattern = /^[0-9]{17}$/;

function statusLabel(status: HoldStatus): string {
  switch (status) {
    case "HELD": return "সিট সাময়িকভাবে সংরক্ষিত";
    case "CONFIRMED": return "ভর্তি নিশ্চিত";
    case "EXPIRED": return "মেয়াদ শেষ";
    case "CANCELLED": return "বাতিল";
  }
}

function effectiveStatus(hold: MyHoldInfo, now: number): HoldStatus {
  if (hold.status === "HELD" && new Date(hold.expiresAt).getTime() <= now) return "EXPIRED";
  return hold.status;
}

function remainingTime(expiresAt: string, now: number): string {
  const seconds = Math.max(0, Math.ceil((new Date(expiresAt).getTime() - now) / 1000));
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;
  return `${minutes} মিনিট ${rest} সেকেন্ড`;
}

function explainError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 0) return error.message;
    if (error.status === 409) {
      const message = error.message.toLowerCase();
      if (message.includes("no seats")) return "এই ক্লাসে আর খালি Seat নেই। অন্য ক্লাস বেছে নিন।";
      if (message.includes("already has")) return "এই শিক্ষার্থীর এই Class-এ আগে থেকেই একটি সক্রিয় আবেদন বা নিশ্চিত ভর্তি আছে।";
      if (message.includes("expired")) return "আবেদনের মেয়াদ শেষ হয়েছে। Seat থাকলে নতুন করে আবেদন করুন।";
      return "এই আবেদনটি সম্পন্ন করা যাচ্ছে না। Seat বা আবেদনের অবস্থা যাচাই করুন।";
    }
    if (error.status === 400) return "জন্মনিবন্ধন নম্বর বা দেওয়া তথ্য সঠিক নয়। আবার যাচাই করুন।";
    if (error.status === 404) return "আবেদন বা ক্লাসটি আর পাওয়া যাচ্ছে না। তালিকা হালনাগাদ করুন।";
    if (error.status >= 500) return "সার্ভারে সমস্যা হয়েছে। কিছুক্ষণ পর আবার চেষ্টা করুন।";
    return "অনুরোধটি সম্পন্ন করা যায়নি। আবার চেষ্টা করুন।";
  }
  return "অপ্রত্যাশিত সমস্যা হয়েছে। আবার চেষ্টা করুন।";
}

export default function App() {
  const [studentId, setStudentId] = useState(() => localStorage.getItem(STUDENT_ID_KEY) ?? "");
  const [classes, setClasses] = useState<ClassInfo[]>([]);
  const [holds, setHolds] = useState<MyHoldInfo[]>([]);
  const [activeTab, setActiveTab] = useState<"admissions" | "applications" | "admin">("admissions");
  const [className, setClassName] = useState("");
  const [totalSeats, setTotalSeats] = useState("120");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busyId, setBusyId] = useState("");
  const [loading, setLoading] = useState(true);
  const [now, setNow] = useState(Date.now());

  const validStudentId = studentIdPattern.test(studentId);

  useEffect(() => {
    localStorage.setItem(STUDENT_ID_KEY, studentId);
  }, [studentId]);

  const refreshAll = useCallback(async () => {
    try {
      const classData = await api.getClasses();
      setClasses(classData);
      if (studentIdPattern.test(studentId)) {
        const holdData = await api.getMyHolds(studentId);
        setHolds(holdData);
      } else {
        setHolds([]);
      }
      setError("");
    } catch (loadError) {
      setError(explainError(loadError));
    } finally {
      setLoading(false);
    }
  }, [studentId]);

  useEffect(() => {
    void refreshAll();
    const refreshTimer = window.setInterval(() => void refreshAll(), 5000);
    const clockTimer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => {
      window.clearInterval(refreshTimer);
      window.clearInterval(clockTimer);
    };
  }, [refreshAll]);

  const activeClassIds = useMemo(() => new Set(
    holds.filter(hold => {
      const status = effectiveStatus(hold, now);
      return status === "HELD" || status === "CONFIRMED";
    }).map(hold => hold.classId)
  ), [holds, now]);

  async function handleCreateClass(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setNotice("");
    const seats = Number(totalSeats);
    if (!className.trim() || !Number.isInteger(seats) || seats < 1) {
      setError("ক্লাসের নাম এবং ১ বা তার বেশি Seat সংখ্যা দিন।");
      return;
    }
    setBusyId("create-class");
    try {
      await api.createClass(className.trim(), seats);
      setClassName("");
      setTotalSeats("120");
      setNotice("নতুন ক্লাস সফলভাবে তৈরি হয়েছে।");
      await refreshAll();
      setActiveTab("admissions");
    } catch (createError) {
      setError(explainError(createError));
    } finally {
      setBusyId("");
    }
  }

  async function handleApply(item: ClassInfo) {
    if (!validStudentId) {
      setError("আবেদন করার আগে ১৭ সংখ্যার জন্মনিবন্ধন নম্বর দিন।");
      return;
    }
    setError("");
    setNotice("");
    setBusyId(item.id);
    try {
      await api.createHold(item.id, studentId);
      setNotice(`${item.name}-এ আপনার জন্য একটি Seat সাময়িকভাবে সংরক্ষণ করা হয়েছে।`);
      await refreshAll();
      setActiveTab("applications");
    } catch (applyError) {
      setError(explainError(applyError));
    } finally {
      setBusyId("");
    }
  }

  async function handleConfirm(hold: MyHoldInfo) {
    if (!validStudentId) return;
    setError("");
    setNotice("");
    setBusyId(hold.holdId);
    try {
      await api.confirmHold(hold.holdId, studentId);
      setNotice("ভর্তি সফলভাবে নিশ্চিত হয়েছে।");
      await refreshAll();
    } catch (confirmError) {
      setError(explainError(confirmError));
      await refreshAll();
    } finally {
      setBusyId("");
    }
  }

  async function handleCancel(hold: MyHoldInfo) {
    if (!validStudentId) return;
    setError("");
    setNotice("");
    setBusyId(hold.holdId);
    try {
      await api.cancelHold(hold.holdId, studentId);
      setNotice("আবেদনটি বাতিল করা হয়েছে।");
      await refreshAll();
    } catch (cancelError) {
      setError(explainError(cancelError));
      await refreshAll();
    } finally {
      setBusyId("");
    }
  }

  return (
    <main className="app-shell">
      <header className="hero">
        <div className="hero-copy">
          <span className="eyebrow">SCHOOL ADMISSION PORTAL</span>
          <h1>নতুন শিক্ষাবর্ষে ভর্তি</h1>
          <p>আগে আবেদন করুন, আগে Seat নিশ্চিত করুন। আপনার আবেদন ও Seat-এর অবস্থা এখান থেকেই দেখুন।</p>
        </div>
        <div className="hero-mark" aria-hidden="true">SA</div>
      </header>

      <section className="identity-panel">
        <div>
          <label htmlFor="student-id">শিক্ষার্থীর ১৭ সংখ্যার জন্মনিবন্ধন নম্বর</label>
          <p>এই নম্বরটিই আপনার পরিচয় হিসেবে ব্যবহার হবে। Login প্রয়োজন নেই।</p>
        </div>
        <div className="identity-input-wrap">
          <input
            id="student-id"
            value={studentId}
            onChange={event => setStudentId(event.target.value.replace(/\D/g, "").slice(0, 17))}
            inputMode="numeric"
            maxLength={17}
            placeholder="১৭ সংখ্যার নম্বর লিখুন"
            aria-describedby="student-id-hint"
          />
          <span id="student-id-hint" className={validStudentId ? "input-state valid" : "input-state"}>
            {validStudentId ? "নম্বর সঠিক" : `${studentId.length}/17`}
          </span>
        </div>
      </section>

      <nav className="tabs" aria-label="প্রধান মেনু">
        <button className={activeTab === "admissions" ? "tab active" : "tab"} onClick={() => setActiveTab("admissions")}>ভর্তির Seat</button>
        <button className={activeTab === "applications" ? "tab active" : "tab"} onClick={() => setActiveTab("applications")}>আমার আবেদন <span className="tab-count">{holds.length}</span></button>
        <button className={activeTab === "admin" ? "tab active" : "tab"} onClick={() => setActiveTab("admin")}>School Admin</button>
      </nav>

      {error && <div className="alert error" role="alert">{error}</div>}
      {notice && <div className="alert success" role="status">{notice}</div>}

      {activeTab === "admissions" && (
        <section className="content-section">
          <div className="section-heading">
            <div>
              <span className="eyebrow dark">AVAILABLE CLASSES</span>
              <h2>ভর্তির জন্য উপলভ্য ক্লাস</h2>
              <p>প্রতিটি Shift আলাদা Class হিসেবে গণনা করা হয়। Seat-এর হিসাব স্বয়ংক্রিয়ভাবে হালনাগাদ হয়।</p>
            </div>
            <button className="button secondary" onClick={() => void refreshAll()}>হালনাগাদ করুন</button>
          </div>
          {loading ? <div className="empty-state">তথ্য লোড হচ্ছে...</div> : classes.length === 0 ? (
            <div className="empty-state">এখনো কোনো Class তৈরি করা হয়নি। School Admin থেকে নতুন Class তৈরি করুন।</div>
          ) : (
            <div className="class-grid">
              {classes.map(item => {
                const alreadyApplied = activeClassIds.has(item.id);
                return (
                  <article className="class-card" key={item.id}>
                    <div className="class-card-top">
                      <span className="class-tag">ADMISSION OPEN</span>
                      <span className={item.availableSeats > 0 ? "seat-indicator open" : "seat-indicator full"}>
                        {item.availableSeats > 0 ? "Seat আছে" : "Seat পূর্ণ"}
                      </span>
                    </div>
                    <h3>{item.name}</h3>
                    <div className="seat-summary">
                      <div><strong>{item.availableSeats}</strong><span>খালি</span></div>
                      <div><strong>{item.heldSeats}</strong><span>Hold</span></div>
                      <div><strong>{item.confirmedSeats}</strong><span>নিশ্চিত</span></div>
                      <div><strong>{item.totalSeats}</strong><span>মোট</span></div>
                    </div>
                    <div className="capacity-track" aria-label={`${item.availableSeats} of ${item.totalSeats} seats available`}>
                      <span style={{ width: `${Math.min(100, ((item.totalSeats - item.availableSeats) / item.totalSeats) * 100)}%` }} />
                    </div>
                    <button
                      className="button primary full-width"
                      disabled={!validStudentId || item.availableSeats < 1 || alreadyApplied || busyId === item.id}
                      onClick={() => void handleApply(item)}
                    >
                      {busyId === item.id ? "প্রক্রিয়াধীন..." : alreadyApplied ? "আবেদন করা হয়েছে" : item.availableSeats < 1 ? "Seat নেই" : "আবেদন করুন"}
                    </button>
                    {!validStudentId && <small className="helper-text">আবেদন করতে উপরে শিক্ষার্থীর নম্বর দিন।</small>}
                  </article>
                );
              })}
            </div>
          )}
        </section>
      )}

      {activeTab === "applications" && (
        <section className="content-section">
          <div className="section-heading">
            <div>
              <span className="eyebrow dark">YOUR APPLICATIONS</span>
              <h2>আমার আবেদনসমূহ</h2>
              <p>আপনার বর্তমান ও পূর্বের সব আবেদনের অবস্থা এখানে দেখা যাবে।</p>
            </div>
          </div>
          {!validStudentId ? <div className="empty-state">আবেদন দেখতে উপরে ১৭ সংখ্যার জন্মনিবন্ধন নম্বর দিন।</div> : holds.length === 0 ? (
            <div className="empty-state">এই শিক্ষার্থীর কোনো আবেদন পাওয়া যায়নি।</div>
          ) : (
            <div className="application-list">
              {holds.map(hold => {
                const status = effectiveStatus(hold, now);
                const isHeld = status === "HELD";
                return (
                  <article className="application-card" key={hold.holdId}>
                    <div className="application-main">
                      <div>
                        <span className={`status-pill status-${status.toLowerCase()}`}>{statusLabel(status)}</span>
                        <h3>{hold.className}</h3>
                        <p className="application-id">Application ID: {hold.holdId}</p>
                      </div>
                      {isHeld && <div className="countdown"><span>মেয়াদ শেষ হতে বাকি</span><strong>{remainingTime(hold.expiresAt, now)}</strong></div>}
                    </div>
                    <div className="application-actions">
                      <button className="button primary" disabled={!isHeld || busyId === hold.holdId} onClick={() => void handleConfirm(hold)}>
                        {busyId === hold.holdId ? "প্রক্রিয়াধীন..." : "ভর্তি ফি দিন"}
                      </button>
                      <button className="button danger" disabled={!isHeld || busyId === hold.holdId} onClick={() => void handleCancel(hold)}>বাতিল করুন</button>
                    </div>
                    {status === "EXPIRED" && <p className="status-note">আবেদনের সময় শেষ হয়েছে। Seat আবার অন্যদের জন্য উন্মুক্ত।</p>}
                    {status === "CONFIRMED" && <p className="status-note">আপনার ভর্তি নিশ্চিত হয়েছে।</p>}
                  </article>
                );
              })}
            </div>
          )}
        </section>
      )}

      {activeTab === "admin" && (
        <section className="content-section admin-section">
          <div className="section-heading">
            <div>
              <span className="eyebrow dark">SCHOOL ADMIN</span>
              <h2>নতুন Class তৈরি করুন</h2>
              <p>প্রতিটি Class/Shift-এর জন্য আলাদা Seat সংখ্যা নির্ধারণ করুন।</p>
            </div>
          </div>
          <form className="admin-form" onSubmit={handleCreateClass}>
            <label htmlFor="class-name">Class-এর নাম</label>
            <input id="class-name" value={className} onChange={event => setClassName(event.target.value)} maxLength={120} placeholder="যেমন: Class 6 - Morning Shift" />
            <label htmlFor="total-seats">মোট Seat সংখ্যা</label>
            <input id="total-seats" type="number" min="1" max="100000" value={totalSeats} onChange={event => setTotalSeats(event.target.value)} />
            <button className="button primary" type="submit" disabled={busyId === "create-class"}>
              {busyId === "create-class" ? "তৈরি হচ্ছে..." : "Class তৈরি করুন"}
            </button>
          </form>
          <p className="admin-note">Assignment অনুযায়ী Login বাধ্যতামূলক নয়। এটি একটি demonstration admin screen; production ব্যবহারে Admin authentication যোগ করতে হবে।</p>
        </section>
      )}

      <footer className="footer">
        <span>School Admission System</span>
        <span>Seat status refreshes automatically</span>
      </footer>
    </main>
  );
}
