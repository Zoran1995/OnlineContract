# OnlineContract

This project is an ASP.NET Core web app with a lightweight frontend (Tailwind + DaisyUI + Material Icons). Recent updates improve notifications UX, stabilize the Event Log page, and unify navbar/bell behavior across pages.

## Recent changes (Dec 2025)

# KidzStyle OnlineContract

This repository hosts a minimal ASP.NET app that serves static pages from `wwwroot` and exposes a few JSON APIs. The frontend is vanilla HTML/CSS/JS with Tailwind and DaisyUI.

## What’s new

- Pages and navigation
  - Static routes: `/login`, `/home`, `/about`, `/address`, `/eventlog`, `/products`.
  - Address and About pages render store info dynamically from the database via `/api/stores`.
  - Products page added; Home’s “Browse” links point to `/products`.
  - UI polish: centered sections, pastel notification styles, unified working-hours block.

- Store data from DB
  - New model: `Models/Store.cs` mapped to `dbo.stores`.
  - API: `GET /api/stores` returns id, name, address, phone, email, hours.
  - Frontend fetches and renders stores; Address shows two item images per store.

- Auth and account
  - Login: `POST /api/login` returns `{ success, userId, roleId }`.
  - Register: `POST /api/register` validates username/email uniqueness and creates an account.
  - Auto-login after registration: the user is signed in immediately and redirected to Home.
  - Roles added: `YourApp.Domain.Identity.UserRole` (Customer=0, Worker=1, Manager=2, Administrator=3).
  - `AxUser` has `role_id` column mapped via `RoleId` property.

- Role-based UI
  - EventLog nav link and Export CSV button are visible only to logged-in Managers or Administrators.
  - `wwwroot/js/shared.js` reads `roleId` from localStorage to toggle visibility.

- Event log
  - `GET /api/event-log` paginated feed with optional filters.
  - `GET /api/event-log/export` returns CSV.
  - Client error logging endpoint: `POST /api/log-client-error`.

## How to run

1. Build the app:
   - Task: VS Code task "build" or
   - `dotnet build --configuration Debug c:\Projects\OnlineContract\OnlineContract.csproj`

2. Run in watch mode (HTTPS profile):
   - Task: VS Code task "watch-run-https" (suppresses auto-launch) or
   - `dotnet watch run --launch-profile https --configuration Debug`

3. Navigate to pages:
   - Login: `/login` (supports `?mode=login` or `?mode=signin`)
   - Home: `/home`
   - About: `/about`
   - Address: `/address`
   - Products: `/products`
   - EventLog: `/eventlog` (requires role Manager or Administrator)

## Notes

- Tailwind/DaisyUI are loaded via CDN.
- Material Icons via Google Fonts.
- LocalStorage keys used by the UI:
  - `isLoggedIn`, `userId`, `roleId`, `lastActivityTs`.
- After registration, the app sets `isLoggedIn=true`, `userId` to the new ID, `roleId=0` (Customer) by default.
- The Address page’s top Phone/Email cards read values from the first two stores returned by `/api/stores`.
- Working hours format is split by `;` and rendered as multiple lines.

## Folder overview

- `Controllers/LogOn.cs` — minimal API endpoints and rewrites.
- `Data/AppDbContext.cs` — EF Core context and mappings.
- `Models/AxUser.cs` — user model with `role_id`.
- `Models/Store.cs` — store model.
- `Helpers/GlobalEnums.cs` — `EventType` and `UserRole` enums.
- `wwwroot/` — static frontend assets and pages.

## Removed or changed

- Old Home promo texts (“Weekend Special”, “Featured Deal”, “20% Off SUVs”) removed/replaced with value props.
- Home footer reaction icons hidden.
- About/Address hardcoded contact info removed; both now fetch from DB.

### ⚠️ Toast Notifications
- Client-side validation errors and warnings are shown as **toast bubbles**:
  - **Info** → green
  - **Warning** → orange
  - **Error** → red
- Toasts disappear after 20 seconds or on click.
- Notifications are stored in a bell panel until dismissed.

### 🛠️ Client-Side Error Logging
- Any frontend error (e.g., failed export, invalid search) is sent to backend via `/api/log-client-error`.
- Logged with the **real userId** (not system).
- Ensures full traceability of both backend and frontend issues.

## Tech Stack
- **Backend:** ASP.NET Core 10, Entity Framework Core, SQL Server
- **Frontend:** HTML5, CSS3, Vanilla JavaScript
- **Database:** SQL Server with `event_log` table and supporting views/functions

## How to Run
1. Clone the repository
2. Configure connection string in `appsettings.json`
3. Run migrations and ensure `event_log` table exists
4. Start the application:
   ```bash
   dotnet run

  ## Local changes (this workspace)

  The following list documents local edits made in this workspace that are not necessarily present in the upstream GitHub repository. These are the practical fixes and UX improvements applied while developing and testing locally.

  - Security & backend
    - Replaced SHA-256-only password hashing with PBKDF2 (Rfc2898DeriveBytes) and kept a legacy SHA-256 verification branch for migration.
    - HTML-encoded/log-sanitized descriptions and stack traces before saving to the database to prevent stored XSS.
    - Added duplicate-username checks and better validation/error responses on registration endpoints.

  - Event log & export
    - Event log API now returns event type names and user display names (not numeric ids) for the grid and CSV export.
    - Export to CSV now prevents creating a file when the grid is empty and shows a friendly warning instead.
    - Client-side date-range validation improved with friendlier messaging when `from > to`.

  - Frontend safety & notifications
    - Replaced unsafe `innerHTML` usage with DOM-safe creation + `textContent` in event log rendering and shared scripts to avoid XSS.
    - Toast notifications: prepend newest toasts at top, clamp text to two lines (no scrollbar) and use pastel color variants for info/warning/error.
    - Notifications bell: made badge overlay the bell (position relative to bell), badge is now resilient to multiple script loads.
    - Notification panel: colored notification list items by type (info/warning/error), opens directly below the bell, and supports click-away to close.
    - `shared.js` made idempotent (stores `notifications` on `window`) so reloading the script doesn't throw redeclaration errors.

  - Static assets, scripts & styles
    - Fixed parsing errors and CSS syntax issues in `wwwroot/css/login.css` (removed stray lines, added strong overrides to enforce toast position/top-right).
    - Added `favicon.svg` and linked it in HTML to eliminate browser 404 noise.
    - Ensured `shared.js` is included only once per page (removed duplicate includes in edited HTML files).

  - Other
    - Improved logging and error-handling messages to be more developer-friendly during development.

  Files changed (high level)
  - `Helpers/PasswordHelper.cs`, `Helpers/LoggerHelper.cs`
  - `Controllers/LogOn.cs` (routing tweaks, register endpoint validation, event-log projection)
  - `wwwroot/js/shared.js`, `wwwroot/js/eventlog.js`, `wwwroot/js/login.js` (DOM-safe rendering, notification behavior, export guard)
  - `wwwroot/css/login.css`, `wwwroot/css/home.css`, `wwwroot/css/eventlog.css` (toast layout, badge, color variants, clamp rules)
  - `wwwroot/eventlog.html`, other HTML files (removed duplicate script includes, linked favicon)
  - `wwwroot/favicon.svg`
