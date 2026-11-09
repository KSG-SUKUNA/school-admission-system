# School Admission System

A containerized school-admission application built for the Assistant Software Engineer take-home assignment. The stack is ASP.NET Core 8, PostgreSQL 16, Entity Framework Core 8, React 18, TypeScript, Vite, and Docker Compose.

## Features

- Separate admission capacity for every class/shift.
- Student identification through the 17-digit birth-registration number supplied in `X-Student-Id`; no login is required by the brief.
- First-come, first-served seat allocation with a configurable temporary hold.
- Automatic hold expiry and seat release.
- Idempotent admission confirmation; confirming the same hold more than once does not increase confirmed seats more than once.
- Cancellation of an unconfirmed hold.
- Live class/seat refresh and a per-application countdown in the React UI.
- PostgreSQL persistence across container restarts.
- Database-coordinated concurrency protection that also works across two independent API processes.
- Backend integration tests and frontend component tests.

## Technology

| Layer | Technology |
|---|---|
| Backend | C# / ASP.NET Core 8 Minimal API |
| Data access | Entity Framework Core 8 / Npgsql |
| Database | PostgreSQL 16 |
| Frontend | React 18 / TypeScript / Vite |
| Frontend tests | Vitest / React Testing Library |
| Backend tests | xUnit / ASP.NET Core `WebApplicationFactory` |
| Runtime | Docker / Docker Compose |

## Run the application

The only runtime prerequisite is Docker Desktop (Windows/macOS) or Docker Engine with the Docker Compose plugin. Local .NET, Node.js, and PostgreSQL installations are not required to run the application.

1. Copy `.env.example` to `.env` in the repository root.

   PowerShell:

   ```powershell
   Copy-Item .env.example .env
   ```

   Bash:

   ```bash
   cp .env.example .env
   ```

2. Set a private `POSTGRES_PASSWORD` in `.env` before using the project beyond a local demonstration.
3. From the repository root, build and start the application:

   ```bash
   docker compose up --build -d
   ```

4. Check the containers:

   ```bash
   docker compose ps
   ```

5. Open the application and API health endpoint:

   - Frontend: <http://localhost:5173>
   - Backend health: <http://localhost:8080/health>

The API listens on port `8080`. The frontend is served by Nginx on port `5173`.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `POSTGRES_DB` | `school_admission` | Application database name |
| `POSTGRES_USER` | `postgres` | PostgreSQL user |
| `POSTGRES_PASSWORD` | `change-this-password` in `.env.example` | PostgreSQL password |
| `HOLD_DURATION_SECONDS` | `600` | Seat-hold lifetime in seconds |
| `VITE_API_BASE_URL` | `http://localhost:8080` | Backend URL embedded in the frontend build |

`ConnectionStrings__DefaultConnection` is assembled by Docker Compose from the PostgreSQL variables. `VITE_API_BASE_URL` is a build-time frontend setting; rebuild the frontend after changing it:

```bash
docker compose build --no-cache frontend
docker compose up -d frontend
```

## Main UI workflow

1. Enter the student's 17-digit birth-registration number at the top of the page.
2. In **ভর্তির Seat**, review each class/shift and its total, held, confirmed, and available seats.
3. Select **আবেদন করুন** to create a temporary hold.
4. In **আমার আবেদন**, review the hold status and countdown. Select **ভর্তি ফি দিন** to simulate a successful payment and confirm admission.
5. Select **বাতিল করুন** to cancel an unconfirmed hold.
6. In **School Admin**, create a class/shift and set its seat capacity.

The class list refreshes automatically. The application countdown updates every second, and the page refreshes class/application data periodically without a manual reload. The student ID is kept in browser local storage so the same student's applications can be retrieved after a page refresh or browser restart.

**Payment is simulated.** No payment gateway or payment credentials are used. The admin screen is intentionally available without authentication because the assignment says login is not required; it is not a production security model.

## API contract

All API request and response bodies are JSON. Student-specific endpoints use the `X-Student-Id` request header, which must contain exactly 17 digits.

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/classes` | Create a class/shift |
| `GET` | `/classes` | List classes with seat counts |
| `GET` | `/classes/{classId}` | Get one class and its seat counts |
| `POST` | `/classes/{classId}/holds` | Create a temporary seat hold |
| `GET` | `/holds/{holdId}` | Read an application's status |
| `POST` | `/holds/{holdId}/confirm` | Simulate payment and confirm admission |
| `DELETE` | `/holds/{holdId}` | Cancel an unconfirmed hold; success returns `204` |
| `GET` | `/me/holds` | List applications for the supplied student ID |

Hold statuses are `HELD`, `CONFIRMED`, `EXPIRED`, and `CANCELLED`. Invalid input returns a 4xx response. A full class, duplicate active application, expired hold confirmation, or invalid state transition returns `409` where appropriate. Error responses contain a JSON `message` field.

## Seat-capacity and concurrency design

Seat allocation is protected at the database level, not by an in-process C# lock:

1. A hold-creation transaction locks the target row in `AdmissionClasses` using PostgreSQL `SELECT ... FOR UPDATE`.
2. While holding that lock, the service expires due holds, checks for an existing active/confirmed application by the same student, counts active held and confirmed seats, and inserts the new hold only if capacity remains.
3. A partial unique index on `(StudentId, ClassId)` for `HELD` and `CONFIRMED` records independently enforces the one-active-application rule.
4. The transaction commits before the request returns. Other API processes sharing the database must acquire the same class-row lock before allocating a seat.
5. A PostgreSQL advisory lock serializes startup migrations if both API processes start at the same time.

A hosted worker expires overdue holds. Read and write operations also normalize expired holds, so seat availability does not depend on a browser being open or a user making a request.

## Automated tests

### Backend integration tests

The backend suite contains eleven tests covering:

- Class creation and initial seat counts.
- Invalid class input.
- Invalid student ID.
- Duplicate active applications.
- Idempotent confirmation.
- Expiry and automatic seat release.
- Rejection of confirmation after expiry.
- Cancellation and seat release.
- Rejection of cancellation after confirmation.
- Concurrent requests and two independent API hosts sharing one database.

Run the backend tests from the repository root:

```bash
docker compose --profile test run --build --rm backend-tests
```

Backend tests use the separate `school_admission_test` database service and do not add test records to the application database.

### Frontend component tests

The frontend suite checks student-ID validation, API-provided seat counts, and the expired-hold state/payment-button behavior.

```bash
docker compose --profile test run --build --rm frontend-tests
```

## Bonus: two backend processes sharing one database

Start the optional second API process:

```bash
docker compose --profile bonus up --build -d
```

The first process is available on port `8080`; the second is available on port `8081`. Both connect to the same PostgreSQL database and use the same row-locking seat-allocation service.

On Windows PowerShell, run the included concurrent-request demonstration:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\bonus-concurrency.ps1
```

From Bash, Git Bash, or WSL, the equivalent script is:

```bash
bash scripts/bonus-concurrency.sh
```

The demonstration creates a class with five seats and sends 25 concurrent hold requests across both API processes. It verifies that exactly five holds succeed and that the database seat count never exceeds the configured capacity.

## Persistence and shutdown

Stop the application without deleting its data:

```bash
docker compose down
```

The named `postgres_data` volume preserves application records across `docker compose down` and subsequent `docker compose up`. Do not run `docker compose down -v` unless you intentionally want to delete the database volume. The test database uses a separate `postgres_test_data` volume.

## Repository layout

```text
school-admission-system/
├── backend/
│   ├── Admission.Api/
│   └── Admission.Tests/
├── frontend/
│   └── src/
├── scripts/
├── docker-compose.yml
├── .env.example
├── README.md
├── ASSUMPTIONS.md
├── AI_USAGE.md
└── GUIDE_BN.md
```

## GitHub submission

1. Create a **public** repository in your own GitHub account.
2. Initialize Git in this project folder and configure your own author identity.
3. Commit the backend, frontend, Docker setup, tests, and documentation in separate meaningful commits. Do not combine the entire project into one final commit.
4. Add your GitHub repository as `origin`, push the `main` branch, and check that the public repository contains both `backend/` and `frontend/`.
5. Submit the public repository URL using the submission link in the assignment.

Do not upload or share the confidential assignment PDF. Keep `.env` out of Git; commit `.env.example` instead.

## Verification note

The developer workstation run recorded in `AI_USAGE.md` confirms that Docker built both application images, PostgreSQL reported healthy, and the backend and frontend containers started. The automated test commands and the two-process bonus demonstration must still be run and their actual results checked before submission. No test is described as passing until its command completes successfully.
