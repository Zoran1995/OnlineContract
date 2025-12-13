# OnlineContract

This project is an ASP.NET Core web app with a lightweight frontend (Tailwind + DaisyUI + Material Icons). Recent updates improve notifications UX, stabilize the Event Log page, and unify navbar/bell behavior across pages.

## Recent changes (Dec 2025)

- Notifications
  - Bell is always visible across pages: injected into the navbar when present, otherwise fixed top-right.
  - Badge shows the count and hides when it’s 0, consistent site-wide.
  - Toasts appear strictly below the navbar and never block clicks; position recalculates on resize and scroll.
  - Notification panel stays anchored under the bell; width locks while open to prevent shifting.
  - Panel controls added: thin “Dismiss all” and type filters (Information/Warning/Error) that filter the list.
  - Filter resets automatically when the panel closes, showing all messages next time.
  - Per-item dismiss works correctly with filtering; notifications persist across redirects (localStorage).

- Event Log page (`wwwroot/eventlog.html` + `wwwroot/js/eventlog.js` + `wwwroot/css/eventlog.css`)
  - Removed duplicate HTML document and redundant navbar/auth scripts; single sticky navbar remains.
  - Export button includes a download icon; Search and Clear buttons now include icons.
  - Toasts are positioned below the navbar/bell/export button boundary so they never overlap header controls.
  - Layout cleaned and consistent with other pages; notification bell lives in the navbar’s right controls.

- Shared behavior (`wwwroot/js/shared.js`)
  - Robust bell injection with fallbacks; never hidden, high z-index for visibility.
  - Toast container uses pointer-events: none and per-toast pointer-events: auto to avoid blocking navbar.
  - Computes toast top from the maximum bottom of navbar/bell/export (aligns to boundary); recalculates on scroll/resize.
  - Dismiss-all and per-item dismiss update the bell and persist to localStorage.

- Validation & auth (summary of prior work)
  - Client/server email validation added in login/register flows.
  - Optional address fields added: city, street_address, postal_code (5-digit validation).
  - Global logout redirects to `/login`; inactivity auto-logout after 30 minutes.

## How to run

1. Build
   - Run `dotnet build` from the repository root.
2. Run
   - Run `dotnet run` to start the app, then open the reported URL in a browser.

## Files of interest

- `wwwroot/js/shared.js`: Bell, badge, toast, panel logic; navbar auth; inactivity logout; filter controls.
- `wwwroot/eventlog.html`: Single sticky navbar; filters/grid/pagination; export button with icon.
- `wwwroot/js/eventlog.js`: Event Log interactions; icons for Search/Clear; export handler.
- `wwwroot/css/eventlog.css`: Page styling; allows JS to control toast top (no `!important` overrides).
- `wwwroot/home.html`, `wwwroot/login.html`, `wwwroot/about.html`, `wwwroot/address.html`: Pages using shared navbar + bell.

## Images: local photo integration

All pages now reference local JPG photos stored under `wwwroot/resources/photos/` to avoid broken images and external dependencies.

Expected filenames (drop your real photos using these names):
- hero-scenic.jpg
- login-illustration.jpg
- gallery-1.jpg, gallery-2.jpg, gallery-3.jpg
- car-compact.jpg, car-suv.jpg, car-luxury.jpg
- city-downtown.jpg, city-park.jpg, city-coffee.jpg

Recommended resolutions:
- Hero banner: 1600×500–600 (wide)
- Cards/galleries/vehicles/cities: ~1280×720 or 800×500 (landscape)

Styling notes:
- Images are rendered with `object-cover` and rounded corners to fit neatly without distortion.
- There is no placeholder fallback; the app displays only concrete images you provide under `wwwroot/resources/photos/`.

Adding more photos:
- Place additional JPGs under `wwwroot/resources/photos/` and update the corresponding page to point to them.
- Prefer appropriately compressed JPGs for performance.

## Notes

- If the navbar markup changes, the bell auto-relocates to `.navbar` or falls back to fixed top-right.
- Toast spacing from the top boundary can be tuned; currently aligned to the exact lower edge (+1px).

# OnlineContract Application

This project is an ASP.NET Core application with a modern frontend (HTML/CSS/JS) designed to handle user authentication, event logging, and error tracking.

## Features Implemented

### 🔐 Authentication
- Custom login endpoint with secure password hashing.
- User ID stored in `localStorage` after successful login.
- Failed login attempts are logged as **Warning** events.

### 📑 Event Logging
- Centralized logging of all errors and warnings into the `event_log` table.
- Each log entry includes: EventType, DateTime, Description, User, and full StackTrace.
- Global error handler middleware ensures unhandled exceptions are logged.

### 📊 Event Log Grid
- Displays event logs with filtering by **EventType** and date range.
- New **StackTrace** column added for detailed debugging.
- Client-side validation: prevents invalid date ranges (`from > to`).
- Pagination with selectable page size (10, 20, 50, 100).
- Export to CSV with properly aligned headers and ISO DateTime format.

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
