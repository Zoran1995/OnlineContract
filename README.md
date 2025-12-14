# KidzStyle · OnlineContract

Minimal ASP.NET Core app serving static pages from `wwwroot` with a few JSON APIs. Frontend is vanilla HTML/CSS/JS using TailwindCSS and DaisyUI via CDN.

## Key Features (Dec 2025)

- Pages & navigation
  - Routes: `/home`, `/about`, `/address`, `/products`, `/eventlog`, `/login`.
  - About & Address pull store info from DB via `GET /api/stores`.
  - Home “Browse” goes to `/products`.
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
  - Privileged roles: Administrator=7, Manager=8. UI checks `localStorage.roleId`.
  - Client consistently passes `userId` to `/api/stores` GET/PUT; server defaults to `2` (system) if missing.

- Event Log
  - `GET /api/event-log` with filters; `GET /api/event-log/export` CSV.
  - `EventType` values: Information=2, Warning=3, Error=4.
  - EventLog page gated on the client: non‑privileged users see “Access Denied”.
  - Server logs full exception text, including stack traces, tagged with `userId` (fallback `2`).

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
   - `/home`, `/about`, `/address`, `/products`, `/login`, `/eventlog`

## Implementation notes

- Tailwind/DaisyUI via CDN; Material Icons from Google Fonts.
- LocalStorage keys: `isLoggedIn`, `userId`, `roleId`.
- Registration sets `isLoggedIn=true`, `roleId=0` (Customer) by default.
- Address page’s top Phone/Email cards use the first two stores from `/api/stores`.
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
- Products routing and links updated.

## Security & quality

- Client-side gating for EventLog (UI only); server-side checks can be added if needed.
- DOM rendering uses template strings with known data; avoid unsafe injections.
 - All API calls avoid crashes on missing `userId` via server-side defaulting.

