# KidzStyle · OnlineContract

Minimal ASP.NET Core app serving static pages from `wwwroot` with a few JSON APIs. Frontend is vanilla HTML/CSS/JS using TailwindCSS and DaisyUI via CDN.

## Key Features (Dec 2025)

- Pages & navigation
  - Routes: `/home`, `/about`, `/address`, `/collections`, `/eventlog`, `/login`.
  - Privileged product management: `/products` (grid) and `/products/{id}` (details).
  - About & Address pull store info from DB via `GET /api/stores`.
  - Home “Browse” goes to `/collections`.
  - Admin-only change store modal lives at route `/changestore`.

- Store data from DB
  - `Models/Store.cs` mapped in EF Core.
  - API `GET /api/stores` returns: `{ id, name, address, phone, email, hours }`.
  - About renders store cards; Address shows store details + two item images per store.
  - Working hours are split by `;` and shown line-by-line.
  - Stores are sorted by `StoreId` server-side; client defensively sorts.

- UI/UX updates
  - Working-hours blocks: full width, fixed height, centered content.
  - About/Address store panels: fixed equal height, elongated and centered.
  - Pastel notification styling; unified navbar behavior.
  - Change Store modal opens empty; fields populate after store selection.
  - Working-hours editor uses `input type="time"` with 30‑minute increments; ranges normalized and validated.

# OnlineContract — README

This repository contains a minimal ASP.NET Core web application that serves static pages from `wwwroot` together with a set of JSON APIs backed by Entity Framework Core. The frontend is plain HTML/CSS/JavaScript (TailwindCSS + DaisyUI via CDN) and the server is implemented as a minimal API in `Controllers/Program.cs`.

This README documents the full set of features and the recent functional changes and fixes applied across the backend and frontend (authentication, users/teams, stores, exports, timestamps, UI/UX improvements, and validation fixes).

---

## High-level summary of recent changes

- Login & authentication
  - Added clearer error messages for login failures (invalid credentials, server error).
  - Detect and return a friendly message when a valid credential pair belongs to a deactivated user: "Your account has been deactivated. If you need it reactivated, please contact our administrator.".
  - Block team/group accounts from signing in (when `ax_user.IsGroup == true`) — team accounts cannot be used to authenticate. Message returned: "Team accounts cannot be used to sign in. Please use a personal account or contact your administrator for access.".
  - Persist `LastLoginDt` (server local time) on successful sign-in.

- User create/update behaviour
  - Create (`POST /api/users`) enforces required fields (email, phone, password for non-group users) and sets audit timestamps.
  - Update (`PUT /api/users/{id}`) enforces email/phone and optimistic concurrency via `Stamp`.
  - Password update validation changed: the server only validates and applies a password when the client explicitly intends to change it. Client placeholder values (e.g. `••••••••`) are treated as "no change"; if the user clears the password field (sends empty string), server validation will enforce it (password required).
  - Success/failure responses standardized: update, activate/deactivate/delete now return descriptive messages (e.g. "User has been updated successfully.").

- Users & Teams dropdown and modal fixes
  - `GET /api/groups` now returns only groups where `IsGroup == true`, `IsActive == true`, and `IsDeleted == false` so dropdowns and filters only show active teams.
  - The user edit modal hides or excludes deactivated/ deleted teams from the Team dropdown.

- Exports and CSV
  - `GET /api/event-log/export` and `GET /api/contracts/export` return CSV files named with a timestamp suffix (e.g. `EventLog_2025-12-28_14-23-02.csv`).
  - Event type mapping fixed: UI numeric values (1,2,3) map to DB event types (2,3,4) for queries and exports.
  - Date filtering for exports respects the full datetime value (time part included) when parsing `from`/`to` parameters.
  - CSV output uses conservative escaping (quotes when needed and doubled internal quotes) to avoid malformed rows.

- Timestamp handling
  - When creating users and updating password or last-login, timestamps are set using server local time (`DateTime.Now`) to match how timestamps are viewed in the database and avoid a 1-hour offset issue previously reported.

- UI / Frontend improvements
  - New Change Store page and JavaScript to match Users grid UX (selection, Open, pagination, zebra rows).
  - Store modal fetches fresh data (cache-busting) and triggers the stores grid to refresh after save.
  - Notifications and toast behavior improved: user-friendly messages and the notification bell shows newest notifications first.
  - User dropdown now closes on outside click and Escape.
  - EventLog page: pressing Enter in filter fields triggers search.
  - Users modal: team dropdown populated from `GET /api/groups` and maps to `OwnerId`; password field displays a placeholder (`••••••••`) for unchanged passwords.

- Client fixes for password/update flow
  - The frontend `wwwroot/js/users.js` now sends the `Password` field when the user clears it (empty string) so the server can validate an explicit clear action.
  - When the placeholder `••••••••` is present, the client does not send that as a password change.

- Forced password change (new `is_temp_password` flow)
  - DB & model: the `ax_user.is_temp_password` BIT column is mapped to `AxUser.IsTempPassword` so the server and client can coordinate forced changes.
  - Admin UI: the Users modal now includes a checkbox **User must change password at next logon** (`uIsTempPassword`) which admins/managers can set on create or update. The checkbox is only persisted when explicitly changed by the admin.
  - Login flow: if a user with `is_temp_password = 1` attempts to sign in, the login API returns `{ mustChangePassword:true, stamp }` and the client opens a modal forcing the user to set a new password before continuing.
  - Change endpoint: added `POST /api/users/change-temp-password` which accepts `{ code, newPassword, confirmPassword, stamp }`, validates the password policy, prevents reusing the current password, enforces optimistic concurrency (`stamp`), clears `is_temp_password`, updates `password_dt` and `last_login_dt` to server local time, increments `stamp`, and signs the user in.
  - Validations & UX: the modal enforces: non-empty passwords, 8+ characters, at least one uppercase, at least one digit, matching confirmation, and clear messages on failure. Concurrency conflicts return a clear retry message.

- Messages and logging
  - Many server responses were expanded into full-sentence messages for better UX.
  - Event logging remained intact — server logs include full exception stacks for diagnostics.

---

## APIs (not exhaustive) — behavior highlights

- POST /api/login
  - Validates credentials. If successful and the user is active and not a group account, signs in using cookie auth and returns `{ success:true, userId, roleId }`.
  - Returns clear messages for: invalid code, invalid password, deactivated account, team-account sign-in attempt, or server error.

- POST /api/register
  - Creates a new user and sets `PasswordDt` and `CreatedDt` timestamps; returns `userId` and `roleId` on success.

- GET /api/groups?page=1&pageSize=1000
  - Returns only active, not-deleted groups for dropdowns.

- GET /api/users
  - Returns paged list of users with `groupName`, `stamp`, `isActive`, etc.

- PUT /api/users/{id}
  - Validates `Stamp` for optimistic concurrency.
  - Validates email/phone presence and format.
  - Password logic:
    - If the client does not include a `Password` field at all, password is not changed.
    - If the client includes `Password` with the placeholder value (`••••••••` or `********`), server treats this as "no change" and leaves the password untouched.
    - If the client includes an empty string for `Password`, server treats this as an explicit attempt to set/clear the password and validation applies (empty is rejected with an explanatory message).

- POST /api/users/{id}/deactivate, activate, delete
  - Perform the action, increment `Stamp`, write event log, and return a descriptive success message.

- GET /api/event-log/export and GET /api/contracts/export
  - Accept filters, respect exact datetime `from`/`to` values, and generate a CSV file with a timestamped filename.

---

## Frontend notes & pages

- Users page (`wwwroot/users.html` + `wwwroot/js/users.js`)
  - Grid selection, Open button enable/disable, team dropdown sourced from `/api/groups`.
  - Modal behavior:
    - Placeholder password displayed for unchanged passwords.
    - If user clears password input and saves, the client sends `Password: ""` so server validation will enforce requirements.

- Change Store page (`wwwroot/changestore.html` + `wwwroot/js/changestore.js`)
  - Mirrors Users grid behaviour and exposes `window.refreshStores()` for the modal to call after save.

- Shared utilities (`wwwroot/js/shared.js`)
  - Toasts, notification bell (newest-first), user dropdown handling, and logout helper.

---

## Timestamp & timezone handling

To resolve earlier observed 1-hour offsets in `ax_user.PasswordDt` and `ax_user.LastLoginDt`, timestamps for creation/password updates/last-login are now captured using the server local time (`DateTime.Now`). If you prefer storing UTC times, convert to `DateTimeOffset.UtcNow` and ensure the DB column type preserves offsets.

---

## How to build and test locally

1. Build

```powershell
dotnet build --configuration Debug "c:\Projects\OnlineContract\OnlineContract.csproj"
```

2. Run

```powershell
dotnet run --project "c:\Projects\OnlineContract\OnlineContract.csproj"
```

3. Quick verification scenarios

- Login as admin (example): `admin / passw0rd` → should sign in normally.
- Attempt to sign in with a team account (`ax_user.IsGroup = 1`) → you should receive: "Team accounts cannot be used to sign in. Please use a personal account or contact your administrator for access.".
- Edit an existing user without changing password → open Users, select a user, open modal, do not modify the password input, click Save → update succeeds.
- Clear the password field in the modal (delete characters so it becomes empty) and Save → server will validate and return an explanatory error if empty (password required for that update).
- Attempt to save a user when Stamp mismatch occurs → server returns a conflict message instructing to refresh.
- Export EventLog with type and exact datetimes → returned CSV contains only filtered rows and filename includes timestamp.

---

## Files & areas to inspect for related logic

- Server: `Controllers/Program.cs` (login, users, groups, stores, exports, timestamps) — primary place for recent fixes.
- Client: `wwwroot/js/users.js`, `wwwroot/js/shared.js`, `wwwroot/js/changestore.js`, `wwwroot/js/eventlog.js`, `wwwroot/js/contracts.js`.
- Models: `Models/AxUser.cs`, `Models/NoteDtos.cs`, `Models/ProductDtos.cs`.

---

If you want, I can also:

- run the app here and exercise the admin/user/team scenarios;
- add automated integration tests for the user update and login cases;
- or revert timestamp handling to UTC instead of local server time.

Work tracked: updated README with comprehensive changelog and usage notes.

