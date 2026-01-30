# OnlineContract - Comprehensive Documentation

## Table of Contents
1. [Project Overview](#project-overview)
2. [Technology Stack](#technology-stack)
3. [Database Schema](#database-schema)
4. [API Endpoints](#api-endpoints)
5. [Frontend Pages](#frontend-pages)
6. [Services](#services)
7. [Authentication & Authorization](#authentication--authorization)
8. [Key Features](#key-features)
9. [Data Transfer Objects (DTOs)](#data-transfer-objects-dtos)
10. [Helper Classes](#helper-classes)
11. [Testing](#testing)

---

## Project Overview

**OnlineContract** is a full-stack e-commerce contract management system built with ASP.NET Core. It serves as an online platform for managing contracts, products, users, stores, payments, and tasks. The application supports both customers and administrative users with role-based access control.

### Key Capabilities
- **Contract Management**: Create, track, and manage customer contracts through various states (Draft → Submitted → Accepted → Delivered, etc.)
- **Product Catalog**: Browse products with variants (size, color), inventory tracking across multiple stores
- **Shopping Cart**: Both authenticated and anonymous cart functionality with session bridging
- **Payment Processing**: Integration with WSPay payment gateway for online payments
- **User Management**: Multi-role system with customers, workers, managers, and administrators
- **Task Management**: Workflow tasks with approval rules for write-offs and refunds
- **Reporting**: End-of-Month (EOM) reports with PDF generation
- **Store Management**: Multiple store locations with inventory per store

---

## Technology Stack

### Backend
| Component | Technology |
|-----------|------------|
| Framework | ASP.NET Core (.NET 9.0) |
| Database | Microsoft SQL Server |
| ORM | Entity Framework Core 9.0 |
| Authentication | Cookie-based Authentication |
| Email | MailKit 4.1.0 (SMTP via Gmail) |
| PDF Generation | QuestPDF 2024.10.2 |
| Testing | SQLite (In-Memory) |

### Frontend
| Component | Technology |
|-----------|------------|
| UI Framework | Vanilla HTML/CSS/JavaScript |
| CSS Framework | TailwindCSS + DaisyUI (via CDN) |
| Build Tools | PostCSS, TailwindCSS config |

### Infrastructure
| Component | Technology |
|-----------|------------|
| Session Storage | Distributed Memory Cache (Redis-ready) |
| Data Protection | ASP.NET Core Data Protection (file-based keys) |
| Background Jobs | IHostedService (EOM Report, Draft Purge) |

---

## Database Schema

### Core Tables

#### Users & Authentication
| Table | Description |
|-------|-------------|
| `ax_user` | Users (customers, workers, managers, admins) and groups/teams |
| `password_reset_token` | Password reset tokens with expiry |

**ax_user columns:**
- `ax_user_id`, `first_name`, `last_name`, `code` (username), `password` (hashed)
- `email`, `phone_number`, `city`, `street_address`, `postal_code`
- `role_id` (5=Customer, 6=Worker, 7=Manager, 8=Administrator)
- `is_group` (true for teams), `owner_id` (team membership)
- `is_active`, `is_deleted`, `is_temp_password`, `stamp`

#### Products & Inventory
| Table | Description |
|-------|-------------|
| `product` | Base product information |
| `product_variant` | Product variants (size, color, price, photo) |
| `product_inventory` | Inventory quantities per variant per store |
| `store` | Store locations |

**product_variant columns:**
- `product_variant_id`, `product_id`, `size`, `color`, `amount` (price)
- `photo_file_name`, `is_active`, `is_deleted`, `stamp`

#### Contracts & Orders
| Table | Description |
|-------|-------------|
| `contract` | Order header (customer, state, amounts) |
| `contract_det` | Order line items (variant, quantity, price) |
| `contract_state` | State machine configuration |
| `contract_state_transition` | Valid state transitions |

**contract columns:**
- `contract_id`, `input_user_id` (customer), `contract_state`
- `amount`, `amt_matched` (paid amount)
- `delivered_dt`, `written_off_dt`, `rejected_dt`, `cancelled_dt`
- `stamp`, `is_active`, `is_deleted`

**Contract States:** Draft(9) → Submitted(10) → Accepted(11) → PartiallyAccepted(12) → Rejected(13) → InProgress(14) → Completed(15) → Dispatched(16) → Delivered(17) → Returned(18) → Cancelled(19) → WrittenOff(20) → Refunded(21)

#### Payments
| Table | Description |
|-------|-------------|
| `payment` | Payment records linked to contracts |

**payment columns:**
- `payment_id`, `contract_id`, `provider` (WSPay)
- `external_order_id`, `amt_gross`, `currency`, `status`
- `transaction_id`, `last_callback_dt`, `last_callback_status`

#### Tasks & Approvals
| Table | Description |
|-------|-------------|
| `task` | Workflow tasks (approval, reminders) |
| `approval_rule` | Business rules for write-offs, refunds |

**task columns:**
- `task_id`, `subject`, `comments`, `priority`, `status`
- `assigned_to_user_id`, `initiated_by_user_id`, `contract_id`
- `reminder_dt`, `completed_dt`, `stamp`

#### Supporting Tables
| Table | Description |
|-------|-------------|
| `lookup_set` | Lookup values (states, priorities, etc.) |
| `note` | Notes attached to contracts or products |
| `event_log` | System event logging (info, warning, error) |
| `scheduler_process` | Background job tracking |

### Key Functions & Stored Procedures
- `dbo.GetLocalTime()` - Returns server local time (Central European)
- `dbo.fn_get_lookup_value(@id)` - Get lookup value by ID
- `dbo.ufn_contract_next_state_names(@state)` - Valid next states for a contract
- `dbo.ufn_item_next_state_names(@state)` - Valid next states for contract items
- `dbo.usp_DeleteDraftContractsBatch` - Batch delete expired draft contracts

---

## API Endpoints

### Authentication (`/api/auth`, `/api`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/api/login` | User login | No |
| POST | `/api/auth/login` | User login (alias) | No |
| POST | `/api/logout` | User logout | Yes |
| POST | `/api/auth/logout` | User logout (alias) | Yes |
| POST | `/api/auth/forgot-password` | Request password reset | No |
| GET | `/api/auth/reset-token/{token}` | Validate reset token | No |
| POST | `/api/auth/reset-password` | Complete password reset | No |
| POST | `/api/register` | User registration | No |

### Users (`/api/users`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/users` | List users (paginated, filterable) | Yes |
| GET | `/api/users/{id}` | Get user details | Yes |
| POST | `/api/users` | Create user | Yes |
| PUT | `/api/users/{id}` | Update user | Yes |
| POST | `/api/users/{id}/activate` | Activate user | Yes |
| POST | `/api/users/{id}/deactivate` | Deactivate user | Yes |
| DELETE | `/api/users/{id}` | Delete user | Yes |
| POST | `/api/users/change-temp-password` | Change temporary password | Yes |

### Groups/Teams (`/api/groups`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/groups` | List active groups (for dropdowns) | Yes |

### Profile (`/api/profile`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/profile` | Get current user profile | Yes |
| PUT | `/api/profile` | Update current user profile | Yes |

### Contracts (`/api/contracts`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/contracts` | List contracts (paginated, filterable) | Yes |
| GET | `/api/contracts/{id}` | Get contract details | Yes |
| GET | `/api/contracts/{id}/items` | Get contract line items | Yes |
| PUT | `/api/contracts/{id}/items/{detId}` | Update contract item | Yes |
| DELETE | `/api/contracts/{id}/items/{detId}` | Delete contract item | Yes |
| POST | `/api/contracts/{id}/submit` | Submit contract (COD) | Yes |
| POST | `/api/contracts/{id}/submit-payment` | Submit with online payment | Yes |
| POST | `/api/contracts/{id}/cancel` | Cancel contract | Yes |
| POST | `/api/contracts/{id}/state` | Change contract state | Yes |
| GET | `/api/contracts/export` | Export contracts to CSV | Yes |

### Contract Items Workflow (`/api/contracts/items`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/contracts/items/{id}/state/modal-data` | Get state change modal data | Yes |
| POST | `/api/contracts/items/{id}/state/set` | Set item state | Yes |

### Contract Notes (`/api/contracts/{id}/notes`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/contracts/{id}/notes` | List notes for contract | Yes |
| POST | `/api/contracts/{id}/notes` | Add note to contract | Yes |

### Products (`/api/products`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/products` | List products (admin grid) | Yes |
| GET | `/api/products/cards` | Get product cards (public) | No |
| GET | `/api/products/filter-options` | Get filter options (sizes, colors, price range) | No |
| GET | `/api/products/{id}` | Get product details | Yes |
| POST | `/api/products` | Create product | Yes |
| PUT | `/api/products/{id}` | Update product | Yes |
| POST | `/api/products/{id}/activate` | Activate product | Yes |
| POST | `/api/products/{id}/deactivate` | Deactivate product | Yes |
| DELETE | `/api/products/{id}` | Delete product | Yes |

### Product Variants (`/api/product-variants`, `/api/variants`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/products/{id}/variants` | Get distinct sizes/colors | No |
| GET | `/api/variants/by-selection` | Get variant by size/color | No |
| GET | `/api/variants/{id}/availability` | Get inventory availability | No |
| POST | `/api/product-variants/{id}/activate` | Activate variant | Yes |
| POST | `/api/product-variants/{id}/deactivate` | Deactivate variant | Yes |

### Product Notes (`/api/products/{id}/notes`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/products/{id}/notes` | List notes for product | Yes |
| POST | `/api/products/{id}/notes` | Add note to product | Yes |

### Cart (`/api/cart`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/api/cart/items` | Add item to cart | Yes |
| POST | `/api/cart/merge-anon` | Merge anonymous cart after login | Yes |

### Anonymous Cart (`/api/anon-cart`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/api/anon-cart/items` | Add item to anonymous cart | No |
| GET | `/api/anon-cart/summary` | Get anonymous cart summary | No |
| DELETE | `/api/anon-cart` | Clear anonymous cart | No |

### Session (`/api/session`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/api/session/pending-add` | Set pending add-to-cart | No |
| GET | `/api/session/pending-add` | Get pending add-to-cart | No |
| DELETE | `/api/session/pending-add` | Clear pending add-to-cart | No |

### Stores (`/api/stores`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/stores` | List all stores | No |
| PUT | `/api/stores/{id}` | Update store details | Yes |

### Payments (`/api/payments`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/payments/status` | Get payment status | No |
| POST | `/api/payments/webhook` | Generic payment webhook | No |
| POST | `/api/payments/wspay/callback` | WSPay callback (success/failure) | No |
| POST | `/api/payments/wspay/mock-callback` | Mock callback (testing only) | No |

### Tasks (`/api/tasks`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/tasks` | List tasks (paginated, filterable) | Yes |
| GET | `/api/tasks/{id}` | Get task details | Yes |
| GET | `/api/tasks/lookups` | Get priority/status lookups | Yes |
| GET | `/api/tasks/contract-pending` | Check pending tasks for contract | Yes |
| PUT | `/api/tasks/{id}` | Update task | Yes |
| POST | `/api/tasks/{id}/status` | Set task status | Yes |

### Notes (`/api/notes`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/notes` | List all notes | Yes |
| GET | `/api/notes/{id}` | Get note details | Yes |
| POST | `/api/notes` | Create note | Yes |
| PUT | `/api/notes/{id}` | Update note | Yes |
| DELETE | `/api/notes/{id}` | Delete note | Yes |
| POST | `/api/notes/{id}/activate` | Activate note | Yes |
| POST | `/api/notes/{id}/deactivate` | Deactivate note | Yes |

### Approval Rules (`/api/approval-rules`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/approval-rules` | List approval rules | Yes |
| GET | `/api/approval-rules/{id}` | Get rule details | Yes |
| PUT | `/api/approval-rules/{id}` | Update rule | Yes |
| POST | `/api/approval-rules/{id}/activate` | Activate rule | Yes |
| POST | `/api/approval-rules/{id}/deactivate` | Deactivate rule | Yes |

### Event Log (`/api/event-log`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/event-log` | List events (paginated, filterable) | Yes |
| GET | `/api/event-log/export` | Export events to CSV | Yes |

### Reports (`/api/reports`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/api/reports/eom/run` | Trigger EOM report manually | Yes |
| GET | `/api/reports/eom/preview` | Preview EOM report data | Yes |

### Scheduler Processes (`/api/processes`)
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/processes` | List scheduler processes | Yes |
| POST | `/api/processes/{id}/activate` | Activate process | Yes |
| POST | `/api/processes/{id}/deactivate` | Deactivate process | Yes |
| POST | `/api/processes/{id}/run` | Run process manually | Yes |

### Utility Endpoints
| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/whoami` | Get current user info | No |
| GET | `/api/email-health` | Email service health check | Yes |

---

## Frontend Pages

### Public Pages
| Page | Route | Description |
|------|-------|-------------|
| `home.html` | `/home` | Landing page with Browse button |
| `about.html` | `/about` | About page with store information |
| `address.html` | `/address` | Store addresses and details |
| `collections.html` | `/collections` | Product catalog with filters |
| `product-details.html` | `/product-details?id=X` | Product detail page with variant selection |
| `login.html` | `/login` | Login form |
| `reset-password.html` | `/reset-password?token=X` | Password reset form |

### Authenticated Pages
| Page | Route | Description | Role |
|------|-------|-------------|------|
| `contracts.html` | `/contracts` | Contract management grid | All |
| `contract-details.html` | `/contract-details?id=X` | Contract details with items | All |
| `contractshistory.html` | `/contractshistory` | Historical contracts | All |
| `contractshistory-details.html` | `/contractshistory-details?id=X` | Historical contract details | All |
| `profile.html` | `/profile` | User profile management | All |
| `products.html` | `/products` | Product management grid | Admin/Manager |
| `notes.html` | `/notes` | Notes management | Admin/Manager |
| `users.html` | `/users` | User management | Admin/Manager |
| `tasks.html` | `/tasks` | Task management | Admin/Manager |
| `eventlog.html` | `/eventlog` | System event log | Admin/Manager |
| `processes.html` | `/processes` | Scheduler processes | Admin/Manager |
| `approvalrules.html` | `/approvalrules` | Approval rules config | Admin/Manager |
| `changestore.html` | `/changestore` | Store management | Admin/Manager |

### Payment Pages
| Page | Route | Description |
|------|-------|-------------|
| `payments-return-success.html` | `/payments-return-success` | Payment success return |
| `payments-return-cancel.html` | `/payments-return-cancel` | Payment cancelled return |
| `payments-mock.html` | `/payments-mock` | Mock payment page (testing) |

---

## Services

### Cart Services
| Service | Description |
|---------|-------------|
| `CartService` | Manages cart operations via draft contracts |
| `AnonCartCacheService` | In-memory cache for anonymous cart items |
| `CartMergeService` | Merges anonymous cart into user's draft on login |
| `SessionBridgeService` | Handles pending add-to-cart across login |

### Product Services
| Service | Description |
|---------|-------------|
| `ProductQueryService` | Product queries (cards, filters, variants, availability) |
| `VariantAvailabilityService` | Checks variant stock across stores |

### Payment Services
| Service | Description |
|---------|-------------|
| `IPaymentService` / `PaymentService` | Payment intent creation and callback handling |
| `IWspayClient` / `WspayClient` | WSPay gateway integration |
| `StubWspayClient` | Test double for payment gateway |

### Workflow Services
| Service | Description |
|---------|-------------|
| `ContractItemWorkflowService` | Contract item state transitions |
| `WriteOffApprovalTaskService` | Creates approval tasks for write-offs |

### Email Services
| Service | Description |
|---------|-------------|
| `IEmailService` / `EmailService` | SMTP email sending via MailKit |
| `ForgotPasswordService` | Password reset email workflow |

### Background Services
| Service | Description |
|---------|-------------|
| `EomReportService` | End-of-Month report generation |
| `EomReportScheduler` | Background scheduler for EOM reports |
| `EomReportPdfRenderer` | PDF generation for EOM reports |
| `DraftContractPurgeService` | Cleans up expired draft contracts |
| `DraftContractPurgeScheduler` | Background scheduler for draft purge |

### Notification Services
| Service | Description |
|---------|-------------|
| `INotificationService` / `NotificationService` | In-app notifications |

---

## Authentication & Authorization

### Authentication
- **Cookie-based** using ASP.NET Core Cookie Authentication
- Session persisted for 8 hours with refresh
- Temp password flow forces password change on first login
- Password reset via email token (15-minute expiry)

### Password Policy
- Minimum 8 characters
- At least one uppercase letter
- At least one digit
- Cannot reuse current password on reset

### User Roles
| Role | ID | Permissions |
|------|-----|-------------|
| Customer | 5 | View own contracts, profile, browse products |
| Worker | 6 | View assigned tasks, contracts |
| Manager | 7 | All worker permissions + manage products, users, tasks |
| Administrator | 8 | Full system access |

### Authorization Checks
```csharp
UserContextHelper.CanManageProducts(HttpContext) // Manager or Admin only
UserContextHelper.IsAdministrator(HttpContext)   // Admin only
UserContextHelper.GetCurrentUserId(HttpContext)  // Current user ID
```

### Special Account Behaviors
- **Team/Group accounts** (`is_group = true`) cannot log in
- **Deactivated accounts** are blocked from login
- **System user** (ID=2) is reserved for automated processes

---

## Key Features

### 1. Contract Lifecycle
```
Draft → Submitted → Accepted/PartiallyAccepted/Rejected → InProgress → Completed → Dispatched → Delivered
                                                                                              ↓
                                                      Returned → WrittenOff → Refunded        │
                                                                                              ↓
                                                                                          Cancelled
```

### 2. Product Management
- Products with multiple variants (size/color combinations)
- Per-variant pricing
- Inventory tracked per store location
- Product photos per variant
- Activation/deactivation without deletion

### 3. Shopping Cart
- **Authenticated users**: Cart stored as Draft contract
- **Anonymous users**: Cart stored in memory cache with cookie ID
- **Session bridging**: Pending add-to-cart preserved across login
- **Cart merge**: Anonymous cart merged into user's draft on login

### 4. Payment Processing
- WSPay integration for online payments
- Cash-on-Delivery (COD) option
- Payment status tracking
- Idempotent callback handling
- Amount/currency verification

### 5. Task Management
- Priority levels: Urgent, High, Normal, Low
- Status workflow: NotStarted → Started → Approved/Rejected/Cancelled/Completed/Failed
- Automatic task creation for write-off approvals
- Contract-linked tasks

### 6. Approval Rules
- Configurable rules for write-offs and refunds
- Amount thresholds
- Percentage thresholds
- Assigned approvers

### 7. Reporting
- End-of-Month (EOM) reports
- PDF generation with QuestPDF
- Scheduled and manual execution
- Report summaries by status

### 8. Background Jobs
- **EOM Report Scheduler**: Generates monthly reports automatically
- **Draft Contract Purge**: Cleans up abandoned draft contracts older than configured threshold

### 9. Event Logging
- Three log levels: Information, Warning, Error
- Full stack trace capture for errors
- User association
- CSV export with timestamp

### 10. Store Management
- Multiple store locations
- Store details (address, phone, email, hours)
- Per-store inventory
- Working hours editor

---

## Data Transfer Objects (DTOs)

### Authentication
| DTO | Purpose |
|-----|---------|
| `LoginDto` | Login request (code, password) |
| `RegisterDto` | Registration request |
| `ChangeTempPasswordDto` | Temporary password change |

### Profile
| DTO | Purpose |
|-----|---------|
| `ProfileDto` | User profile response |
| `ProfileUpdateDto` | Profile update request |

### Users
| DTO | Purpose |
|-----|---------|
| `UserCreateDto` | New user creation |
| `UserUpdateDto` | User update request |

### Products
| DTO | Purpose |
|-----|---------|
| `ProductDto` | Product details |
| `ProductCardDto` | Product card for catalog |
| `VariantsDto` | Variant sizes/colors |
| `VariantSelectionDto` | Variant lookup result |
| `AvailabilityDto` | Stock availability per store |

### Cart
| DTO | Purpose |
|-----|---------|
| `AddToCartRequest` | Add to cart request |
| `AddToCartResponse` | Add to cart response |

### Contracts
| DTO | Purpose |
|-----|---------|
| `ContractItemUpdateDto` | Update contract line item |
| `WorkflowDto` | State change workflow data |

### Tasks
| DTO | Purpose |
|-----|---------|
| `SetTaskStatusDto` | Task status update |
| `UpdateTaskDto` | Task update request |

### Notes
| DTO | Purpose |
|-----|---------|
| `NoteDto` | Note data |

### Stores
| DTO | Purpose |
|-----|---------|
| `StoreUpdateDto` | Store update request |

### Error Handling
| DTO | Purpose |
|-----|---------|
| `ClientErrorDto` | Client error response |

---

## Helper Classes

### Enumerations (`GlobalEnums.cs`)
```csharp
EventType: Information(2), Warning(3), Error(4)
UserRole: Customer(5), Worker(6), Manager(7), Administrator(8)
ContractState: Draft(9) through Refunded(21)
ProductStateInOrder: Draft(22), Submitted(23), Accepted(24), Rejected(25)
TaskPriority: Urgent(26), High(27), Normal(28), Low(29)
TaskStatus: NotStarted(30) through SuccessfulNothingProcessed(39)
ApprovalRuleContext: RefundPayment(40), ContractWriteOff(41)
```

### Utility Helpers
| Helper | Purpose |
|--------|---------|
| `PasswordHelper` | Password hashing (BCrypt) and verification |
| `PhoneHelper` | Serbian phone number normalization |
| `DateRangeHelper` | Date range calculations |
| `LookupHelper` | Lookup value resolution |
| `LoggerHelper` | Event logging to database |
| `QuerySorting` | Dynamic query sorting extension |

### Infrastructure
| Class | Purpose |
|-------|---------|
| `JsonResultHelper` | Stable JSON serialization |
| `UserContextHelper` | User context extraction from claims |
| `StreamJsonOutputFormatter` | Streaming JSON output |

---

## Testing

### Test Project
Location: `Tests/OnlineContract.Tests.csproj`

### Test Categories
| Category | Description |
|----------|-------------|
| API Tests | Controller endpoint testing |
| Service Tests | Business logic unit tests |
| Integration Tests | End-to-end workflow tests |
| UI Tests | Frontend interaction tests |

### Key Test Files
| File | Coverage |
|------|----------|
| `ContractsListTests.cs` | Contract listing and filtering |
| `ContractDetailsTests.cs` | Contract detail operations |
| `ContractStateWorkflowTests.cs` | State transitions |
| `CartBadgeApiTests.cs` | Cart functionality |
| `PaymentsCallbackHardenedTests.cs` | Payment callback security |
| `WspaySignatureTests.cs` | WSPay signature verification |
| `ProfileTests.cs` | Profile operations |
| `TasksApiTests.cs` | Task management |
| `ForgotPasswordTests.cs` | Password reset flow |
| `EomReportServiceTests.cs` | EOM report generation |
| `DraftContractPurgeServiceTests.cs` | Draft cleanup |

### Test Infrastructure
| Class | Purpose |
|-------|---------|
| `WebAppFactory.cs` | Test application factory |
| `TestDataSeeder.cs` | Test data setup |

### Running Tests
```bash
dotnet test --configuration Debug
```

---

## Configuration

### appsettings.json Keys
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=OnlineContract;..."
  },
  "AppOrigin": "https://your-domain.com",
  "GMAIL_USER": "user@gmail.com",
  "GMAIL_APP_PASSWORD": "app-specific-password",
  "Payments": {
    "Enabled": "true",
    "WSPay": {
      "Environment": "Production|Sandbox|Testing",
      "ShopId": "...",
      "SecretKey": "...",
      "Currency": "RSD",
      "UseMinorUnits": "true",
      "WebhookSecret": "..."
    }
  },
  "ReportSettings": {
    "OutputDirectory": "./reports",
    "ScheduleTime": "02:00"
  }
}
```

### Environment Variables
- `ASPNETCORE_ENVIRONMENT`: Development, Staging, Production, Testing
- `GMAIL_USER`: Email sender address
- `GMAIL_APP_PASSWORD`: Gmail app-specific password
- `APP_ORIGIN`: Application base URL for email links

---

## Deployment Notes

### Prerequisites
1. SQL Server database with schema applied
2. .NET 9.0 Runtime
3. SMTP credentials for email
4. WSPay merchant account (production)

### Database Setup
```bash
# Apply schema
sqlcmd -S server -d OnlineContract -i Database/schema.sql

# Apply seed data
sqlcmd -S server -d OnlineContract -i Database/seed.sql
```

### Build & Run
```bash
dotnet build --configuration Release
dotnet run --configuration Release
```

---

*Last Updated: January 2026*
