using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text;
using System.Security.Claims;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;
using OnlineContract.Dtos;
using System.Text.Json;
// Load local .env into process environment (development only).
// This avoids hard-coding secrets while allowing local dev to store values in a .env file.
try
{
    var aspEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    if (string.Equals(aspEnv, "Development", StringComparison.OrdinalIgnoreCase))
    {
        var dotEnvPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (File.Exists(dotEnvPath))
        {
            foreach (var raw in File.ReadAllLines(dotEnvPath))
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#")) continue;
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;
                var key = line.Substring(0, idx).Trim();
                var val = line.Substring(idx + 1).Trim();
                if ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'")))
                {
                    val = val.Substring(1, val.Length - 2);
                }
                // Unescape common escaped newline sequences
                val = val.Replace("\\n", "\n");
                // Only set if not already present in environment (do not overwrite)
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, val, EnvironmentVariableTarget.Process);
                }
            }
        }
    }
}
catch { }

var builder = WebApplication.CreateBuilder(args);

// -------------------------
// Services
// -------------------------

// DbContext
if (builder.Environment.IsEnvironment("Testing"))
{
    // In tests, the factory will override with a persistent in-memory SQLite connection
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("DataSource=:memory:"));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
}

// Memory cache (used by ForgotPasswordService for rate limiting)
builder.Services.AddMemoryCache();

// Distributed cache + Session (for pending Add to Cart)
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
// PROD Redis (ready for production; keep commented until configured):
// builder.Services.AddStackExchangeRedisCache(options => {
//     options.Configuration = builder.Configuration["Redis:Configuration"];
//     options.InstanceName = "onlinecontract:";
// });
// Add session except in Testing to avoid PipeWriter issues
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSession(options =>
    {
        options.Cookie.Name = ".OnlineContract.Session";
        options.IdleTimeout = TimeSpan.FromHours(4);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });
}

// Email services: register the SMTP MailKit sender and adapter to the existing IEmailService helper
builder.Services.AddScoped<OnlineContract.Services.IEmailService, OnlineContract.Services.EmailService>();
// Register new services
builder.Services.AddScoped<OnlineContract.Services.ProductQueryService>();
builder.Services.AddScoped<OnlineContract.Services.CartService>();
builder.Services.AddSingleton<OnlineContract.Services.SessionBridgeService>();
builder.Services.AddScoped<OnlineContract.Services.AnonCartCacheService>();
builder.Services.AddScoped<OnlineContract.Services.CartMergeService>();
builder.Services.AddScoped<OnlineContract.Services.VariantAvailabilityService>();
builder.Services.AddScoped<OnlineContract.Services.WriteOffApprovalTaskService>();

// EOM Report services
builder.Services.Configure<OnlineContract.Services.Reports.EomReportSettings>(
    builder.Configuration.GetSection("ReportSettings"));
builder.Services.AddSingleton<OnlineContract.Services.Notifications.INotificationService, OnlineContract.Services.Notifications.NotificationService>();
builder.Services.AddScoped<OnlineContract.Services.Reports.IEomReportPdfRenderer, OnlineContract.Services.Reports.EomReportPdfRenderer>();
builder.Services.AddScoped<OnlineContract.Services.Reports.IEomReportService, OnlineContract.Services.Reports.EomReportService>();
// Only register the scheduler in non-Testing environments
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<OnlineContract.Services.Reports.EomReportScheduler>();
}

// Draft Contract Purge services
builder.Services.AddScoped<OnlineContract.Services.DraftPurge.IDraftContractPurgeService, OnlineContract.Services.DraftPurge.DraftContractPurgeService>();
// Only register the scheduler in non-Testing environments
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<OnlineContract.Services.DraftPurge.DraftContractPurgeScheduler>();
}

// Payments
// Use stub client in non-Production environments to allow local testing without credentials
var wspEnv = builder.Configuration["Payments:WSPay:Environment"] ?? (builder.Environment.IsProduction() ? "Production" : "Testing");
if (!string.Equals(wspEnv, "Production", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<OnlineContract.Services.IWspayClient, OnlineContract.Services.StubWspayClient>();
else
    builder.Services.AddScoped<OnlineContract.Services.IWspayClient, OnlineContract.Services.WspayClient>();
builder.Services.AddScoped<OnlineContract.Services.IPaymentService, OnlineContract.Services.PaymentService>();

// Cookie Authentication + Authorization
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".OnlineContract.Auth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";

        // Dev vs Prod cookie settings
        var appOriginTmp = builder.Configuration["AppOrigin"]; // e.g. https://app.example.com
        if (builder.Environment.IsDevelopment())
        {
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }
        else
        {
            options.Cookie.SameSite = string.IsNullOrEmpty(appOriginTmp) ? SameSiteMode.Lax : SameSiteMode.None;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }

        var cookieDomain = builder.Configuration["CookieDomain"];
        if (!string.IsNullOrWhiteSpace(cookieDomain))
        {
            options.Cookie.Domain = cookieDomain;
        }
        options.Cookie.Path = "/";

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                bool isApi =
                    ctx.Request.Path.StartsWithSegments("/api") ||
                    ctx.Request.Headers["Accept"].ToString().Contains("application/json") ||
                    (ctx.Request.Headers["X-Requested-With"] == "XMLHttpRequest");

                if (isApi)
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Controllers
builder.Services.AddControllers(options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        // Prefer stream-based JSON formatter first to avoid PipeWriter issues in TestServer
        options.OutputFormatters.Insert(0, new StreamJsonOutputFormatter(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
})
.AddJsonOptions(options =>
{
    // Use UnsafeRelaxedJsonEscaping to properly display Latin diacritics (ć, č, š, ž, đ, etc.)
    options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

// Suppress automatic 400 responses from [ApiController] so validation stays manual,
// matching legacy minimal API behavior expected by tests
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
{
    o.SuppressModelStateInvalidFilter = true;
});

// Persist DataProtection keys
builder.Services.AddDataProtection()
       .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")))
       .SetApplicationName("AppAuth");

var appOrigin = builder.Configuration["AppOrigin"]; // npr. https://app.example.com
if (!string.IsNullOrEmpty(appOrigin))
{
    builder.Services.AddCors(o => o.AddPolicy("ApiCors", b =>
        b.WithOrigins(appOrigin)
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()
    ));
}

//builder.WebHost.ConfigureKestrel(k =>
//{
//    k.ListenLocalhost(52616);                         // HTTP
//    k.ListenLocalhost(52617, o => o.UseHttps());      // HTTPS
//});
//builder.Services.AddHttpsRedirection(o => o.HttpsPort = 52617);

// -------------------------
// Build
// -------------------------

var app = builder.Build();

// -------------------------
// Rewrite: /route -> /route.html
// -------------------------

// If an already-authenticated user attempts to open the login/signin pages
// directly (typing the URL or opening a bookmarked link), redirect them
// to `/home`. We do a lightweight cookie presence check here to avoid
// forcing full authentication middleware before rewrites.
app.Use(async (ctx, next) =>
{
    try
    {
        var p = ctx.Request.Path.Value ?? string.Empty;
        if (p.StartsWith("/login", StringComparison.OrdinalIgnoreCase) || p.StartsWith("/signin", StringComparison.OrdinalIgnoreCase))
        {
            // Consider the user authenticated when the auth cookie exists.
            if (ctx.Request.Cookies != null && ctx.Request.Cookies.ContainsKey(".OnlineContract.Auth"))
            {
                ctx.Response.Redirect("/home");
                return;
            }
        }
    }
    catch { }
    await next();
});

var rewriteOptions = new RewriteOptions()
    .AddRewrite("(?i)^login$", "login.html", skipRemainingRules: true)
    .AddRewrite("(?i)^home$", "home.html", skipRemainingRules: true)
    .AddRewrite("(?i)^eventlog$", "eventlog.html", skipRemainingRules: true)
    .AddRewrite("(?i)^about$", "about.html", skipRemainingRules: true)
    .AddRewrite("(?i)^address$", "address.html", skipRemainingRules: true)
    .AddRewrite("(?i)^collections$", "collections.html", skipRemainingRules: true)
    .AddRewrite("(?i)^changestore$", "changestore.html", skipRemainingRules: true)
    .AddRewrite("(?i)^users$", "users.html", skipRemainingRules: true)
    .AddRewrite("(?i)^contracts$", "contracts.html", skipRemainingRules: true)
    .AddRewrite("(?i)^approvalrules$", "approvalrules.html", skipRemainingRules: true)
    .AddRewrite("(?i)^processes$", "processes.html", skipRemainingRules: true)
    .AddRewrite("(?i)^processlog$", "processlog.html", skipRemainingRules: true)
    .AddRewrite("(?i)^tasks$", "tasks.html", skipRemainingRules: true)
    .AddRewrite("(?i)^reset$", "reset-password.html", skipRemainingRules: true)
    .AddRewrite("(?i)^contractshistory$", "contractshistory.html", skipRemainingRules: true)
    .AddRewrite("(?i)^payments/wspay/return/success$", "payments-return-success.html", skipRemainingRules: true)
    .AddRewrite("(?i)^payments/wspay/return/cancel$", "payments-return-cancel.html", skipRemainingRules: true)
    .AddRewrite("(?i)^payments/wspay/mock$", "payments-mock.html", skipRemainingRules: true)
    .AddRewrite("(?i)^terms$", "terms.html", skipRemainingRules: true)
    .AddRewrite("(?i)^privacy$", "privacy.html", skipRemainingRules: true);

app.UseRewriter(rewriteOptions);

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

// -------------------------
// Middleware order
// -------------------------

app.UseRouting();

if (!string.IsNullOrEmpty(appOrigin))
{
    app.UseCors("ApiCors");
}

app.UseAuthentication();
app.UseAuthorization();
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseSession();
}

// -------------------------
// Cache
// -------------------------

app.Use(async (ctx, next) =>
{
    await next.Invoke();
    try
    {
        var path = ctx.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var isHtml = (ctx.Response.ContentType ?? string.Empty)
                        .StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
                     || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
        if (isHtml)
        {
            ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            ctx.Response.Headers["Pragma"] = "no-cache";
            ctx.Response.Headers["Expires"] = "0";
        }
    }
    catch { }
});

// Static files (no-store za .html)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    }
});

// Map physical product images folder to /product-images
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(@"C:\Projects\Build\InstallDocs"),
    RequestPath = "/product-images"
});

// -------------------------
// Routes
// -------------------------

// Map attribute-routed controllers
app.MapControllers();

// Root -> /login
app.MapGet("/", context =>
{
    context.Response.Redirect("/login");
    return Task.CompletedTask;
});

// StableJson helper removed; controllers use JsonResultHelper for consistent JSON

// Removed unused StableJsonStatus helper

// Contract details page shell
app.MapGet("/contracts/{id:int}", (HttpContext context, int id) =>
{
    // HTML shell is static; data loads via /api/contracts/{id}
    var filePath = Path.Combine(app.Environment.WebRootPath, "contract-details.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// Contracts listing page (guarded like products)
app.MapGet("/contracts", (HttpContext context) =>
{
    var rc = context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
    var isManager = int.TryParse(rc, out var roleId) && (roleId == 6 || roleId == 7 || roleId == 8);
    if (!isManager) return Results.Redirect("/home");
    var filePath = Path.Combine(app.Environment.WebRootPath, "contracts.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// Approval Rules page
app.MapGet("/approvalrules", (HttpContext context) =>
{
    var rc = context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
    var isManager = int.TryParse(rc, out var roleId) && (roleId == 6 || roleId == 7 || roleId == 8);
    if (!isManager) return Results.Redirect("/home");
    var filePath = Path.Combine(app.Environment.WebRootPath, "approvalrules.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// Processes page (same access as Approval Rules)
app.MapGet("/processes", (HttpContext context) =>
{
    var rc = context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
    var isManager = int.TryParse(rc, out var roleId) && (roleId == 6 || roleId == 7 || roleId == 8);
    if (!isManager) return Results.Redirect("/home");
    var filePath = Path.Combine(app.Environment.WebRootPath, "processes.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// Profile page
app.MapGet("/profile", (HttpContext context) =>
{
    if (!(context.User?.Identity?.IsAuthenticated ?? false))
        return Results.Redirect("/login?mode=login");
    var filePath = Path.Combine(app.Environment.WebRootPath, "profile.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// Customer-only Contracts History page
// Local helper removed; inline role checks used in shells

app.MapGet("/contractshistory", (HttpContext context) =>
{
    var rc = context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
    var isCustomer = int.TryParse(rc, out var roleId) && roleId == 5;
    if (!isCustomer) return Results.Redirect("/home");
    var filePath = Path.Combine(app.Environment.WebRootPath, "contractshistory.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// Customer-only Contract History Details (items-only) page shell
app.MapGet("/contractshistory/{id:int}", (HttpContext context, int id) =>
{
    var rc = context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
    var isCustomer = int.TryParse(rc, out var roleId) && roleId == 5;
    if (!isCustomer) return Results.Redirect("/home");
    var filePath = Path.Combine(app.Environment.WebRootPath, "contractshistory-details.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// Variant-level actions migrated to ProductVariantsController
        

// Products pages (Admin/Manager/Worker)
// Local helper removed; inline role checks used in shells

app.MapGet("/products", (HttpContext context) =>
{
        var rc = context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
        var isManager = int.TryParse(rc, out var roleId) && (roleId == 6 || roleId == 7 || roleId == 8);
        if (!isManager) return Results.Redirect("/home");
        var filePath = Path.Combine(app.Environment.WebRootPath, "products.html");
        return Results.File(filePath, "text/html");
    }).RequireAuthorization();

app.MapGet("/products/new", (HttpContext context) =>
{
        var rc = context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
        var isManager = int.TryParse(rc, out var roleId) && (roleId == 6 || roleId == 7 || roleId == 8);
        if (!isManager) return Results.Redirect("/home");
        var filePath = Path.Combine(app.Environment.WebRootPath, "product-details.html");
        return Results.File(filePath, "text/html");
    }).RequireAuthorization();

app.MapGet("/products/{id:int}", (HttpContext context, int id) =>
{
        var rc = context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
        var isManager = int.TryParse(rc, out var roleId) && (roleId == 6 || roleId == 7 || roleId == 8);
        if (!isManager) return Results.Redirect("/home");
        var filePath = Path.Combine(app.Environment.WebRootPath, "product-details.html");
        return Results.File(filePath, "text/html");
    }).RequireAuthorization();

// Removed broad MapWhen fallback to prevent overriding specific routes like /checkout

// -------------------------
// Collections & Product Cards + Variant endpoints
// -------------------------

// Product cards endpoint migrated to ProductsQueryController

// Payments routes handled by PaymentsController

// Variant query endpoints migrated to ProductVariantsController

// -------------------------
// Session bridge for pending Add to Cart
// -------------------------
// Session pending-add endpoints migrated to SessionController

// -------------------------
// Cart API
// -------------------------
// Cart items endpoint migrated to CartController

// -------------------------
// Anonymous Cart API (cache + cookie)
// -------------------------
// Anon cart endpoints migrated to AnonCartController

// Merge anon cart into authenticated draft (auth required)
// Cart merge endpoint migrated to CartController

// -------------------------
// Checkout route (server-side anon merge)
// -------------------------
// Local helper removed; inline claim parsing used in /checkout shell

app.MapGet("/checkout", async (HttpContext ctx, OnlineContract.Services.CartMergeService mergeSvc, OnlineContract.Services.AnonCartCacheService anonSvc) =>
{
    int? userId = null;
    {
        var claim = ctx.User.FindFirst("sub")
            ?? ctx.User.FindFirst("userId")
            ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim != null && int.TryParse(claim.Value, out var id)) userId = id;
    }
    if (userId is null)
    {
        var returnUrl = "/checkout";
        return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }
    if (anonSvc.TryGetAnonId(out var anonId))
    {
        await mergeSvc.MergeAnonIntoUserDraftAsync(userId.Value, anonId, ctx.RequestAborted);
        // After success the anon cookie/cache are cleared.
    }
    // Redirect to existing contracts page (acts as checkout/cart view in this app)
    return Results.Redirect("/contracts");
}).AllowAnonymous();

// Auth endpoints are served by AuthController

// -------------------------
// Users & Teams API (Authorized) — served by UsersController and GroupsController
// -------------------------

// -------------------------
// EventLog + Store (as before)
// -------------------------

// EventLog endpoints migrated to EventLogController

// Stores GET endpoint migrated to StoresController

// Stores PUT endpoint migrated to StoresController

// Contracts list endpoint migrated to ContractsController

// Contract details endpoint migrated to ContractsController

// Contract items (read-only list for details page)
// Contract items endpoint migrated to ContractsController

// Contract state modal-data endpoint migrated to ContractsController

// Contract state change endpoint
// Contract state change endpoint migrated to ContractsController

// Contract item delete endpoint migrated to ContractsController

// Contract save endpoint migrated to ContractsController

// Contract item edit endpoint migrated to ContractsController

// Payment webhook migrated to PaymentsController

// Payments status and callback are handled by PaymentsController

// Contract submit endpoint migrated to ContractsController

// Contract items workflow modal-data migrated to ContractItemsWorkflowController
// Contract items workflow modal-data migrated to ContractItemsWorkflowController

// Contract items workflow set-state migrated to ContractItemsWorkflowController
// Contract items workflow set-state migrated to ContractItemsWorkflowController

// Contracts export endpoint migrated to ContractsController (removed)

// -------------------------
// Products API (Admin/Manager/Worker)
// -------------------------

// Local helper removed; controllers use UserContextHelper

// -------------------------
// Profile API
// -------------------------

// Profile endpoints migrated to ProfileController

// Removed unused IsValidEmail local function (validation handled elsewhere)

// Phone normalization now handled by OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone
// Products list endpoint migrated to ProductsController

// Product details endpoint migrated to ProductsController

// Product create endpoint migrated to ProductsController

// Product update endpoint migrated to ProductsController

// Product details endpoint migrated to ProductsController

// Product upload-photo endpoint migrated to ProductsController

// Product deactivate endpoint migrated to ProductsController

// Product activate endpoint migrated to ProductsController

// Product delete endpoint migrated to ProductsController

// Product Notes endpoints

// Notes page route
app.MapGet("/notes", (HttpContext context) =>
{
    var filePath = Path.Combine(app.Environment.WebRootPath, "notes.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// API: list notes (paged, filters) — handled below; removing corrupted block

// API: update single note (simple edit)
// Note update migrated to NotesController (removed)

// Lifecycle log
var lifetime = app.Lifetime;
lifetime.ApplicationStarted.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStarted callback"));
lifetime.ApplicationStopping.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStopping callback"));

app.Run();