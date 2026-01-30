# KidzStyle · OnlineContract

> E-commerce contract management system for children's clothing retail

A full-stack web application built with ASP.NET Core backend and vanilla HTML/CSS/JavaScript frontend. The system manages products, contracts, orders, payments, users, and stores for a children's clothing retail business.

---

## Table of Contents

- [Technology Stack](#technology-stack)
- [Project Structure](#project-structure)
- [Database Schema](#database-schema)
- [API Endpoints](#api-endpoints)
- [Frontend Pages](#frontend-pages)
- [Services](#services)
- [Authentication & Authorization](#authentication--authorization)
- [Key Features](#key-features)
- [Background Jobs](#background-jobs)
- [Testing](#testing)
- [Configuration](#configuration)
- [Getting Started](#getting-started)

---

## Technology Stack

### Backend
- **Framework**: ASP.NET Core (.NET 9.0)
- **ORM**: Entity Framework Core
- **Database**: Microsoft SQL Server
- **Authentication**: Cookie-based authentication
- **Email**: MailKit/MimeKit
- **PDF Generation**: QuestPDF
- **Payment Gateway**: WSPay integration

### Frontend
- **Markup**: HTML5
- **Styling**: TailwindCSS + DaisyUI (via CDN)
- **Icons**: Google Material Icons
- **JavaScript**: Vanilla ES6+
- **Theme**: Custom pastel theme (`theme-pastel.css`)

---

## Project Structure

```
OnlineContract/
├── Controllers/           # API controllers
├── Data/                  # DbContext and repositories
├── Database/              # SQL schema and migrations
│   ├── schema.sql         # Full database schema
│   ├── seed.sql           # Initial seed data
│   └── migrations/        # Incremental migrations
├── Dtos/                  # Data Transfer Objects
├── Helpers/               # Utility classes
├── Infrastructure/        # Cross-cutting concerns
├── Models/                # Entity models
├── Services/              # Business logic services
├── Tests/                 # Unit and integration tests
├── wwwroot/               # Static files (HTML, JS, CSS, images)
│   ├── css/               # Stylesheets
│   ├── js/                # JavaScript modules
│   ├── resources/         # Static resources (icons, photos)
│   └── *.html             # HTML pages
├── appsettings.json       # Configuration
└── OnlineContract.csproj  # Project file
```

---

## Database Schema

The system uses **18 core tables**:

### User Management
| Table | Description |
|-------|-------------|
| `ax_user` | User accounts (customers, admins, managers, teams) |
| `password_reset_token` | Password reset tokens with expiration |

### Products & Inventory
| Table | Description |
|-------|-------------|
| `product` | Product catalog |
| `product_variant` | Product variants (size, color, price, photo) |
| `product_inventory` | Inventory tracking per variant per store |
| `note` | Product notes and descriptions |

### Contracts & Orders
| Table | Description |
|-------|-------------|
| `contract` | Main contract/order header |
| `contract_det` | Contract line items |
| `contract_state` | Contract workflow state transitions |
| `payment` | Payment records (WSPay integration) |

### Customer Engagement
| Table | Description |
|-------|-------------|
| `review` | Customer reviews with star ratings (1-5) |

### Supporting Tables
| Table | Description |
|-------|-------------|
| `store` | Store locations and info |
| `task_item` | Task/to-do items |
| `approval_rule` | Workflow approval rules |
| `lookup_set` | Lookup values (statuses, types) |
| `event_log` | System event logging |
| `scheduler_process` | Background job scheduling |

### Key Relationships
- `contract` → `contract_det` (1:N) - Order items
- `product` → `product_variant` (1:N) - Size/color variants
- `product_variant` → `product_inventory` (1:N) - Per-store stock
- `contract` → `contract_state` (1:N) - Workflow history
- `ax_user.owner_id` → `ax_user.ax_user_id` - Team membership

---

## API Endpoints

### Authentication (`/api`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/login` | User login (cookie auth) |
| POST | `/api/logout` | User logout |
| POST | `/api/register` | New user registration |
| GET | `/api/session` | Get current session info |
| POST | `/api/users/change-temp-password` | Change temporary password |
| POST | `/api/password/request-reset` | Request password reset |
| POST | `/api/password/reset` | Reset password with token |

### Users (`/api/users`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/users` | List users (paginated, filterable) |
| GET | `/api/users/{id}` | Get user by ID |
| POST | `/api/users` | Create user |
| PUT | `/api/users/{id}` | Update user |
| PUT | `/api/users/{id}/activate` | Activate user |
| PUT | `/api/users/{id}/deactivate` | Deactivate user |
| DELETE | `/api/users/{id}` | Soft delete user |

### Groups/Teams (`/api/groups`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/groups` | List active teams |

### Products (`/api/products`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/products` | List products (paginated) |
| GET | `/api/products/{id}` | Get product details |
| POST | `/api/products` | Create product |
| PUT | `/api/products/{id}` | Update product |
| DELETE | `/api/products/{id}` | Soft delete product |
| GET | `/api/products/cards` | Product cards for catalog (with filters) |
| GET | `/api/products/filter-options` | Get filter dropdown values |

### Product Variants (`/api/products/{id}/variants`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/products/{id}/variants` | List variants for product |
| POST | `/api/products/{id}/variants` | Create variant |
| PUT | `/api/products/{id}/variants/{variantId}` | Update variant |
| DELETE | `/api/products/{id}/variants/{variantId}` | Delete variant |
| POST | `/api/variants/{id}/upload-photo` | Upload variant photo |

### Contracts (`/api/contracts`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/contracts` | List contracts (paginated) |
| GET | `/api/contracts/{id}` | Get contract details |
| POST | `/api/contracts` | Create contract |
| PUT | `/api/contracts/{id}` | Update contract |
| DELETE | `/api/contracts/{id}` | Soft delete contract |
| GET | `/api/contracts/export` | Export to CSV |
| POST | `/api/contracts/{id}/submit` | Submit draft contract |
| POST | `/api/contracts/{id}/cancel` | Cancel contract |

### Contract Items (`/api/contracts/{id}/items`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/contracts/{id}/items` | List contract items |
| POST | `/api/contracts/{id}/items` | Add item to contract |
| PUT | `/api/contracts/{id}/items/{itemId}` | Update item |
| DELETE | `/api/contracts/{id}/items/{itemId}` | Remove item |
| PUT | `/api/contracts/{id}/items/{itemId}/workflow` | Update item workflow status |

### Shopping Cart (`/api/cart`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/cart` | Get user's cart |
| POST | `/api/cart/add` | Add item to cart |
| PUT | `/api/cart/{itemId}` | Update cart item quantity |
| DELETE | `/api/cart/{itemId}` | Remove cart item |
| POST | `/api/cart/checkout` | Checkout cart → create contract |

### Anonymous Cart (`/api/anon-cart`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/anon-cart` | Get anonymous cart (by session) |
| POST | `/api/anon-cart/add` | Add to anonymous cart |
| PUT | `/api/anon-cart/{itemId}` | Update anonymous cart item |
| DELETE | `/api/anon-cart/{itemId}` | Remove from anonymous cart |

### Payments (`/api/payments`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/payments` | List payments |
| GET | `/api/payments/{id}` | Get payment details |
| POST | `/api/payments/initiate` | Initiate WSPay payment |
| POST | `/api/payments/callback` | WSPay callback handler |

### Tasks (`/api/tasks`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/tasks` | List tasks (paginated, filterable) |
| GET | `/api/tasks/{id}` | Get task details |
| POST | `/api/tasks` | Create task |
| PUT | `/api/tasks/{id}` | Update task |
| DELETE | `/api/tasks/{id}` | Delete task |
| PUT | `/api/tasks/{id}/status` | Update task status |

### Notes (`/api/notes`, `/api/products/{id}/notes`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/notes` | List all notes |
| GET | `/api/products/{id}/notes` | List product notes |
| POST | `/api/products/{id}/notes` | Add product note |
| PUT | `/api/notes/{id}` | Update note |
| DELETE | `/api/notes/{id}` | Delete note |
| PUT | `/api/notes/{id}/set-main` | Set as main note |

### Stores (`/api/stores`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/stores` | List stores |
| GET | `/api/stores/{id}` | Get store details |
| PUT | `/api/stores/{id}` | Update store |

### Event Log (`/api/event-log`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/event-log` | List events (paginated, filterable) |
| GET | `/api/event-log/export` | Export to CSV |

### Profile (`/api/profile`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/profile` | Get current user profile |
| PUT | `/api/profile` | Update profile |

### Processes (`/api/processes`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/processes` | List scheduler processes |
| PUT | `/api/processes/{id}` | Update process schedule |
| POST | `/api/processes/{id}/run` | Run process manually |

### Approval Rules (`/api/approval-rules`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/approval-rules` | List approval rules |
| POST | `/api/approval-rules` | Create rule |
| PUT | `/api/approval-rules/{id}` | Update rule |
| DELETE | `/api/approval-rules/{id}` | Delete rule |

### Reviews (`/api/reviews`)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/reviews` | List reviews (paginated, public) |
| GET | `/api/reviews/summary` | Get review statistics (avg rating, count) |
| POST | `/api/reviews` | Create review (auth optional, 1-5 stars) |
| POST | `/api/reviews/{id}/delete` | Soft delete review (admin only) |

---

## Frontend Pages

### Public Pages (No Authentication Required)
| Page | Path | Description |
|------|------|-------------|
| Home | `/home` | Landing page with hero, featured products |
| About | `/about` | Store information cards |
| Address | `/address` | Store locations and contact |
| Collections | `/collections` | Product catalog with filters and pagination |
| Product Details | `/product-details` | Single product view with variants |
| Login | `/login` | User authentication |
| Register | `/register` | New user registration |
| Reset Password | `/reset-password` | Password reset flow |

### Authenticated Pages
| Page | Path | Role | Description |
|------|------|------|-------------|
| Profile | `/profile` | All | User profile management |
| Contracts | `/contracts` | All | View own contracts |
| Contract Details | `/contract-details` | All | Single contract view |
| Cart | `/cart` | All | Shopping cart |
| Checkout | `/checkout` | All | Order checkout |

### Admin/Manager Pages
| Page | Path | Role | Description |
|------|------|------|-------------|
| Users | `/users` | Admin/Manager | User management grid |
| Products | `/products` | Admin/Manager | Product management grid |
| Product Edit | `/product-edit` | Admin/Manager | Add/edit product |
| Tasks | `/tasks` | Admin/Manager | Task management |
| Event Log | `/eventlog` | Admin/Manager | System event viewer |
| Change Store | `/changestore` | Admin | Edit store information |
| Processes | `/processes` | Admin | Background job management |

---

## Services

### Cart Services
| Service | Description |
|---------|-------------|
| `CartService` | Authenticated user cart operations |
| `AnonCartCacheService` | In-memory anonymous cart storage |
| `CartMergeService` | Merge anonymous cart on login |

### Payment Services
| Service | Description |
|---------|-------------|
| `PaymentService` | Payment creation and status tracking |
| `WspayClient` | WSPay gateway integration |

### Product Services
| Service | Description |
|---------|-------------|
| `ProductQueryService` | Product card queries with filtering |
| `VariantPhotoService` | Photo upload and management |

### Background Job Services
| Service | Description |
|---------|-------------|
| `EomReportService` | End-of-month PDF report generation |
| `EomReportScheduler` | EOM report scheduling (IHostedService) |
| `DraftContractPurgeService` | Purge old draft contracts |
| `DraftContractPurgeScheduler` | Draft purge scheduling (IHostedService) |

### Other Services
| Service | Description |
|---------|-------------|
| `EmailService` | Email sending via SMTP (MailKit) |
| `NotificationService` | User notifications |
| `ContractWorkflowService` | Contract state transitions |
| `EventLogService` | System event logging |

---

## Authentication & Authorization

### Roles
| Role ID | Name | Permissions |
|---------|------|-------------|
| 1 | Admin | Full system access |
| 2 | Manager | User/product/contract management |
| 3 | User | Own profile and contracts |
| 4 | Guest | Read-only public pages |

### Authentication Flow
1. User submits credentials to `POST /api/login`
2. Server validates credentials against `ax_user` table
3. On success, creates authentication cookie
4. If `is_temp_password = true`, returns `mustChangePassword: true`
5. Client shows password change modal if needed
6. Session info available via `GET /api/session`

### Special Cases
- **Team accounts** (`is_group = true`): Cannot sign in directly
- **Deactivated users** (`is_active = false`): Login blocked with message
- **Temporary passwords**: Forced change on first login

---

## Key Features

### 1. Product Catalog
- Hierarchical products with variants (size, color)
- Photo upload for variants
- Price per variant
- Inventory tracking per store
- Product notes (main note displayed on cards)
- **Filters**: Size, Color, Price range, Sort order
- **Pagination**: 10/20/50 items per page

### 2. Shopping Cart
- Authenticated user cart (persisted in DB)
- Anonymous cart (session-based, in-memory)
- Cart merge on login
- Checkout creates contract in Draft status

### 3. Contract Management
- **Statuses**: Draft → Submitted → In Progress → Shipped → Delivered → Cancelled
- Line items with quantity and variant selection
- Workflow state history
- CSV export
- **Draft Purge**: Automatic cleanup of old drafts

### 4. Payment Integration (WSPay)
- Payment initiation with redirect to gateway
- Callback handling for success/failure/cancel
- Payment status tracking

### 5. Task Management
- Task creation with subject, comments, due date
- Priority levels (Low, Medium, High, Critical)
- Status tracking (New, In Progress, Completed, Cancelled)
- Assignment to users
- Filtering by status, priority, date range

### 6. User Management
- User CRUD operations
- Role assignment
- Team/group membership
- Password policies (8+ chars, uppercase, digit)
- Temporary password forcing
- Account activation/deactivation

### 7. Store Management
- Multiple store locations
- Working hours configuration
- Contact information
- Store-specific inventory

### 8. Reporting
- End-of-month PDF reports
- Event log with filtering and export
- Contract export to CSV

### 9. Customer Reviews
- Star rating system (1-5 stars)
- Public review listing on home page
- Anonymous and authenticated users can submit reviews
- Review summary with average rating and total count
- Admin moderation (soft delete functionality)
- Interactive star rating UI with hover effects

---

## Background Jobs

### EOM Report Scheduler
- **Schedule**: Runs monthly on the 1st at 00:00 (configurable via `scheduler_process`)
- **Function**: Generates PDF reports for the previous month
- **Output**: Report files stored in configured location
- **Logging**: Comprehensive event logging to `event_log` table

### Draft Contract Purge Scheduler
- **Schedule**: Runs daily at 03:00 (configurable)
- **Function**: Deletes draft contracts older than threshold
- **Threshold**: Configurable days (default: 30)
- **Logging**: Comprehensive event logging to `event_log` table

### Scheduler Event Logging

All scheduler processes write detailed logs to the `event_log` table for production debugging:

| Event Type | Logged Scenarios |
|------------|------------------|
| **Information** | Scheduler started, execution triggered, scheduler stopped |
| **Warning** | Process deactivated (skipped run), another instance already running |
| **Error** | Process not found in DB, unexpected errors in scheduler loop |

**Log Format**: `[Scheduler] Process Name - Description. Reason: Detailed explanation.`

**Manual Run Logging**: When a user triggers a process manually via `/api/processes/{id}/run`, the system logs:
- Manual execution initiated (with user ID)
- Blocked executions (with reason: pending task exists, process already running)

**Example Log Entries**:
```
[Scheduler] EOM Report Generation - Scheduled execution triggered at 2026-01-01 00:00:15. Mode: Scheduled.
[Scheduler] Draft Contract Purge - SKIPPED: Process is deactivated (is_active = 0). Reason: An administrator has deactivated this process.
[Scheduler] EOM Report Generation - Manual execution triggered by user ID 5.
[Scheduler] Draft Contract Purge - Manual execution BLOCKED by user ID 3. Reason: Another instance is already running.
```

---

## Testing

The project includes comprehensive tests in the `/Tests` folder:

### Test Categories
- **Unit Tests**: Service and helper tests
- **Integration Tests**: API endpoint tests
- **Controller Tests**: HTTP response validation

### Test Files
```
Tests/
├── Api/                    # API integration tests
│   ├── AuthControllerTests.cs
│   ├── CartControllerTests.cs
│   ├── ContractsControllerTests.cs
│   └── ...
├── Services/               # Service unit tests
│   ├── CartServiceTests.cs
│   ├── PaymentServiceTests.cs
│   └── ...
└── Helpers/                # Helper unit tests
```

### Running Tests
```bash
dotnet test
```

---

## Configuration

### appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=OnlineContract;..."
  },
  "Email": {
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "FromAddress": "noreply@example.com"
  },
  "WSPay": {
    "ShopId": "...",
    "SecretKey": "...",
    "FormUrl": "https://formtest.wspay.biz/Authorization.aspx"
  }
}
```

### Environment Variables
- `ASPNETCORE_ENVIRONMENT`: Development/Production
- `ConnectionStrings__DefaultConnection`: Database connection override

---

## Getting Started

### Prerequisites
- .NET 9.0 SDK
- SQL Server (or SQL Server Express/LocalDB)
- Node.js (for TailwindCSS build, optional)

### Setup
1. Clone the repository
2. Create database and run `Database/schema.sql`
3. Optionally run `Database/seed.sql` for sample data
4. Update connection string in `appsettings.json`
5. Run the application:
   ```bash
   dotnet run
   ```
6. Open browser at `https://localhost:5001` or `http://localhost:5000`

### Development
```bash
# Build
dotnet build

# Run with hot reload
dotnet watch run

# Run tests
dotnet test

# Build TailwindCSS (if modified)
npm run build:css
```

---

## Recent Updates (January 2026)

### Customer Reviews Feature
- New `review` table in database schema
- REST API for reviews (list, summary, create, delete)
- Interactive star rating UI on home page
- Support for anonymous and authenticated reviews
- Review summary showing average rating and total count
- Admin moderation with soft delete
- 21 test cases covering all review scenarios

### Scheduler Logging Improvements
- Comprehensive event logging for all scheduler processes
- Detailed logging for skipped runs (deactivated process, already running)
- Manual run trigger logging with user ID
- Blocked execution logging with detailed reasons
- Error logging with stack traces for debugging
- All logs written to `event_log` table with `[Scheduler]` prefix

### Collections Page Improvements
- Modern pastel UI design with glassmorphism effects
- Working filters: Size, Color, Price slider, Sort order
- Pagination with 10/20/50 items per page
- Placeholder image for missing product photos
- Consistent navbar with other pages

### UTF-8 Collation Support
- Extended `Latin1_General_100_CI_AS_SC_UTF8` collation to text columns
- Supports Serbian Latin characters (č, ć, đ, š, ž)

### Draft Contract Purge
- Background scheduler for automatic draft cleanup
- Configurable retention period
- Manual trigger available via admin UI

### Task Management Enhancements
- Improved filter dropdowns width
- Status update modal fixes

---

## License

Proprietary - All rights reserved.

---

## Contact

For support or inquiries, please contact the development team.
