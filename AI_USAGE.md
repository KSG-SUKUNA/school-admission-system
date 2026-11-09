# AI usage disclosure

AI assistance was used during development. The assignment permits AI tools and explicitly requests this disclosure.

- **Tool:** ChatGPT by OpenAI (GPT-6).
- **Areas assisted:** interpreting the supplied assignment, planning the application architecture, drafting the API and database implementation, designing the seat-allocation/concurrency approach, preparing React UI components, writing automated tests, preparing Docker Compose configuration, and drafting project documentation.
- **Developer review:** the generated implementation was reviewed against the assignment's API contract and mandatory rules. The developer ran the Docker build/start workflow locally and reviewed the resulting container status and application UI.

## Issues found during review and corrections

| Issue | How it was found | Correction |
|---|---|---|
| Frontend TypeScript project-reference error (`TS6310`) because the referenced composite Vite config project had `noEmit: true`. | The frontend Docker build output reported the TypeScript error. | Updated `frontend/tsconfig.node.json` to emit declaration files only (`declaration: true`, `emitDeclarationOnly: true`, `noEmit: false`). |
| Backend expression-tree compile error (`CS0853`) caused by the named argument `ignoreCase: true` inside an EF Core value-conversion expression. | The backend Docker publish output identified the model configuration and migration lines. | Changed the call to use the equivalent positional argument: `Enum.Parse<HoldStatus>(value, true)`. |
| The initial backend test expected seat-count fields in the `POST /classes` response, although the assignment's sample response contains only `id`, `name`, and `totalSeats`. | API contract review. | Updated the test to verify the creation response contract, then call `GET /classes/{classId}` to verify the initial seat counts. |
| Backend integration tests originally shared the application database. | Test-environment review. | Added a dedicated `db-test` PostgreSQL service and `postgres_test_data` volume for backend tests. |

## Verification record

| Date | Check | Result |
|---|---|---|
| 2026-11-09 | `docker compose up --build -d` on the developer workstation | **Observed successful.** Both application images built; PostgreSQL reported `healthy`; backend and frontend containers started. |
| 2026-11-09 | `docker compose ps` | **Observed successful.** Backend and frontend showed `Up`; PostgreSQL showed `Up (healthy)`. |
| 2026-11-09 | Frontend browser smoke check | **User-reported successful.** The application opened and appeared correct in the user's browser. |
| Before submission | `docker compose --profile test run --build --rm backend-tests` | **Not yet run in the recorded session.** Run locally and review the exit code/output. |
| Before submission | `docker compose --profile test run --build --rm frontend-tests` | **Not yet run in the recorded session.** Run locally and review the exit code/output. |
| Before submission | Two-process bonus concurrency script | **Not yet run in the recorded session.** Run locally and confirm the script prints `PASS`. |

Only the checks marked observed/user-reported above are recorded as completed. The automated test suites and multi-process bonus must be executed successfully before submission; no unexecuted test is claimed as passing.
