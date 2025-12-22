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

- Auth & roles
  - `POST /api/login` → `{ success, userId, roleId }`.
  - `POST /api/register` enforces unique username/email and auto‑logs in new users.
  - `Models/AxUser.cs` maps `[Column("role_id")] int RoleId`.
  - Privileged roles: Worker=6, Manager=7, Administrator=8.
  - Navbar and page gating are driven from `GET /whoami` (cookie auth) via `wwwroot/js/shared.js`.
  - Client consistently passes `userId` to `/api/stores` GET/PUT; server defaults to `2` (system) if missing.

- Event Log
  - `GET /api/event-log` with filters; `GET /api/event-log/export` CSV.
  - `EventType` values: Information=2, Warning=3, Error=4.
  - EventLog page gated on the client: non‑privileged users see “Access Denied”.
  - Filters: Search and Clear aligned to the right; pagination buttons include chevron icons.
  - Server logs full exception text, including stack traces, tagged with `userId` (fallback `2`).

- Users & Teams
  - Page: `/users` with filters and grid.
  - Filters: Name (text) + Team (dropdown). Team list is populated from `GET /api/groups?page=1&pageSize=1000` and shows all groups (`is_group = 1`).
  - Grid: Teams column displays the team’s `ax_user.Code` derived from `owner_id`. Uses `groupName` from `GET /api/users` when present, else owner mapping.
  - Toolbar: `Open`, `New User`, `New Team`, and a three‑dots menu with `Activate/Deactivate` + `Delete`.
  - Actions: `POST /api/users/{id}/activate`, `POST /api/users/{id}/deactivate`, `POST /api/users/{id}/delete`.
  - Modal: Team Code is a dropdown (blank default) matching the Team filter; selecting updates hidden `OwnerId` for the payload.
  - UX: Three‑dots menu opens on first click and closes on second; also closes on outside click and Escape.
  - Navbar: Export to CSV is hidden on Users & Teams page.

- Contracts
  - Page: `/contracts` (grid + filters + paging) styled like Users/EventLog.
  - Filters: Contract state + Customer name/code; Search/Clear buttons aligned with filters.
  - Selection: clicking a row enables the `Open` button; otherwise it stays disabled.
  - APIs:
    - `GET /api/contracts?state=&name=&page=&pageSize=` → `{ items, totalCount, totalPages }`
    - `GET /api/contracts/{id}` → single contract payload
    - `GET /api/contracts/export?state=&name=` → CSV download (`contracts.csv`)
  - Data source:
    - Reads from `dbo.contract` (`contract_id`, `input_dt`, `input_user_id`, `contract_state`, ...)
    - Only rows where `contract_id > 0` are returned/exported.
  - Access control:
    - Guests and `Customer` role are blocked with an inline “Access Denied” screen.
    - Export button shows a warning toast if the grid is empty.

- Products (privileged management)
  - Page: `/products` (grid + filters + paging) for Worker/Manager/Administrator.
  - Filters: Name + Store dropdown.
    - Store dropdown always includes “All Stores” and store names from `dbo.stores` via `GET /api/stores`.
  - Grid:
    - First column is Product ID.
    - Status is plain text (“Active” / “Inactive”), matching Users.
    - Row selection is highlighted and enables actions like Open.
  - Product Details page: `/products/{id}`
    - Uses tabs for product info, variants, and inventory.
    - Shows `ax_user.code` (not numeric user ids) for audit fields returned by the API.
    - Photo upload is optional; server validates image formats (PNG/JPG/JPEG/GIF/WEBP).
    - Upload destination: `C:\Projects\Build\InstallDocs` (only the filename is stored on the variant).
  - Auditing: create/update/activate/deactivate/delete actions are logged to `event_log`.

## Run locally

1. Build
   - VS Code task: “build”
   - Or PowerShell:
     ```powershell
     dotnet build --configuration Debug "c:\Projects\OnlineContract\OnlineContract.csproj"
     ```
2. Run
   - VS Code task: “watch-run-https” (no auto‑open)
   - Or PowerShell:
     ```powershell
     dotnet watch run --launch-profile https --configuration Debug
     ```
3. Open pages
  - `/home`, `/about`, `/address`, `/collections`, `/login`, `/eventlog`, `/users`, `/contracts`, `/products`

## Implementation notes

- Tailwind/DaisyUI via CDN; Material Icons from Google Fonts.
- LocalStorage keys: `isLoggedIn`, `userId`, `roleId`.
- Registration sets `isLoggedIn=true`, `roleId=0` (Customer) by default.
- Address page’s top Phone/Email cards use the first two stores from `/api/stores`.
- Cookie auth: API endpoints return 401 (not redirects) and require authorization. `/api/groups` is restricted to Admin/Manager.
 - EF mapping marks `dbo.stores` as having a trigger to avoid `OUTPUT` clause issues.
 - `Store.Last_Modified_User_Id` is set on PUT using the logged‑in `userId` (fallback `2`).

## Code map

- `Controllers/LogOn.cs` — endpoints (stores, event log, auth) + rewrites.
- `Data/AppDbContext.cs` — EF Core context.
- `Models/AxUser.cs` — user model with `role_id`.
 - `Models/Store.cs` — store model including `Last_Modified_User_Id`.
- `Helpers/GlobalEnums.cs` — `EventType` and `UserRole` enums.
- `wwwroot/` — static pages and assets (`home.html`, `about.html`, `address.html`, etc.).

## Cleanups

- Removed outdated Home promos and footer reaction icons.
- Removed hardcoded contact info; About/Address now fully DB‑driven.
- Public “Products” page renamed to `/collections`; privileged `/products` is the management module.

## Security & quality

- Client-side gating for EventLog (UI only); server-side checks can be added if needed.
- DOM rendering uses template strings with known data; avoid unsafe injections.
 - All API calls avoid crashes on missing `userId` via server-side defaulting.

