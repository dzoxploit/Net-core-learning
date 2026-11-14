# Meeting room reservations: RuangMeetingApi + RuangMeetingWeb

```
Browser ──(form POST + antiforgery token)──> RuangMeetingWeb (ASP.NET Core MVC, :5151)
                                                 │  HttpClient + X-Api-Key + X-Correlation-ID
                                                 ▼
                                          RuangMeetingApi (ASP.NET Core Web API, :5133) ──> SQL Server (RuangMeetingDb)
                                                 └─ Serilog: console + logs/api-*.log + logs/api-*.json
```

## Run

```bash
cd RuangMeetingApi && dotnet run --launch-profile http   # creates DB + tables + seed rooms on first start
cd RuangMeetingWeb && dotnet run --launch-profile http   # http://localhost:5151
cd RuangMeetingApi && dotnet test tests/RuangMeetingApi.Tests   # 35 tests, DB RuangMeetingDb_Test
```

SQL Server connection: `ConnectionStrings:Default` in the API's `appsettings.json`. The API key (`Api:Key`) must match in both apps. Put a real key in user-secrets or an environment variable in production. Example requests are in [`RuangMeetingApi.http`](RuangMeetingApi.http).

## Database ([`Database/RuangMeetingDb.sql`](Database/RuangMeetingDb.sql))

| Table | Columns |
|---|---|
| `RUANGAN` | RuanganId, **KodeRuangan** (unique), NamaRuangan, Lokasi, Kapasitas, IsAktif, RowVer |
| `RESERVASI` | ReservasiId, **KodeReservasi** (from a SEQUENCE), RuanganId (FK), NamaPemesan, JudulMeeting, Tanggal, JamMulai, JamSelesai, JumlahPeserta, Status (`BOOKED`/`CANCELLED`/`COMPLETED`), IdempotencyKey (unique), RowVer |

Reservations are never deleted. Cancel sets `CANCELLED`, so the history is kept. Only `BOOKED` reservations hold a time slot.

## API

| Method | Route | Notes |
|---|---|---|
| GET | `/api/ruangan?aktif=` | list rooms |
| GET/POST | `/api/ruangan/{id}`, `/api/ruangan` | |
| PUT/DELETE | `/api/ruangan/{id}` | require `If-Match: "<rowVersion>"`. DELETE only works for rooms with no reservations. |
| GET | `/api/reservasi?tanggal=&ruanganId=` | history |
| POST | `/api/reservasi` | requires `Idempotency-Key: <guid>` |
| PUT | `/api/reservasi/{id}` | requires `If-Match` |
| POST | `/api/reservasi/{id}/cancel` | requires `If-Match` |
| GET | `/api/health` | no API key needed |

Every error is RFC 7807 ProblemDetails with `correlationId`:

| Status | Meaning |
|---|---|
| 401 | missing or wrong API key |
| 404 | not found |
| 409 | overlap, duplicate code, or room still in use |
| 412 | `If-Match` holds a stale version |
| 422 | a business rule failed (`errors` lists the fields) |
| 428 | `If-Match` missing |

## Business rules

| Rule | Where it's enforced |
|---|---|
| Inactive room can't be booked | API (422) |
| Participants ≤ room capacity | API (422) |
| Start < end | API (422) + `CHECK` constraint |
| Duration 30 min to 8 hours | API (422) + `CHECK` constraint |
| No overlapping `BOOKED` reservations in one room (back-to-back is allowed) | API (409) + trigger `TR_RESERVASI_NoOverlap` |
| Can't book in the past | API (422) |
| Can't deactivate a room or cut its capacity below upcoming bookings | API (409) |

The web form shows capacity, duration and over-capacity warnings live (`wwwroot/js/reservasi.js`), but the API decides.

## Race conditions

| Scenario | Protection |
|---|---|
| Two users book overlapping slots in the same room at the same moment | Before checking for overlap, the API locks the room row with `SELECT … WITH (UPDLOCK, HOLDLOCK)`. Writers to the same room queue up, so check-then-insert is atomic. |
| Writes that bypass the API | The trigger takes the same room lock and re-checks the overlap inside SQL Server (`THROW 50010`). |
| A room is deactivated while someone books it | Deactivation takes the same room lock. Either the booking lands first (and deactivation is refused) or the deactivation lands first (and the booking is refused). |
| Double-click, resend or retry of "Save" | The `Idempotency-Key` is unique. A repeat returns the reservation that already exists (`200`, `Idempotent-Replayed: true`) instead of creating a second one. The web form also follows Post/Redirect/Get and disables its buttons on submit. |
| Two admins edit the same reservation or room | `ROWVERSION` with `If-Match`. The second writer gets 412, and the web app reloads the latest data. |
| Duplicate reservation numbers | They come from a `SEQUENCE`, never `MAX()+1`. |

`tests/RuangMeetingApi.Tests/ApiTests.cs` fires 10 to 30 simultaneous requests through the real API against the real database for each scenario. **Mutation check:**
- Without the app-level room lock, double-bookings were still stopped by the trigger. However, valid non-overlapping bookings were wrongly rejected because the trigger deadlocked, and a booking slipped into a room that was being deactivated.
- Without both the lock and the trigger, real double-bookings appeared.

## CSRF

- **Web:** `AutoValidateAntiforgeryToken` is a global filter, so every POST needs a valid token. The antiforgery and TempData cookies are `SameSite=Strict` and `HttpOnly`.
- **Security headers:** `X-Frame-Options: DENY`, and a CSP with `frame-ancestors 'none'`, `form-action 'self'` and `script-src 'self'` (no inline scripts).
- **Checked with curl:**
  - a POST without a token returns 400
  - a token without its cookie (the cross-site case) returns 400
  - a valid token is accepted
- **API:** no cookies or browser sessions, and no CORS. Each call must carry `X-Api-Key`, which is compared in constant time, and only the web server holds the key. A malicious page can't make a victim's browser send an authenticated request, so the API has no CSRF surface.

## Logging (API)

- **Serilog** (configured in `appsettings.json`), with three outputs:
  - console
  - human-readable rolling file `logs/api-YYYYMMDD.log`
  - structured JSON `logs/api-YYYYMMDD.json`, ready for Seq, ELK or Loki

  Each daily file is kept for 14 days.
- **Correlation ID:** `X-Correlation-ID` comes from the caller (the web app forwards its request id) or is generated. It is checked to contain only safe characters, attached to every log line, echoed in the response header, and included in every error body. The web app shows it to the user on server errors, so a report can be matched to the log.
- **Request log:** one line per request with method, path, status and time. 4xx responses log as Warning and 5xx as Error.
- **Business events:** created, changed and cancelled reservations; rejections with the reason (overlap with which reservation, capacity, inactive room); API-key failures with the client IP; unhandled exceptions with the stack trace.

## Differences from the mockup

- Jam Mulai and Jam Selesai use time inputs (5-minute steps) instead of fixed dropdowns.
- In the reservation form, Status is fixed to BOOKED for a new reservation and editable when editing. The third button, **Cancel**, cancels the selected reservation.
- The reservation grid has a date filter.
