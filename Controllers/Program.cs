using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
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
    .AddRewrite("(?i)^reset$", "reset-password.html", skipRemainingRules: true)
    .AddRewrite("(?i)^contractshistory$", "contractshistory.html", skipRemainingRules: true);

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

// Root -> /login
app.MapGet("/", context =>
{
    context.Response.Redirect("/login");
    return Task.CompletedTask;
});

// Helper: stable JSON in Testing (Results.Text) vs normal JSON otherwise
IResult StableJson(object payload)
{
    return app.Environment.IsEnvironment("Testing")
        ? Results.Text(JsonSerializer.Serialize(payload), "application/json")
        : Results.Json(payload);
}

// Helper: stable JSON with explicit status code (avoids PipeWriter in Testing)
IResult StableJsonStatus(object payload, int statusCode)
{
    return app.Environment.IsEnvironment("Testing")
        ? Results.Text(JsonSerializer.Serialize(payload), "application/json", statusCode: statusCode)
        : Results.Json(payload, statusCode: statusCode);
}

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
    if (!CanManageProducts(context)) return Results.Redirect("/home");
    var filePath = Path.Combine(app.Environment.WebRootPath, "contracts.html");
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
static bool IsCustomer(HttpContext http)
{
    try
    {
        var rc = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
        return int.TryParse(rc, out var roleId) && roleId == 5;
    }
    catch { return false; }
}

app.MapGet("/contractshistory", (HttpContext context) =>
{
    if (!IsCustomer(context)) return Results.Redirect("/home");
    var filePath = Path.Combine(app.Environment.WebRootPath, "contractshistory.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// Customer-only Contract History Details (items-only) page shell
app.MapGet("/contractshistory/{id:int}", (HttpContext context, int id) =>
{
    if (!IsCustomer(context)) return Results.Redirect("/home");
    var filePath = Path.Combine(app.Environment.WebRootPath, "contractshistory-details.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// --- Variant-level actions: activate / deactivate / delete ---
app.MapPost("/api/product-variants/{id:int}/activate", async (AppDbContext db, HttpContext http, int id) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        var v = await db.ProductVariants.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (v == null) return Results.NotFound(new { message = "Inventory row not found. It may have been removed." });
            // Prevent variant/inventory changes when parent product is inactive or deleted
            var parentProduct = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == v.ProductId);
            if (parentProduct == null || !parentProduct.IsActive || parentProduct.IsDeleted)
            {
                return Results.BadRequest(new { success = false, message = "Product Inventory for this product cannot be changed because this product is deactivated or deleted." });
            }
        var uid = GetCurrentUserId(http);
        v.IsActive = true;
        v.LastModifiedById = uid;
        v.LastUpdatedDt = DateTime.Now;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Inventory activated", $"VariantId={v.Id}; ProductId={v.ProductId}", uid);
        return Results.Ok(new { success = true, message = "Product Inventory has been successfully activated." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Activate inventory failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

app.MapPost("/api/product-variants/{id:int}/deactivate", async (AppDbContext db, HttpContext http, int id) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        var v = await db.ProductVariants.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (v == null) return Results.NotFound(new { message = "Inventory row not found. It may have been removed." });
            // Prevent variant/inventory changes when parent product is inactive or deleted
            var parentProduct = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == v.ProductId);
            if (parentProduct == null || !parentProduct.IsActive || parentProduct.IsDeleted)
            {
                return Results.BadRequest(new { success = false, message = "Product Inventory for this product cannot be changed because this product is deactivated or deleted." });
            }
        var uid = GetCurrentUserId(http);
        v.IsActive = false;
        v.LastModifiedById = uid;
        v.LastUpdatedDt = DateTime.Now;
        // Also deactivate related inventory rows for this variant
        var inventories = await db.ProductInventories.Where(i => i.ProductVariantId == v.Id && !i.IsDeleted).ToListAsync();
        foreach (var inv in inventories)
        {
            inv.IsActive = false;
            inv.LastModifiedById = uid;
            inv.LastUpdatedDt = DateTime.Now;
        }
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Inventory deactivated", $"VariantId={v.Id}; ProductId={v.ProductId}", uid);
        return Results.Ok(new { success = true, message = "Product Inventory has been successfully deactivated." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Deactivate inventory failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

app.MapPost("/api/product-variants/{id:int}/delete", async (AppDbContext db, HttpContext http, int id) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    await using var tx = await db.Database.BeginTransactionAsync();
    try
    {
        var v = await db.ProductVariants.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (v == null) return Results.NotFound(new { message = "The inventory row was not found. It may have been removed." });
        // Prevent variant/inventory changes when parent product is inactive or deleted
        var parentProduct = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == v.ProductId);
        if (parentProduct == null || !parentProduct.IsActive || parentProduct.IsDeleted)
        {
            return Results.BadRequest(new { success = false, message = "Product Inventory for this product cannot be changed because this product is deactivated or deleted." });
        }
        var uid = GetCurrentUserId(http);
        // Mark variant deleted and inactive
        v.IsActive = false;
        v.IsDeleted = true;
        v.LastModifiedById = uid;
        v.LastUpdatedDt = DateTime.Now;

        // Mark related inventory rows deleted/inactive
        var inventories = await db.ProductInventories.Where(i => i.ProductVariantId == v.Id && !i.IsDeleted).ToListAsync();
        foreach (var inv in inventories)
        {
            inv.IsActive = false;
            inv.IsDeleted = true;
            inv.LastModifiedById = uid;
            inv.LastUpdatedDt = DateTime.Now;
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Inventory deleted", $"VariantId={v.Id}; ProductId={v.ProductId}", uid);
        return Results.Ok(new { success = true, message = "Product Inventory has been successfully deleted." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Delete inventory failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();
        

// Products pages (Admin/Manager/Worker)
static bool CanManageProducts(HttpContext http)
{
    try
    {
        var rc = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
        return int.TryParse(rc, out var roleId) && (roleId == 6 || roleId == 7 || roleId == 8);
    }
    catch { return false; }
}

app.MapGet("/products", (HttpContext context) =>
{
        if (!CanManageProducts(context)) return Results.Redirect("/home");
        var filePath = Path.Combine(app.Environment.WebRootPath, "products.html");
        return Results.File(filePath, "text/html");
}).RequireAuthorization();

app.MapGet("/products/new", (HttpContext context) =>
{
        if (!CanManageProducts(context)) return Results.Redirect("/home");
        var filePath = Path.Combine(app.Environment.WebRootPath, "product-details.html");
        return Results.File(filePath, "text/html");
}).RequireAuthorization();

app.MapGet("/products/{id:int}", (HttpContext context, int id) =>
{
        if (!CanManageProducts(context)) return Results.Redirect("/home");
        var filePath = Path.Combine(app.Environment.WebRootPath, "product-details.html");
        return Results.File(filePath, "text/html");
}).RequireAuthorization();

// Fallback
app.MapFallback(context =>
{
    context.Response.Redirect("/login");
    return Task.CompletedTask;
});

// -------------------------
// Collections & Product Cards + Variant endpoints
// -------------------------

app.MapGet("/api/products/cards", async (OnlineContract.Services.ProductQueryService svc, HttpContext http) =>
{
    var cards = await svc.GetProductCardsAsync(http.RequestAborted);
    // Log minimal telemetry
    try { await LoggerHelper.LogEventAsync(http.RequestServices.GetRequiredService<AppDbContext>(), OnlineContract.Helpers.EventType.Information, "Product cards load", $"Count={cards.Count}", GetCurrentUserId(http)); } catch { }
    return Results.Json(cards);
}).AllowAnonymous();

app.MapGet("/api/products/{productId:int}/variants", async (OnlineContract.Services.ProductQueryService svc, int productId, HttpContext http) =>
{
    var resp = await svc.GetDistinctSizesColorsAsync(productId, http.RequestAborted);
    return Results.Json(resp);
}).AllowAnonymous();

app.MapGet("/api/variants/by-selection", async (OnlineContract.Services.ProductQueryService svc, int productId, string size, string color, HttpContext http) =>
{
    var row = await svc.GetVariantBySelectionAsync(productId, size, color, http.RequestAborted);
    if (row == null) return Results.NotFound(new { message = "Variant not found." });
    return Results.Json(row);
}).AllowAnonymous();

app.MapGet("/api/variants/{variantId:int}/availability", async (OnlineContract.Services.ProductQueryService svc, int variantId, HttpContext http) =>
{
    var list = await svc.GetAvailabilityAsync(variantId, http.RequestAborted);
    return Results.Json(list);
}).AllowAnonymous();

// -------------------------
// Session bridge for pending Add to Cart
// -------------------------
app.MapPost("/api/session/pending-add", async (OnlineContract.Services.SessionBridgeService sessionSvc, HttpContext http) =>
{
    try
    {
        var dto = await http.Request.ReadFromJsonAsync<System.Collections.Generic.Dictionary<string, int>>();
        var productId = dto != null && dto.TryGetValue("productId", out var pid) ? pid : 0;
        if (productId <= 0) return Results.BadRequest(new { message = "productId is required" });
        sessionSvc.SetPendingAdd(http, productId);
        try { await LoggerHelper.LogEventAsync(http.RequestServices.GetRequiredService<AppDbContext>(), OnlineContract.Helpers.EventType.Information, "PendingAddToCart set", $"ProductId={productId}", GetCurrentUserId(http)); } catch { }
        return Results.Ok(new { success = true });
    }
    catch { return Results.StatusCode(500); }
}).AllowAnonymous();

app.MapDelete("/api/session/pending-add", async (OnlineContract.Services.SessionBridgeService sessionSvc, HttpContext http) =>
{
    try
    {
        sessionSvc.ClearPendingAdd(http);
        try { await LoggerHelper.LogEventAsync(http.RequestServices.GetRequiredService<AppDbContext>(), OnlineContract.Helpers.EventType.Information, "PendingAddToCart cleared", "", GetCurrentUserId(http)); } catch { }
        return Results.Ok(new { success = true });
    }
    catch { return Results.StatusCode(500); }
}).AllowAnonymous();

app.MapGet("/api/session/pending-add", (OnlineContract.Services.SessionBridgeService sessionSvc, HttpContext http) =>
{
    try
    {
        var pid = sessionSvc.GetPendingAdd(http) ?? 0;
        return Results.Json(new { productId = pid });
    }
    catch { return Results.Json(new { productId = 0 }); }
}).AllowAnonymous();

// -------------------------
// Cart API
// -------------------------
app.MapPost("/api/cart/items", async (OnlineContract.Services.CartService cartSvc, HttpContext http) =>
{
    try
    {
        var dto = await http.Request.ReadFromJsonAsync<OnlineContract.Dtos.AddToCartRequest>();
        if (dto == null || dto.ProductVariantId <= 0 || dto.Quantity < 1)
            return StableJsonStatus(new { message = "Invalid payload" }, StatusCodes.Status400BadRequest);

        var uid = GetCurrentUserId(http);
        var res = await cartSvc.AddToCartAsync(uid, dto.ProductVariantId, dto.Quantity, http.RequestAborted);
        try { await LoggerHelper.LogEventAsync(http.RequestServices.GetRequiredService<AppDbContext>(), OnlineContract.Helpers.EventType.Information, "AddToCart", $"VariantId={dto.ProductVariantId}; Qty={dto.Quantity}; ContractId={res.ContractId}", uid); } catch { }
        return StableJson(res);
    }
    catch (InvalidOperationException ex)
    {
        // Business rule violations
        return StableJsonStatus(new { message = ex.Message }, StatusCodes.Status400BadRequest);
    }
    catch (Exception ex)
    {
        try { await LoggerHelper.LogEventAsync(http.RequestServices.GetRequiredService<AppDbContext>(), OnlineContract.Helpers.EventType.Error, "AddToCart failed", ex.ToString(), GetCurrentUserId(http)); } catch { }
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// -------------------------
// Anonymous Cart API (cache + cookie)
// -------------------------
app.MapPost("/api/anon-cart/items", async (OnlineContract.Services.AnonCartCacheService svc, HttpContext http) =>
{
    try
    {
        var dto = await http.Request.ReadFromJsonAsync<OnlineContract.Dtos.AddToCartRequest>();
        if (dto == null || dto.ProductVariantId <= 0 || dto.Quantity < 1)
            return Results.BadRequest(new { message = "Invalid request" });
        var anonId = svc.GetOrCreateAnonId();
        await svc.UpsertAsync(anonId, dto.ProductVariantId, dto.Quantity, http.RequestAborted);
        try { await LoggerHelper.LogEventAsync(http.RequestServices.GetRequiredService<AppDbContext>(), OnlineContract.Helpers.EventType.Information, "Anon AddToCart", $"VariantId={dto.ProductVariantId}; Qty={dto.Quantity}; AnonId={anonId}", 0); } catch { }
        return Results.Text(JsonSerializer.Serialize(new { anonCartId = anonId }), "application/json");
    }
    catch (InvalidOperationException)
    {
        return Results.StatusCode(StatusCodes.Status400BadRequest);
    }
    catch (Exception ex)
    {
        try { await LoggerHelper.LogEventAsync(http.RequestServices.GetRequiredService<AppDbContext>(), OnlineContract.Helpers.EventType.Error, "Anon AddToCart failed", ex.ToString(), 0); } catch { }
        return Results.StatusCode(500);
    }
}).AllowAnonymous();

app.MapGet("/api/anon-cart/summary", async (OnlineContract.Services.AnonCartCacheService svc, CancellationToken ct) =>
{
    try
    {
        if (!svc.TryGetAnonId(out var anonId))
            return Results.Text(JsonSerializer.Serialize(new { itemCount = 0, items = Array.Empty<object>() }), "application/json");
        var items = await svc.GetAsync(anonId, ct);
        var count = items.Sum(i => i.Qty);
        var payload = new { itemCount = count, items = items.Select(i => new { variantId = i.VariantId, qty = i.Qty }) };
        return Results.Text(JsonSerializer.Serialize(payload), "application/json");
    }
    catch { return Results.Text(JsonSerializer.Serialize(new { itemCount = 0, items = Array.Empty<object>() }), "application/json"); }
}).AllowAnonymous();

app.MapDelete("/api/anon-cart", async (OnlineContract.Services.AnonCartCacheService svc, CancellationToken ct) =>
{
    try
    {
        if (svc.TryGetAnonId(out var anonId))
        {
            await svc.ClearAsync(anonId, ct);
        }
        return Results.NoContent();
    }
    catch { return Results.NoContent(); }
}).AllowAnonymous();

// Merge anon cart into authenticated draft (auth required)
app.MapPost("/api/cart/merge-anon", async (ClaimsPrincipal user, OnlineContract.Services.AnonCartCacheService anonSvc, OnlineContract.Services.CartMergeService mergeSvc, HttpContext http) =>
{
    try
    {
        var userIdClaim = user.FindFirst("sub") ?? user.FindFirst("userId") ?? user.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim is null) return Results.Unauthorized();
        if (!anonSvc.TryGetAnonId(out var anonId)) return Results.Ok();
        await mergeSvc.MergeAnonIntoUserDraftAsync(int.Parse(userIdClaim.Value), anonId, http.RequestAborted);
        return Results.Text("{\"success\":true}", "application/json");
    }
    catch (InvalidOperationException)
    {
        return Results.StatusCode(StatusCodes.Status400BadRequest);
    }
    catch (Exception ex)
    {
        try { await LoggerHelper.LogEventAsync(http.RequestServices.GetRequiredService<AppDbContext>(), OnlineContract.Helpers.EventType.Error, "Merge anon failed", ex.ToString(), 0); } catch { }
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// -------------------------
// Checkout route (server-side anon merge)
// -------------------------
static int? TryGetUserId(HttpContext ctx)
{
    var claim = ctx.User.FindFirst("sub")
        ?? ctx.User.FindFirst("userId")
        ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier);
    if (claim == null) return null;
    return int.TryParse(claim.Value, out var id) ? id : (int?)null;
}

app.MapGet("/checkout", async (HttpContext ctx, OnlineContract.Services.CartMergeService mergeSvc, OnlineContract.Services.AnonCartCacheService anonSvc) =>
{
    var userId = TryGetUserId(ctx);
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

// -------------------------
// Auth API
// -------------------------

app.MapPost("/api/login", async (AppDbContext db, LoginDto dto, HttpContext http) =>
{
    try
    {
        // find user by code (include inactive so we can give precise message when appropriate)
        var user = await db.AxUsers.FirstOrDefaultAsync(u => u.Code == dto.Code && !u.IsDeleted);
        if (user == null)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login failed - invalid user code", $"Code={dto.Code}", 2);
            return Results.Text(JsonSerializer.Serialize(new { success = false, message = "Invalid user code. Please check your credentials and try again." }), "application/json");
        }

        // verify password first so we can detect the case of correct credentials but deactivated account
        if (!PasswordHelper.VerifyPassword(dto.Password, user.Password))
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login failed - invalid password", $"Code={dto.Code}", user.Id);
            return Results.Text(JsonSerializer.Serialize(new { success = false, message = "Invalid password. Please check your credentials and try again." }), "application/json");
        }

        // disallow signing in with team/group accounts
        if (user.IsGroup)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login failed - team account attempted", $"Code={dto.Code}", user.Id);
            return Results.Text(JsonSerializer.Serialize(new { success = false, message = "Team accounts cannot be used to sign in. Please use a personal account or contact our administrator for access." }), "application/json");
        }

        // If user must change password, interrupt normal login and prompt client to change it now
        if (user.IsTempPassword)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login blocked - temp password requires change", $"Code={dto.Code}", user.Id);
            return Results.Text(JsonSerializer.Serialize(new { success = false, mustChangePassword = true, message = "Your account requires a password change before you can continue. Please set a new password now.", stamp = user.Stamp }), "application/json");
        }

        // correct credentials but account inactive -> return a friendly, specific message
        if (!user.IsActive)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login failed - deactivated account", $"Code={dto.Code}", user.Id);
            return Results.Text(JsonSerializer.Serialize(new { success = false, message = "Your account has been deactivated. If you need it reactivated, please contact our administrator." }), "application/json");
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Code ?? string.Empty),
            new Claim(ClaimTypes.Role, user.RoleId.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.Now.AddHours(8) });
        // record last-login time using server local time
        user.LastLoginDt = DateTime.Now;
        await db.SaveChangesAsync();

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Login successful", $"User {user.Code} logged in.", user.Id);
        return Results.Text(JsonSerializer.Serialize(new { success = true, userId = user.Id, roleId = user.RoleId }), "application/json");
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Login endpoint exception", ex.ToString(), 2);
        return Results.Text(JsonSerializer.Serialize(new { success = false, message = "Login failed due to a server error. Please try again later." }), "application/json");
    }
});

// Forgot password endpoints (initiate, validate token, perform reset)
app.MapPost("/api/auth/forgot-password", async (AppDbContext db, HttpContext http, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, OnlineContract.Services.IEmailService emailService) =>
{
    try
    {
        var body = await http.Request.ReadFromJsonAsync<System.Collections.Generic.Dictionary<string, string>>();
        var rawEmail = body != null && body.TryGetValue("email", out var e) ? e : "";
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var svc = new OnlineContract.Services.ForgotPasswordService(app.Configuration);
        var (status, message) = await svc.HandleAsync(db, rawEmail, ip, cache, emailService);
        return Results.Json(new { message }, statusCode: status);
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Forgot-password endpoint exception", ex.ToString(), 2);
        return Results.Json(new { message = "Failed to process forgot-password request." }, statusCode: 500);
    }
}).AllowAnonymous();

app.MapGet("/api/auth/reset-token/{token}", async (AppDbContext db, string token) =>
{
    try
    {
        var now = DateTime.Now;
        var pr = await db.PasswordResetTokens.FirstOrDefaultAsync(p => p.Token == token && !p.IsUsed && p.ExpiryDt > now);
        if (pr == null) return Results.Json(new { success = false, message = "This reset link is invalid or has expired. Please request a new password reset." });
        return Results.Json(new { success = true, code = pr.Code, email = pr.Email });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Reset-token check failed", ex.ToString(), 2);
        return Results.Json(new { success = false, message = "Failed to validate reset token." }, statusCode: 500);
    }
}).AllowAnonymous();

app.MapPost("/api/auth/reset-password", async (AppDbContext db, HttpContext http) =>
{
    try
    {
        var dto = await http.Request.ReadFromJsonAsync<System.Collections.Generic.Dictionary<string, string>>();
        var token = dto != null && dto.TryGetValue("token", out var t) ? t : "";
        var newPw = dto != null && dto.TryGetValue("newPassword", out var n) ? n : "";
        var conf = dto != null && dto.TryGetValue("confirmPassword", out var c) ? c : "";

        if (string.IsNullOrWhiteSpace(token)) return Results.Json(new { success = false, message = "Token is required." });
        if (string.IsNullOrWhiteSpace(newPw) || newPw != conf) return Results.Json(new { success = false, message = "Passwords do not match or are empty." });
        if (newPw.Length < 8 || !System.Text.RegularExpressions.Regex.IsMatch(newPw, "[A-Z]") || !System.Text.RegularExpressions.Regex.IsMatch(newPw, "\\d"))
            return Results.Json(new { success = false, message = "Password must be at least 8 characters, include one uppercase letter and one number." });

        var now = DateTime.Now;
        var pr = await db.PasswordResetTokens.FirstOrDefaultAsync(p => p.Token == token && !p.IsUsed && p.ExpiryDt > now);
        if (pr == null) return Results.Json(new { success = false, message = "This reset link is invalid or has expired." });

        if (!pr.UserId.HasValue) return Results.Json(new { success = false, message = "No user associated with this token." });
        var user = await db.AxUsers.FirstOrDefaultAsync(u => u.Id == pr.UserId.Value && !u.IsDeleted);
        if (user == null) return Results.Json(new { success = false, message = "User account not found." });

        user.Password = PasswordHelper.HashPassword(newPw);
        user.PasswordDt = DateTime.Now;
        user.IsTempPassword = false;
        user.LastLoginDt = DateTime.Now;
        user.Stamp = user.Stamp + 1;

        pr.IsUsed = true;
        pr.UsedDt = DateTime.Now;

        await db.SaveChangesAsync();

        // Sign in the user
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Code ?? string.Empty),
            new Claim(ClaimTypes.Role, user.RoleId.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.Now.AddHours(8) });

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Password reset completed", $"UserId={user.Id}", user.Id);
        return Results.Json(new { success = true, userId = user.Id, roleId = user.RoleId });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Reset-password failed", ex.ToString(), 2);
        return Results.Json(new { success = false, message = "Failed to reset password. Please try again later." });
    }
}).AllowAnonymous();

app.MapPost("/api/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { success = true, message = "You have been signed out successfully." });
});

app.MapPost("/api/register", async (AppDbContext db, RegisterDto dto, HttpContext http) =>
{
    try
    {
        var username = (dto.Username ?? "").Trim();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(dto.Password))
            return Results.Json(new { success = false, message = "Username and password are required. Please provide both to continue." });
        if (string.Equals(dto.Password, "passw0rd", StringComparison.Ordinal))
            return Results.Json(new { success = false, message = "Please choose a stronger password; the default password is not allowed." });

        var exists = await db.AxUsers.AnyAsync(u => u.Code == username);
        if (exists)
            return Results.Json(new { success = false, message = "Username already exists. Please choose a different username and try again." });

        var email = (dto.Email ?? "").Trim();
        if (string.IsNullOrWhiteSpace(email))
            return Results.Json(new { success = false, message = "Email is required. Please provide an email address." });

        var phoneRaw = (dto.Phone ?? "").Trim();
        if (string.IsNullOrWhiteSpace(phoneRaw))
            return Results.Json(new { success = false, message = "Phone is required. Please provide a phone number." });

        if (!IsValidEmail(email))
            return Results.Json(new { success = false, message = "Invalid email format. Please enter a valid email address." });

        exists = await db.AxUsers.AnyAsync(u => u.Email == email);
        if (exists)
            return Results.Json(new { success = false, message = "Email already exists. Please use a different email address." });

        var assignedRole = dto.RoleId ?? (int)UserRole.Customer;

        var (okPhone, normalizedPhone, phoneErr) = OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone(dto.Phone);
        if (!okPhone) return Results.Json(new { success = false, message = phoneErr });

        // find owner group (Customers role_id = 5, is_group = 1, active, not deleted)
        var ownerGroup = await db.AxUsers.FirstOrDefaultAsync(u => u.RoleId == (int)UserRole.Customer && u.IsGroup && u.IsActive && !u.IsDeleted);

        var newUser = new AxUser
        {
            FirstName = dto.FirstName ?? "",
            LastName = dto.LastName ?? "",
            Email = string.IsNullOrWhiteSpace(dto.Email) ? "" : dto.Email.Trim(),
            Phone = normalizedPhone ?? "",
            Code = username,
            Password = PasswordHelper.HashPassword(dto.Password ?? ""),
            IsGroup = false,
            IsTempPassword = dto.IsTempPassword,
            IsActive = true,
            IsDeleted = false,
            OwnerId = ownerGroup != null ? ownerGroup.Id : 0,
            CreatedDt = DateTime.Now,
            PasswordDt = DateTime.Now,
            LastLoginDt = DateTime.Now,
            City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City?.Trim(),
            StreetAddress = string.IsNullOrWhiteSpace(dto.StreetAddress) ? null : dto.StreetAddress?.Trim(),
            PostalCode = string.IsNullOrWhiteSpace(dto.PostalCode) ? null : dto.PostalCode?.Trim(),
            RoleId = assignedRole
        };

        // Set initial stamp: if temp password was requested, mark change
        newUser.Stamp = dto.IsTempPassword ? 1 : 0;

        db.AxUsers.Add(newUser);
        await db.SaveChangesAsync();

        // Sign in the newly created user immediately
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, newUser.Id.ToString()),
            new Claim(ClaimTypes.Name, newUser.Code ?? string.Empty),
            new Claim(ClaimTypes.Role, newUser.RoleId.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.Now.AddHours(8) });

        await LoggerHelper.LogEventAsync(db, EventType.Information, "New Account successfully created", $"User {username} created and signed in.", newUser.Id);
        return Results.Json(new { success = true, userId = newUser.Id, roleId = newUser.RoleId });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Account creation failed", ex.ToString(), 2);
        var msg = app.Environment.IsDevelopment() ? ex.ToString() : "Account creation failed.";
        return Results.Json(new { success = false, message = msg });
    }
});

// Health
app.MapGet("/api/login", () => Results.Json(new { status = "Login endpoint is alive." }));

// -------------------------
// Users & Teams API (Authorized)
// -------------------------


// ---- Users & Teams API (Authorized) ----
app.MapGet("/api/users", async (AppDbContext db, string? name, string? team, int page, int pageSize, int? userId, string? sortBy, string? sortDir) =>
{
    try
    {
        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        var q = db.AxUsers.AsNoTracking().AsQueryable();

        // exclude deleted + system user
        q = q.Where(u => !u.IsDeleted && u.Id > 0 && u.Id != 2);

        if (!string.IsNullOrWhiteSpace(name))
        {
            var n = name.Trim().ToLower();
            q = q.Where(u =>
                (u.FirstName ?? "").ToLower().Contains(n)
                || (u.LastName ?? "").ToLower().Contains(n)
                || (u.Code ?? "").ToLower().Contains(n));
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            var t = team.Trim().ToLower();
            q =
                from u in q
                join g in db.AxUsers.Where(x => x.IsGroup && !x.IsDeleted) on u.OwnerId equals g.Id into groups
                from g in groups.DefaultIfEmpty()
                where g != null && ((g.Code ?? "").ToLower().Contains(t)
                                     || ((g.FirstName ?? "") + " " + (g.LastName ?? "")).Trim().ToLower().Contains(t))
                select u;
        }

        // ❗ Return team code AND group full name for display
        var baseQuery =
            from u in q
            join g in db.AxUsers.AsNoTracking().Where(x => x.IsGroup && !x.IsDeleted) on u.OwnerId equals g.Id into groups
            from g in groups.DefaultIfEmpty()
            select new
            {
                id = u.Id,
                code = u.Code,
                firstName = u.FirstName,
                lastName = u.LastName,
                email = u.Email,
                phone = u.Phone,
                roleId = u.RoleId,
                isTempPassword = u.IsTempPassword,
                isActive = u.IsActive,
                isDeleted = u.IsDeleted,
                isGroup = u.IsGroup,
                ownerId = u.OwnerId,

                // Group column should show human-readable name (Code of the group user)
                groupName = g != null ? g.Code : null,
                stamp = u.Stamp
            };

        // Apply server-side sorting on AxUser before projection
        var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new OnlineContract.Helpers.SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
        var sortMap = new Dictionary<string, System.Linq.Expressions.Expression<Func<OnlineContract.Models.AxUser, object?>>> {
            { "id", u => u.Id },
            { "code", u => u.Code },
            { "firstName", u => u.FirstName },
            { "lastName", u => u.LastName },
            { "email", u => u.Email },
            { "roleId", u => u.RoleId },
            { "ownerId", u => u.OwnerId },
            { "isActive", u => u.IsActive },
            { "stamp", u => u.Stamp }
        };

        var totalCount = await baseQuery.CountAsync();

        // We need to apply ordering on the underlying AxUsers query, so reconstruct an ordered sequence
        var usersQuery = db.AxUsers.AsNoTracking().Where(u => !u.IsDeleted && u.Id > 0 && u.Id != 2);
        if (!string.IsNullOrWhiteSpace(name)) {
            var n = name.Trim().ToLower();
            usersQuery = usersQuery.Where(u => (u.FirstName ?? "").ToLower().Contains(n)
                || (u.LastName ?? "").ToLower().Contains(n)
                || (u.Code ?? "").ToLower().Contains(n));
        }
        if (!string.IsNullOrWhiteSpace(team)) {
            var t = team.Trim().ToLower();
            usersQuery = from u in usersQuery
                         join g in db.AxUsers.Where(x => x.IsGroup && !x.IsDeleted) on u.OwnerId equals g.Id into groups2
                         from g in groups2.DefaultIfEmpty()
                         where g != null && ((g.Code ?? "").ToLower().Contains(t) || (((g.FirstName ?? "") + " " + (g.LastName ?? "")).Trim().ToLower().Contains(t)))
                         select u;
        }

        var orderedUsers = usersQuery.ApplySort(sortSpec, sortMap, u => u.Id);

        var items = await (
            from u in orderedUsers
            join g in db.AxUsers.AsNoTracking().Where(x => x.IsGroup && !x.IsDeleted) on u.OwnerId equals g.Id into groups2
            from g in groups2.DefaultIfEmpty()
            select new
            {
                id = u.Id,
                code = u.Code,
                firstName = u.FirstName,
                lastName = u.LastName,
                email = u.Email,
                phone = u.Phone,
                roleId = u.RoleId,
                isTempPassword = u.IsTempPassword,
                isActive = u.IsActive,
                isDeleted = u.IsDeleted,
                isGroup = u.IsGroup,
                ownerId = u.OwnerId,
                groupName = g != null ? g.Code : null,
                stamp = u.Stamp
            })
            .Skip(Math.Max(0, (pageIndex - 1) * size))
            .Take(size)
            .ToListAsync();

        return Results.Json(new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size), sortBy = sortBy ?? "", sortDir = sortDir ?? "" });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Users fetch failed", ex.ToString(), userId ?? 2);
        return Results.Json(new { items = Array.Empty<object>() });
    }
}).RequireAuthorization();

// Change temporary password and sign-in (used when is_temp_password = 1)
app.MapPost("/api/users/change-temp-password", async (AppDbContext db, ChangeTempPasswordDto dto, HttpContext http) =>
{
    try
    {
        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrEmpty(code)) return Results.Json(new { success = false, message = "Username is required. Please enter your username." });

        var u = await db.AxUsers.FirstOrDefaultAsync(x => x.Code == code && !x.IsDeleted);
        if (u == null) return Results.Json(new { success = false, message = "User not found. Please check your username and try again." });

        if (!u.IsTempPassword)
            return Results.Json(new { success = false, message = "This account does not require a password change." });

        // Concurrency
        if (!dto.Stamp.HasValue || dto.Stamp.Value != u.Stamp)
        {
            return Results.Json(new { success = false, message = "Your session is out of date. Please retry the password change and try again." });
        }

        var pw = dto.NewPassword ?? "";
        var pw2 = dto.ConfirmPassword ?? "";
        if (string.IsNullOrWhiteSpace(pw)) return Results.Json(new { success = false, message = "Password is required. Please enter a new password." });
        if (!string.Equals(pw, pw2, System.StringComparison.Ordinal)) return Results.Json(new { success = false, message = "Passwords do not match. Please ensure both entries are identical." });
        if (pw.Length < 8) return Results.Json(new { success = false, message = "Password must be at least 8 characters long." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(pw, @"[A-Z]")) return Results.Json(new { success = false, message = "Password must contain at least one uppercase letter." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(pw, @"\d")) return Results.Json(new { success = false, message = "Password must contain at least one number." });

        // Do not allow reusing the existing password
        if (PasswordHelper.VerifyPassword(pw, u.Password))
            return Results.Json(new { success = false, message = "The new password cannot be the same as your current password. Please choose a different password." });

        // Apply password change
        u.Password = PasswordHelper.HashPassword(pw);
        u.PasswordDt = DateTime.Now;
        u.LastLoginDt = DateTime.Now;
        u.IsTempPassword = false;
        u.Stamp = u.Stamp + 1;
        await db.SaveChangesAsync();

        // Sign in
        var claims = new List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, u.Id.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, u.Code ?? string.Empty),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, u.RoleId.ToString())
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.Now.AddHours(8) });

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Temporary password changed and user signed in", $"User {u.Code}", u.Id);
        return Results.Json(new { success = true, userId = u.Id, roleId = u.RoleId });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Temp password change failed", ex.ToString(), 2);
        return Results.Json(new { success = false, message = "Failed to change password. Please try again later." });
    }
}).AllowAnonymous();


app.MapGet("/api/groups", async (AppDbContext db, HttpContext http, string? q, int page, int pageSize) =>
{
    // Server-side role gate: only Admin (7) or Manager (8)
    try
    {
        var rc = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!int.TryParse(rc, out var roleId) || (roleId != 7 && roleId != 8))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }
    catch
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    try
    {
        // Only return groups that are active and not deleted for dropdowns
        var query = db.AxUsers.Where(x => x.IsGroup && !x.IsDeleted && x.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(x =>
                (x.Code ?? "").ToLower().Contains(s) ||
                (x.FirstName ?? "").ToLower().Contains(s) ||
                (x.LastName ?? "").ToLower().Contains(s));
        }

        var totalCount = await query.CountAsync();
        var items = await query.OrderBy(x => x.Id)
                               .Skip(Math.Max(0, (page - 1) * pageSize))
                               .Take(pageSize)
                               .Select(x => new
                               {
                                   id = x.Id,
                                   code = x.Code,
                                   fullName = ((x.FirstName ?? "") + " " + (x.LastName ?? "")).Trim()
                               })
                               .ToListAsync();

        return Results.Json(new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Groups fetch failed", ex.ToString(), 2);
        return Results.Json(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

// WhoAmI
app.MapGet("/whoami", (HttpContext http) =>
{
    var user = http.User;
    var isAuth = user?.Identity?.IsAuthenticated ?? false;
    var name = user?.Identity?.Name;
    int roleId = 0;
    try
    {
        var roleClaim = user?.Claims.FirstOrDefault(c =>
            c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(roleClaim)) int.TryParse(roleClaim, out roleId);
    }
    catch { }

    return Results.Json(new
    {
        isAuthenticated = isAuth,
        name = name,
        roleId = roleId,
        claims = user?.Claims.Select(c => new { c.Type, c.Value }) ?? Enumerable.Empty<object>()
    });
}).RequireAuthorization();

// Create/Update/Activate/Deactivate/Delete user
app.MapPost("/api/users", async (AppDbContext db, UserCreateDto dto, int? userId) =>
{
    try
    {
        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrEmpty(code))
            return Results.Json(new { success = false, message = "User code is required. Please enter a valid code." });

        var exists = await db.AxUsers.AnyAsync(u => u.Code == code);
        if (exists)
            return Results.Json(new { success = false, message = "The provided code already exists. Please choose a different code." });

        // Require email and phone for create
        if (string.IsNullOrWhiteSpace(dto.Email))
            return Results.Json(new { success = false, message = "Email is required. Please provide an email address." });
        if (string.IsNullOrWhiteSpace(dto.Phone))
            return Results.Json(new { success = false, message = "Phone is required. Please provide a phone number." });

        var emailCandidate = dto.Email.Trim();
        if (!IsValidEmail(emailCandidate))
            return Results.Json(new { success = false, message = "Invalid email format. Please enter a valid email address." });

        // Ensure email is unique across users and teams
        var existsEmail = await db.AxUsers.AnyAsync(u => u.Email == emailCandidate);
        if (existsEmail)
            return Results.Json(new { success = false, message = "Email already exists. Please use a different email address." });

        var (okCreatePhone, normalizedCreatePhone, createPhoneErr) = OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone(dto.Phone);
        if (!okCreatePhone) return Results.Json(new { success = false, message = createPhoneErr });

        // Require password on create for non-team users and disallow weak default
        if (!dto.IsGroup)
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                return Results.Json(new { success = false, message = "Password is required. Please provide a password." });
            if (string.Equals(dto.Password, "passw0rd", StringComparison.Ordinal))
                return Results.Json(new { success = false, message = "Please choose a stronger password; the default password is not allowed." });
        }

        var u = new AxUser
        {
            Code = code,
            FirstName = dto.FirstName ?? "",
            LastName = dto.IsGroup ? "" : (dto.LastName ?? ""),
            Email = emailCandidate,
            Phone = normalizedCreatePhone ?? "",
            RoleId = dto.RoleId,
            IsGroup = dto.IsGroup,
            IsTempPassword = dto.IsTempPassword,
            OwnerId = dto.OwnerId,
            City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City?.Trim(),
            StreetAddress = string.IsNullOrWhiteSpace(dto.StreetAddress) ? null : dto.StreetAddress?.Trim(),
            PostalCode = string.IsNullOrWhiteSpace(dto.PostalCode) ? null : dto.PostalCode?.Trim(),
            IsActive = true,
            IsDeleted = false,
            CreatedDt = DateTime.Now,
            PasswordDt = DateTime.Now,
            LastLoginDt = null,
            Stamp = dto.IsTempPassword ? 1 : 0,
            Password = dto.IsGroup ? "" : PasswordHelper.HashPassword(dto.Password ?? ""),
            InputUserId = userId ?? 2
        };

        db.AxUsers.Add(u);
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, dto.IsGroup ? "Team created" : "User created", $"Code={u.Code}", userId ?? 2);
        return Results.Json(new { success = true, id = u.Id });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Create user failed", ex.ToString(), userId ?? 2);
        return Results.Json(new { success = false, message = "Failed to create the user. Please try again later." });
    }
}).RequireAuthorization();

app.MapPut("/api/users/{id}", async (AppDbContext db, int id, UserUpdateDto dto, int? userId) =>
{
    try
    {
        var u = await db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return Results.NotFound(new { message = "User not found. Please refresh the list and try again." });

        if (!dto.Stamp.HasValue || dto.Stamp.Value != u.Stamp)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "User update conflict - stamp mismatch", $"UserId={id}", userId ?? 2);
            return Results.Json(new { success = false, message = $"Your changes to user {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
        }

        // Require email and phone on update
        if (string.IsNullOrWhiteSpace(dto.Email))
            return Results.Json(new { success = false, message = "Email is required. Please provide an email address." });
        if (string.IsNullOrWhiteSpace(dto.Phone))
            return Results.Json(new { success = false, message = "Phone is required. Please provide a phone number." });

        var emailUpdate = dto.Email.Trim();
        if (!IsValidEmail(emailUpdate))
            return Results.Json(new { success = false, message = "Invalid email format. Please enter a valid email address." });

        var existsEmail = await db.AxUsers.AnyAsync(x => x.Email == emailUpdate && x.Id != id);
        if (existsEmail) return Results.Json(new { success = false, message = "Email already exists." });

        // Password on update: only validate/apply when the client provided a password field
        // Ignore common UI placeholders like "••••••••" or "********" which indicate the password was not changed.
        if (!u.IsGroup && dto.Password != null)
        {
            var pwd = dto.Password ?? "";
            if (pwd == "••••••••" || pwd == "********")
            {
                // client did not intend to change password; ignore
            }
            else
            {
                if (string.IsNullOrWhiteSpace(pwd))
                    return Results.Json(new { success = false, message = "Password is required when updating a user. Please provide a password." });
                if (string.Equals(pwd, "passw0rd", StringComparison.Ordinal))
                    return Results.Json(new { success = false, message = "Please choose a stronger password; the default password is not allowed." });
                // Apply password (hashed) immediately for clarity
                u.Password = PasswordHelper.HashPassword(pwd);
                u.PasswordDt = DateTime.Now;
            }
        }

        var changed = false;

        if (!string.IsNullOrWhiteSpace(dto.Code))
        {
            var newCode = dto.Code.Trim();
            if (!string.Equals(newCode, u.Code, StringComparison.Ordinal))
            {
                var existsCode = await db.AxUsers.AnyAsync(x => x.Code == newCode && x.Id != id);
                if (existsCode) return Results.Json(new { success = false, message = "Code already exists." });
                u.Code = newCode;
                changed = true;
            }
        }

        if (dto.FirstName != null)
        {
            u.FirstName = dto.FirstName.Trim();
            changed = true;
        }
        if (dto.LastName != null)
        {
            u.LastName = dto.LastName.Trim();
            changed = true;
        }
        if (dto.Email != null)
        {
            u.Email = dto.Email.Trim();
            changed = true;
        }
        if (dto.Phone != null)
        {
            var (okUpdPhone, normalizedUpdPhone, updErr) = OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone(dto.Phone);
            if (!okUpdPhone) return Results.Json(new { success = false, message = updErr });
            u.Phone = normalizedUpdPhone ?? "";
            changed = true;
        }
        if (dto.RoleId.HasValue)
        {
            u.RoleId = dto.RoleId.Value;
            changed = true;
        }
        if (dto.IsActive.HasValue)
        {
            u.IsActive = dto.IsActive.Value;
            changed = true;
        }
        if (dto.OwnerId.HasValue)
        {
            u.OwnerId = dto.OwnerId.Value;
            changed = true;
        }
        if (dto.City != null)
        {
            u.City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim();
            changed = true;
        }
        if (dto.StreetAddress != null)
        {
            u.StreetAddress = string.IsNullOrWhiteSpace(dto.StreetAddress) ? null : dto.StreetAddress.Trim();
            changed = true;
        }
        if (dto.PostalCode != null)
        {
            u.PostalCode = string.IsNullOrWhiteSpace(dto.PostalCode) ? null : dto.PostalCode.Trim();
            changed = true;
        }

        // Apply IsTempPassword only if client explicitly provided it
        if (dto.IsTempPassword.HasValue)
        {
            if (dto.IsTempPassword.Value != u.IsTempPassword)
            {
                u.IsTempPassword = dto.IsTempPassword.Value;
                changed = true;
            }
        }

        // If password was changed above, mark changed as well
        // (password assignment already performed earlier when dto.Password was present)
        // Note: when password was applied we didn't set changed; set it here conservatively if password differs
        // We assume password change happened when dto.Password != null and not placeholder
        if (!u.IsGroup && dto.Password != null)
        {
            var pwd = dto.Password ?? "";
            if (!(pwd == "••••••••" || pwd == "********"))
            {
                changed = true;
            }
        }

        if (changed)
        {
            u.Stamp = u.Stamp + 1;
        }
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "User updated", $"Code={u.Code}", userId ?? 2);
        return Results.Json(new { success = true, message = "User has been updated successfully." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Update user failed", ex.ToString(), userId ?? 2);
        return Results.Json(new { success = false });
    }
}).RequireAuthorization();

app.MapPost("/api/users/{id}/deactivate", async (AppDbContext db, int id, int? userId, int? stamp) =>
{
    try
    {
        var u = await db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return Results.NotFound(new { message = "User not found. The user may have been removed." });
        // optimistic concurrency: require client to supply current stamp
        if (!stamp.HasValue || stamp.Value != u.Stamp)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "User deactivate conflict - stamp mismatch", $"UserId={id}", userId ?? 2);
            return Results.Json(new { success = false, message = $"Your changes to user {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
        }
        u.IsActive = false;
        u.Stamp = u.Stamp + 1;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "User deactivated", $"Code={u.Code}", userId ?? 2);
        return Results.Ok(new { success = true, message = "User has been deactivated successfully." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Deactivate user failed", ex.ToString(), userId ?? 2);
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

app.MapPost("/api/users/{id}/activate", async (AppDbContext db, int id, int? userId, int? stamp) =>
{
    try
    {
        var u = await db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return Results.NotFound(new { message = "User not found. The user may have been removed." });
        // optimistic concurrency: require client to supply current stamp
        if (!stamp.HasValue || stamp.Value != u.Stamp)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "User activate conflict - stamp mismatch", $"UserId={id}", userId ?? 2);
            return Results.Json(new { success = false, message = $"Your changes to user {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
        }
        u.IsActive = true;
        u.Stamp = u.Stamp + 1;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "User activated", $"Code={u.Code}", userId ?? 2);
        return Results.Ok(new { success = true, message = "User has been activated successfully." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Activate user failed", ex.ToString(), userId ?? 2);
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

app.MapPost("/api/users/{id}/delete", async (AppDbContext db, int id, int? userId) =>
{
    try
    {
        var u = await db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return Results.NotFound(new { message = "User not found. The user may have been removed." });
        // Deactivate and mark deleted
        u.IsActive = false;
        u.IsDeleted = true;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "User deleted", $"Code={u.Code}", userId ?? 2);
        return Results.Ok(new { success = true, message = "User has been deleted successfully." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Delete user failed", ex.ToString(), userId ?? 2);
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// -------------------------
// EventLog + Store (as before)
// -------------------------

app.MapGet("/api/event-log", async (AppDbContext db, int userId, int type, DateTime? from, DateTime? to, int page, int pageSize, string? sortBy, string? sortDir) =>
{
    try
    {
        var query = db.EventLogs.AsQueryable();
        if (type > 0)
        {
            var mappedType = type == 1 ? 2 : type == 2 ? 3 : type == 3 ? 4 : type;
            query = query.Where(e => e.EventTypeId == mappedType);
        }
        if (from.HasValue) query = query.Where(e => e.InputDt >= from.Value);
        if (to.HasValue) query = query.Where(e => e.InputDt <= to.Value);

        var totalCount = await query.CountAsync();

        var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new OnlineContract.Helpers.SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
        var sortMap = new Dictionary<string, System.Linq.Expressions.Expression<Func<OnlineContract.Models.EventLog, object?>>> {
            { "id", e => e.EventLogId },
            { "type", e => e.EventTypeId },
            { "inputDt", e => e.InputDt },
            { "description", e => e.Description },
            { "user", e => e.UserId }
        };

        var ordered = sortSpec == null
            ? query.OrderByDescending(e => e.InputDt).ThenBy(e => e.EventLogId)
            : query.ApplySort(sortSpec, sortMap, e => e.EventLogId);

        var items = await (from e in ordered
                           join u in db.AxUsers on e.UserId equals u.Id into users
                           from u in users.DefaultIfEmpty()
                           select new
                           {
                               e.EventLogId,
                               e.EventTypeId,
                               e.InputDt,
                               e.Description,
                               u.Code,
                               e.StackTrace
                           })
                           .Skip((page - 1) * pageSize)
                           .Take(pageSize)
                           .Select(e => new
                           {
                               id = e.EventLogId,
                               type = e.EventTypeId == 2 ? "Information" : e.EventTypeId == 3 ? "Warning" : "Error",
                               date = e.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                               description = e.Description,
                               user = e.Code,
                               stackTrace = e.StackTrace
                           })
                           .ToListAsync();

        return Results.Json(new { items, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize), totalCount, sortBy = sortBy ?? "", sortDir = sortDir ?? "" });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "EventLog fetch failed", ex.ToString(), userId);
        return Results.Json(new { items = new object[0], totalPages = 0, totalCount = 0 });
    }
});

app.MapGet("/api/event-log/export", async (AppDbContext db, int userId, int type, DateTime? from, DateTime? to) =>
{
    try
    {
        var q =
            from e in db.EventLogs
            join u in db.AxUsers on e.UserId equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new
            {
                e.EventLogId,
                EventTypeId = e.EventTypeId,
                TypeName = e.EventTypeId == 2 ? "Information" : e.EventTypeId == 3 ? "Warning" : "Error",
                e.InputDt,
                e.Description,
                UserFullName = u != null ? (u.FirstName + " " + u.LastName).Trim() : $"User {e.UserId}",
                e.StackTrace
            };

        if (type == 0)
        {
            // UI 'All' should include Information(2), Warning(3) and Error(4)
            q = q.Where(x => x.EventTypeId == 2 || x.EventTypeId == 3 || x.EventTypeId == 4);
        }
        else if (type > 0)
        {
            var mappedType = type == 1 ? 2 : type == 2 ? 3 : type == 3 ? 4 : type;
            q = q.Where(x => x.EventTypeId == mappedType);
        }

        if (from.HasValue) q = q.Where(x => x.InputDt >= from.Value);
        if (to.HasValue) q = q.Where(x => x.InputDt <= to.Value);

        var logs = await q.OrderByDescending(x => x.InputDt).ToListAsync();

        // Build CSV with robust escaping to preserve newlines and commas inside fields (especially stack traces)
        string EscapeCsv(object? value)
        {
            if (value == null) return string.Empty;
            var s = value.ToString() ?? string.Empty;
            // Normalize CRLF to LF to avoid platform-specific issues
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            // Escape double-quotes
            s = s.Replace("\"", "\"\"");
            // If field contains comma, quote or newline, wrap in quotes
            if (s.IndexOfAny(new char[] { ',', '"', '\n' }) >= 0)
            {
                s = '"' + s + '"';
            }
            return s;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Id,Type,Date,Description,User,StackTrace");
        foreach (var e in logs)
        {
            sb.Append(EscapeCsv(e.EventLogId)); sb.Append(',');
            sb.Append(EscapeCsv(e.TypeName)); sb.Append(',');
            sb.Append(EscapeCsv(e.InputDt.ToString("yyyy-MM-dd HH:mm:ss"))); sb.Append(',');
            sb.Append(EscapeCsv(e.Description)); sb.Append(',');
            sb.Append(EscapeCsv(e.UserFullName)); sb.Append(',');
            sb.Append(EscapeCsv(e.StackTrace));
            sb.AppendLine();
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var fname = $"EventLog_{ts}.csv";
        return Results.File(bytes, "text/csv; charset=utf-8", fname);
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Export failed", ex.ToString(), userId);
        return Results.StatusCode(500);
    }
});

app.MapGet("/api/stores", async (AppDbContext db, int? userId, string? sortBy, string? sortDir) =>
{
    try
    {
        // Support server-side sorting: Sort -> Filter -> Paginate (stores grid is simple list)
        var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new SortSpec(sortBy, (sortDir ?? "").ToLowerInvariant() == "desc");

        var baseQuery = from s in db.Stores.AsNoTracking()
                        join u in db.AxUsers.AsNoTracking() on s.LastModifiedUserId equals u.Id into uu
                        from u in uu.DefaultIfEmpty()
                        select new
                        {
                            store = s,
                            lastUpdatedBy = u != null ? u.Code : null
                        };

        var map = new Dictionary<string, System.Linq.Expressions.Expression<Func<OnlineContract.Models.Store, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = s => s.StoreId,
            ["name"] = s => s.Name ?? string.Empty,
            ["address"] = s => s.Address ?? string.Empty,
            ["email"] = s => s.Email ?? string.Empty,
            ["phone"] = s => s.PhoneNumber ?? string.Empty,
            ["lastUpdatedBy"] = s => s.LastModifiedUserId
        };

        // Project to an anonymous type after applying ordering to the Store entity
        IQueryable<OnlineContract.Models.Store> storeQuery = db.Stores.AsNoTracking();
        if (sortSpec == null)
            storeQuery = storeQuery.OrderBy(s => s.StoreId);
        else
            storeQuery = storeQuery.ApplySort(sortSpec, map, s => s.StoreId);

        var items = await (from s in storeQuery
                           join u in db.AxUsers.AsNoTracking() on s.LastModifiedUserId equals u.Id into uu
                           from u in uu.DefaultIfEmpty()
                           select new
                           {
                               id = s.StoreId,
                               name = s.Name,
                               address = s.Address,
                               phone = s.PhoneNumber,
                               email = s.Email,
                               hours = s.WorkingHours,
                               lastUpdatedBy = u != null ? u.Code : null
                           }).ToListAsync();

        return Results.Json(new { items });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Stores endpoint failed", ex.ToString(), userId ?? 2);
        return Results.Json(new { items = Array.Empty<object>() });
    }
});

app.MapPut("/api/stores/{id}", async (AppDbContext db, int id, StoreUpdateDto dto, int? userId) =>
{
    try
    {
        var store = await db.Stores.FirstOrDefaultAsync(s => s.StoreId == id);
        if (store == null) return Results.NotFound(new { message = "Store not found. Please verify the store identifier and try again." });

        var name = dto.Name?.Trim();
        var address = dto.Address?.Trim();
        var phone = dto.PhoneNumber?.Trim();
        var email = dto.Email?.Trim();
        var hours = dto.WorkingHours?.Trim();

        if (name is not null) store.Name = name;
        if (address is not null) store.Address = address;
        if (phone is not null) store.PhoneNumber = phone;
        if (email is not null) store.Email = email;
        if (hours is not null) store.WorkingHours = hours;

        store.LastModifiedUserId = userId ?? 2;

        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Store details updated", $"StoreId={store.StoreId}, Name={store.Name}", userId ?? 2);
        return Results.Ok(new { success = true, message = "Store details have been saved successfully." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Update store failed", ex.ToString(), userId ?? 2);
        return Results.StatusCode(500);
    }
});

// Contracts API (Authorized)
app.MapGet("/api/contracts", async (AppDbContext db, HttpContext http, string? state, string? name, string? fromDate, string? toDate, int page, int pageSize, string? sortBy, string? sortDir) =>
{
    try
    {
        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        var roleClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        int.TryParse(roleClaim, out var roleId);
        var userIdClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out var currentUserId);
        var isCustomer = roleId == (int)UserRole.Customer;

        // Build base contracts query (entity) so we can apply server-side sorting before projection
        // For customers: restrict to own contracts that are active and not deleted
        var contractsQuery = db.Contracts.AsNoTracking().Where(c =>
            c.Id > 0 && (!isCustomer || ((c.InputUserId ?? 0) == currentUserId && c.IsActive && !c.IsDeleted)));

        var qUserJoin =
            from c in contractsQuery
            join u0 in db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
            from u in ug.DefaultIfEmpty()
            select new { Contract = c, User = u };

        if (!string.IsNullOrWhiteSpace(state) && Enum.TryParse<OnlineContract.Helpers.ContractState>(state, true, out var st))
        {
            contractsQuery = contractsQuery.Where(x => x.ContractState == st);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var n = name.Trim().ToLower();
            qUserJoin = qUserJoin.Where(x => ((x.User == null ? "" : ((x.User.FirstName ?? "") + " " + (x.User.LastName ?? "")).Trim()) ?? "").ToLower().Contains(n)
                                             || ((x.User == null ? "" : (x.User.Code ?? "")) ?? "").ToLower().Contains(n));
        }

        // Date range filters (EntryDate)
        if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fd))
        {
            contractsQuery = contractsQuery.Where(x => x.EntryDate >= fd);
            qUserJoin = qUserJoin.Where(x => x.Contract.EntryDate >= fd);
        }
        if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var td))
        {
            var tdEnd = td.Date.AddDays(1).AddTicks(-1);
            contractsQuery = contractsQuery.Where(x => x.EntryDate <= tdEnd);
            qUserJoin = qUserJoin.Where(x => x.Contract.EntryDate <= tdEnd);
        }

        var totalCount = await contractsQuery.CountAsync();

        // Apply server-side sorting on contracts entity
        var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new OnlineContract.Helpers.SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
        var sortMap = new Dictionary<string, System.Linq.Expressions.Expression<Func<OnlineContract.Models.Contract, object?>>> {
            { "id", c => c.Id },
            { "entryDate", c => c.EntryDate },
            { "amount", c => c.Amount },
            { "contractState", c => c.ContractState }
        };

        IQueryable<OnlineContract.Models.Contract> orderedContracts;
        if (sortSpec == null)
        {
            orderedContracts = contractsQuery.OrderByDescending(c => c.EntryDate).ThenBy(c => c.Id);
        }
        else if (string.Equals(sortSpec.By, "customerFullName", StringComparison.OrdinalIgnoreCase))
        {
            // Sort by customer's full name (join via subquery). Stable secondary sort by Id.
            if (sortSpec.Desc)
            {
                orderedContracts = contractsQuery
                    .OrderByDescending(c => (db.AxUsers
                        .Where(u => u.Id == (c.InputUserId ?? 0))
                        .Select(u => (((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()))
                        .FirstOrDefault()) ?? "")
                    .ThenBy(c => c.Id);
            }
            else
            {
                orderedContracts = contractsQuery
                    .OrderBy(c => (db.AxUsers
                        .Where(u => u.Id == (c.InputUserId ?? 0))
                        .Select(u => (((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()))
                        .FirstOrDefault()) ?? "")
                    .ThenBy(c => c.Id);
            }
        }
        else
        {
            orderedContracts = contractsQuery.ApplySort(sortSpec, sortMap, c => c.Id);
        }

        var pageRows = await (
            from c in orderedContracts
            join u0 in db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug2
            from u in ug2.DefaultIfEmpty()
            where !isCustomer || ((c.InputUserId ?? 0) == currentUserId && c.IsActive && !c.IsDeleted)
            select new
            {
                c.Id,
                c.EntryDate,
                c.ContractState,
                c.Amount,
                c.AmtMatched,
                c.DeliveredDt,
                c.WrittenOffDt,
                c.RejectedDt,
                c.CancelledDt,
                CustomerFullName = u == null
                    ? ""
                    : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                CustomerCode = u == null ? "" : (u.Code ?? "")
            })
            .Skip(Math.Max(0, (pageIndex - 1) * size))
            .Take(size)
            .ToListAsync();

        // Helper to format dates with "1900-01-01" sentinel as empty
        static string FmtDt(DateTime? d)
        {
            if (!d.HasValue) return "";
            var v = d.Value;
            if (v.Year == 1900 && v.Month == 1 && v.Day == 1) return "";
            return v.ToString("yyyy-MM-dd HH:mm:ss");
        }

        // Resolve human-readable contract state via lookup_set; fall back to enum text if not available
        var items = new List<object>();
        foreach (var x in pageRows)
        {
            string stateText = x.ContractState.ToString();
            try
            {
                stateText = await OnlineContract.Helpers.LookupHelper.GetLookupValueAsync(db, (int)x.ContractState);
            }
            catch { /* fallback already set */ }

            items.Add(new
            {
                id = x.Id,
                customerFullName = x.CustomerFullName,
                amount = x.Amount,
                amtMatched = x.AmtMatched,
                contractState = x.ContractState.ToString(),
                contractStateText = stateText,
                entryDate = x.EntryDate.ToString("yyyy-MM-dd HH:mm:ss"),
                deliveredDate = FmtDt(x.DeliveredDt),
                writtenOffDate = FmtDt(x.WrittenOffDt),
                rejectedDate = FmtDt(x.RejectedDt),
                cancelledDate = FmtDt(x.CancelledDt)
            });
        }

        return StableJson(new
        {
            items,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)size),
            sortBy = sortBy ?? "",
            sortDir = sortDir ?? ""
        });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, OnlineContract.Helpers.EventType.Error, "Contracts fetch failed", ex.ToString(), 2);
        return StableJson(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

app.MapGet("/api/contracts/{id:int}", async (AppDbContext db, HttpContext http, int id) =>
{
    if (id <= 0) return StableJsonStatus(new { message = "Contract not found. Please verify the contract ID and try again." }, StatusCodes.Status404NotFound);

    var roleClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
    int.TryParse(roleClaim, out var roleId);
    var userIdClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
    int.TryParse(userIdClaim, out var currentUserId);
    var isCustomer = roleId == (int)UserRole.Customer;

    var contractEntity = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
    if (contractEntity == null) return StableJsonStatus(new { message = "Contract not found. The contract may have been removed." }, StatusCodes.Status404NotFound);
    if (isCustomer && (contractEntity.InputUserId ?? 0) != currentUserId) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var row = await (
        from c in db.Contracts.AsNoTracking()
        join u0 in db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
        from u in ug.DefaultIfEmpty()
        join u1 in db.AxUsers.AsNoTracking() on c.LastModifiedById equals (int?)u1.Id into ug1
        from lm in ug1.DefaultIfEmpty()
        where c.Id == id
        select new
        {
            c.Id,
            c.EntryDate,
            c.InputUserId,
            c.ContractState,
            c.LastModifiedById,
            c.LastUpdatedDt,
            c.Stamp,
            c.Amount,
            c.AmtMatched,
            CustomerFullName = u == null
                ? ""
                : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
            InputUserCode = u == null ? "" : (u.Code ?? ""),
            LastModifiedByCode = lm == null ? "" : (lm.Code ?? "")
        }
    ).FirstOrDefaultAsync();

    if (row == null)
        return StableJsonStatus(new { message = "Contract not found. The contract may have been removed." }, StatusCodes.Status404NotFound);

    string contractStateText = row.ContractState.ToString();
    try { contractStateText = await OnlineContract.Helpers.LookupHelper.GetLookupValueAsync(db, (int)row.ContractState); } catch {}

    return StableJson(new
    {
        id = row.Id,
        customerFullName = row.CustomerFullName,
        inputUserId = row.InputUserId,
        contractState = row.ContractState.ToString(),
        contractStateText,
        entryDate = row.EntryDate.ToString("yyyy-MM-dd HH:mm:ss"),
        lastModifiedById = row.LastModifiedById,
        inputUserCode = row.InputUserCode,
        lastModifiedByCode = row.LastModifiedByCode,
        lastUpdatedDt = row.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss"),
        stamp = row.Stamp,
        amount = row.Amount,
        amtMatched = row.AmtMatched
    });
}).RequireAuthorization();

// Contract items (read-only list for details page)
app.MapGet("/api/contracts/{id:int}/items", async (AppDbContext db, HttpContext http, int id, int page, int pageSize) =>
{
    if (id <= 0) return StableJsonStatus(new { message = "Contract not found. Please verify the contract ID and try again." }, StatusCodes.Status404NotFound);

    var roleClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
    int.TryParse(roleClaim, out var roleId);
    var userIdClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
    int.TryParse(userIdClaim, out var currentUserId);
    var isCustomer = roleId == (int)UserRole.Customer;

    var contractEntity = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
    if (contractEntity == null) return StableJsonStatus(new { message = "Contract not found. The contract may have been removed." }, StatusCodes.Status404NotFound);
    if (isCustomer && (contractEntity.InputUserId ?? 0) != currentUserId) return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        var baseQ = db.ContractDets.AsNoTracking().Where(d => d.ContractId == id && !d.IsDeleted);
        var totalCount = await baseQ.CountAsync();

        var rows = await (
            from d in baseQ
            join v in db.ProductVariants.AsNoTracking() on d.ProductVariantId equals v.Id
            orderby d.Id
            select new
            {
                d.Id,
                d.ProductName,
                d.Size,
                d.Color,
                d.Quantity,
                d.Amount,
                d.AmtGross,
                d.ItemStateId,
                d.InputDt,
                d.IsActive,
                d.ProductVariantId,
                d.Stamp,
                ProductId = v.ProductId
            })
            .Skip(Math.Max(0, (pageIndex - 1) * size))
            .Take(size)
            .ToListAsync();

        var items = new List<object>();
        foreach (var r in rows)
        {
            string stateText = r.ItemStateId.ToString();
            try { stateText = await OnlineContract.Helpers.LookupHelper.GetLookupValueAsync(db, (int)r.ItemStateId); } catch { /* fallback already set */ }
            string? photoFileName = null;
            try
            {
                photoFileName = await db.ProductVariants.AsNoTracking()
                    .Where(v => v.Id == r.ProductVariantId)
                    .Select(v => v.PhotoFileName)
                    .FirstOrDefaultAsync();
            }
            catch { }
            items.Add(new
            {
                id = r.Id,
                productName = r.ProductName ?? "",
                size = r.Size ?? "",
                color = r.Color ?? "",
                quantity = r.Quantity,
                amount = r.Amount,
                amtGross = r.AmtGross,
                itemStateId = r.ItemStateId,
                itemStateText = stateText,
                inputDt = r.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                isActive = r.IsActive,
                photoFileName,
                productId = r.ProductId,
                stamp = r.Stamp
            });
        }

        return StableJson(new
        {
            items,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)size)
        });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, OnlineContract.Helpers.EventType.Error, "Contract items fetch failed", ex.ToString(), 2);
        return StableJson(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

// Contract items delete (Draft only, owner-only)
app.MapPost("/api/contracts/{id:int}/items/{itemId:int}/delete", async (AppDbContext db, HttpContext http, int id, int itemId) =>
{
    if (id <= 0 || itemId <= 0) return Results.StatusCode(StatusCodes.Status404NotFound);

    try
    {
        var roleClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        int.TryParse(roleClaim, out var roleId);
        var userIdClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out var currentUserId);
        var isCustomer = roleId == (int)UserRole.Customer;
        if (!isCustomer) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (contract == null) return Results.StatusCode(StatusCodes.Status404NotFound);
        if ((contract.InputUserId ?? 0) != currentUserId) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (contract.ContractState != ContractState.Draft) return Results.StatusCode(StatusCodes.Status409Conflict);

        await using var tx = await db.Database.BeginTransactionAsync();

        var det = await db.ContractDets.FirstOrDefaultAsync(d => d.Id == itemId && d.ContractId == id && !d.IsDeleted);
        if (det == null) { await tx.RollbackAsync(); return Results.StatusCode(StatusCodes.Status404NotFound); }

        // Soft-delete detail (respect DB CHECK on active/deleted exclusivity)
        det.IsDeleted = true;
        det.IsActive = false;
        det.LastModifiedById = currentUserId;
        det.LastUpdatedDt = DateTime.Now;
        det.Stamp = det.Stamp + 1;
        await db.SaveChangesAsync();

        // Recompute header amount from remaining active/not-deleted lines as sum of AmtGross
        var newAmount = await db.ContractDets.AsNoTracking()
            .Where(d => d.ContractId == id && d.IsActive && !d.IsDeleted)
            .Select(d => (decimal?)d.AmtGross)
            .SumAsync() ?? 0m;
        contract.Amount = newAmount;
        contract.LastModifiedById = currentUserId;
        contract.LastUpdatedDt = DateTime.Now;
        contract.Stamp = contract.Stamp + 1;
        await db.SaveChangesAsync();

        // If this was the last active detail, soft-delete the contract header as well
        var remainingActive = await db.ContractDets.AsNoTracking()
            .CountAsync(d => d.ContractId == id && d.IsActive && !d.IsDeleted);
        if (remainingActive == 0)
        {
            contract.IsDeleted = true;
            contract.IsActive = false;
            contract.LastModifiedById = currentUserId;
            contract.LastUpdatedDt = DateTime.Now;
            contract.Stamp = contract.Stamp + 1;
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Contract item deleted", $"ContractId={id}; ItemId={itemId}", currentUserId);
        return StableJson(new { success = true, message = "The item was deleted successfully." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Contract item delete failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Save contract (Draft only, owner-only). No-op that bumps audit/stamp.
app.MapPost("/api/contracts/{id:int}/save", async (AppDbContext db, HttpContext http, int id) =>
{
    if (id <= 0) return Results.StatusCode(StatusCodes.Status404NotFound);
    try
    {
        var roleClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        int.TryParse(roleClaim, out var roleId);
        var userIdClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out var currentUserId);
        var isCustomer = roleId == (int)UserRole.Customer;
        if (!isCustomer) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (contract == null) return Results.StatusCode(StatusCodes.Status404NotFound);
        if ((contract.InputUserId ?? 0) != currentUserId) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (contract.ContractState != ContractState.Draft) return Results.StatusCode(StatusCodes.Status409Conflict);

        // Optional concurrency check: client can pass stamp in body
        try
        {
            var body = await http.Request.ReadFromJsonAsync<Dictionary<string, int?>>();
            if (body != null && body.TryGetValue("contractStamp", out var cs) && cs.HasValue && contract.Stamp != cs.Value)
                return Results.StatusCode(StatusCodes.Status409Conflict);
        }
        catch { }

        // Re-validate stock for all active lines (standardized message)
        var dets = await db.ContractDets.Where(d => d.ContractId == id && !d.IsDeleted).ToListAsync();
        foreach (var d in dets)
        {
            var ok = await new OnlineContract.Services.VariantAvailabilityService(db)
                .CheckVariantAvailabilityAsync(d.ProductVariantId, d.Quantity);
            if (!ok)
            {
                var available = await db.ProductInventories.AsNoTracking()
                    .Where(i => i.ProductVariantId == d.ProductVariantId && i.IsActive && !i.IsDeleted)
                    .Select(i => (int?)i.QtyOnHand).SumAsync() ?? 0;
                return StableJson(new { success = false, message = $"Insufficient stock: requested {d.Quantity}, available {available}." });
            }
        }

        // Recompute header amount as sum of AmtGross for active/not-deleted lines
        var newAmount = await db.ContractDets.AsNoTracking()
            .Where(d => d.ContractId == id && d.IsActive && !d.IsDeleted)
            .Select(d => (decimal?)d.AmtGross)
            .SumAsync() ?? 0m;
        contract.Amount = newAmount;

        contract.LastModifiedById = currentUserId;
        contract.LastUpdatedDt = DateTime.Now;
        contract.Stamp = contract.Stamp + 1;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Contract saved", $"ContractId={id}", currentUserId);
        return StableJson(new { success = true, message = "Your changes have been saved successfully." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Contract save failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Edit contract item (Draft only, owner-only) with variant+stock validation and concurrency checks
app.MapPost("/api/contracts/{id:int}/items/{itemId:int}/edit", async (
    AppDbContext db,
    HttpContext http,
    OnlineContract.Services.VariantAvailabilityService stockSvc,
    int id,
    int itemId) =>
{
    if (id <= 0 || itemId <= 0) return Results.StatusCode(StatusCodes.Status404NotFound);
    try
    {
        var roleClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        int.TryParse(roleClaim, out var roleId);
        var userIdClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out var currentUserId);
        var isCustomer = roleId == (int)UserRole.Customer;
        if (!isCustomer) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var dto = await http.Request.ReadFromJsonAsync<OnlineContract.Dtos.ContractItemUpdateDto>();
        if (dto == null) return Results.StatusCode(StatusCodes.Status400BadRequest);
        if (dto.ItemId != 0 && dto.ItemId != itemId) return Results.StatusCode(StatusCodes.Status400BadRequest);
        if (dto.Quantity <= 0) return StableJson(new { success = false, message = "Quantity must be greater than 0." });

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (contract == null) return Results.StatusCode(StatusCodes.Status404NotFound);
        if ((contract.InputUserId ?? 0) != currentUserId) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (contract.ContractState != ContractState.Draft) return Results.StatusCode(StatusCodes.Status409Conflict);
        if (contract.Stamp != dto.ContractStamp) return Results.StatusCode(StatusCodes.Status409Conflict);

        var det = await db.ContractDets.FirstOrDefaultAsync(d => d.Id == itemId && d.ContractId == id && !d.IsDeleted);
        if (det == null) return Results.StatusCode(StatusCodes.Status404NotFound);
        if (det.Stamp != dto.ItemStamp) return Results.StatusCode(StatusCodes.Status409Conflict);

        // Resolve product
        var existingVariant = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == det.ProductVariantId);
        if (existingVariant == null) return Results.StatusCode(StatusCodes.Status404NotFound);
        var productId = existingVariant.ProductId;

        // Variant and stock validation for requested size/color/qty
        var vr = await stockSvc.CheckVariantAvailabilityAsync(productId, dto.Color ?? string.Empty, dto.Size ?? string.Empty, dto.Quantity, dto.StoreId, http.RequestAborted);
        if (vr.FoundVariantId <= 0)
        {
            return StableJson(new { success = false, message = "Selected color/size variant is not available." });
        }
        if (!vr.IsAvailable)
        {
            var err = vr.Errors.FirstOrDefault() ?? $"Insufficient stock: requested {dto.Quantity}, available {vr.AvailableQty}.";
            return StableJson(new { success = false, message = err });
        }

        // Apply edits: do not change Amount (unit price). Update variant, qty, size, color, audit, stamp
        det.ProductVariantId = vr.FoundVariantId;
        det.Quantity = dto.Quantity;
        det.Size = (dto.Size ?? string.Empty).Trim();
        det.Color = (dto.Color ?? string.Empty).Trim();
        det.LastModifiedById = currentUserId;
        det.LastUpdatedDt = DateTime.Now;
        det.Stamp = det.Stamp + 1;

        // Persist the line change first so computed AmtGross reflects the new quantity
        await db.SaveChangesAsync();

        // Recompute header amount: sum of AmtGross for active/not-deleted lines
        var newAmount = await db.ContractDets.AsNoTracking()
            .Where(d => d.ContractId == id && d.IsActive && !d.IsDeleted)
            .Select(d => (decimal?)d.AmtGross)
            .SumAsync() ?? 0m;
        contract.Amount = newAmount;
        contract.LastModifiedById = currentUserId;
        contract.LastUpdatedDt = DateTime.Now;
        contract.Stamp = contract.Stamp + 1;

        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Contract item edited", $"ContractId={id}; ItemId={itemId}; VariantId={det.ProductVariantId}", currentUserId);
        return StableJson(new { success = true, message = "Item has been successfully updated.", stamp = det.Stamp, contractStamp = contract.Stamp });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Contract item edit failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Payment webhook (idempotent) to mark AmtMatched when payment succeeds
app.MapPost("/api/payments/webhook", async (AppDbContext db, HttpContext http) =>
{
    try
    {
        var payload = await http.Request.ReadFromJsonAsync<Dictionary<string, object?>>();
        if (payload == null) return Results.StatusCode(StatusCodes.Status400BadRequest);
        var status = (payload.TryGetValue("status", out var s) ? (s?.ToString() ?? "") : "").Trim().ToLowerInvariant();
        if (status != "success") return Results.Ok(new { ignored = true });
        if (!payload.TryGetValue("contractId", out var cidObj)) return Results.StatusCode(StatusCodes.Status400BadRequest);
        if (!int.TryParse(cidObj?.ToString(), out var contractId) || contractId <= 0) return Results.StatusCode(StatusCodes.Status400BadRequest);

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId && !c.IsDeleted);
        if (contract == null) return Results.StatusCode(StatusCodes.Status404NotFound);
        // Idempotent: only update if not already fully matched
        if (contract.AmtMatched < contract.Amount)
        {
            contract.AmtMatched = contract.Amount;
            contract.LastUpdatedDt = DateTime.Now;
            contract.Stamp = contract.Stamp + 1;
            await db.SaveChangesAsync();
            await LoggerHelper.LogEventAsync(db, EventType.Information, "Payment matched", $"ContractId={contractId}; AmtMatched={contract.AmtMatched}", 2);
        }
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Payment webhook failed", ex.ToString(), 2);
        return Results.StatusCode(500);
    }
});

// WSPay/Monri callback (idempotent): expects { status: 'success'|'failure', contractId: number }
app.MapPost("/api/payments/wspay/callback", async (AppDbContext db, HttpContext http) =>
{
    try
    {
        var payload = await http.Request.ReadFromJsonAsync<Dictionary<string, object?>>();
        if (payload == null) return Results.StatusCode(StatusCodes.Status400BadRequest);
        var status = (payload.TryGetValue("status", out var s) ? (s?.ToString() ?? "") : "").Trim().ToLowerInvariant();
        if (!payload.TryGetValue("contractId", out var cidObj)) return Results.StatusCode(StatusCodes.Status400BadRequest);
        if (!int.TryParse(cidObj?.ToString(), out var contractId) || contractId <= 0) return Results.StatusCode(StatusCodes.Status400BadRequest);

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId && !c.IsDeleted);
        if (contract == null) return Results.StatusCode(StatusCodes.Status404NotFound);

        if (status == "success")
        {
            // If already matched and submitted, no-op
            if (contract.ContractState == ContractState.Submitted && contract.AmtMatched >= contract.Amount)
                return StableJson(new { success = true, idempotent = true });

            var dets = await db.ContractDets.Where(d => d.ContractId == contractId && !d.IsDeleted).ToListAsync();
            foreach (var d in dets)
            {
                d.ItemStateId = ProductStateInOrder.Submitted;
                d.LastUpdatedDt = DateTime.Now;
                d.Stamp = d.Stamp + 1;
            }
            // Recompute header amount by summing per-line gross with rounding
            decimal newAmount = 0m;
            foreach (var ln in dets)
            {
                var net = ln.Amount * ln.Quantity;
                var tax = Math.Round(net * 0.20m, 2, MidpointRounding.AwayFromZero);
                var gross = Math.Round(net + tax, 2, MidpointRounding.AwayFromZero);
                newAmount += gross;
            }
            contract.Amount = newAmount;
            contract.ContractState = ContractState.Submitted;
            contract.AmtMatched = contract.Amount;
            contract.LastUpdatedDt = DateTime.Now;
            contract.Stamp = contract.Stamp + 1;
            await db.SaveChangesAsync();
            await LoggerHelper.LogEventAsync(db, EventType.Information, "WSPay payment matched", $"ContractId={contractId}; AmtMatched={contract.AmtMatched}", 2);
            return StableJson(new { success = true });
        }
        else
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "WSPay payment failed", $"ContractId={contractId}", 2);
            return StableJson(new { success = false, message = "Payment failed. Please try again." });
        }
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "WSPay callback failed", ex.ToString(), 2);
        return Results.StatusCode(500);
    }
});

// Submit contract (Draft only, owner-only). method=cod|online
app.MapPost("/api/contracts/{id:int}/submit", async (AppDbContext db, HttpContext http, OnlineContract.Services.VariantAvailabilityService stockSvc, int id, string? method) =>
{
    if (id <= 0) return Results.StatusCode(StatusCodes.Status404NotFound);
    try
    {
        var roleClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        int.TryParse(roleClaim, out var roleId);
        var userIdClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out var currentUserId);
        var isCustomer = roleId == (int)UserRole.Customer;
        if (!isCustomer) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (contract == null) return Results.StatusCode(StatusCodes.Status404NotFound);
        if ((contract.InputUserId ?? 0) != currentUserId) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (contract.ContractState != ContractState.Draft) return Results.StatusCode(StatusCodes.Status409Conflict);

        // Recompute header amount: sum of AmtGross for active/not-deleted lines
        var sumAmount = await db.ContractDets.AsNoTracking()
            .Where(d => d.ContractId == id && d.IsActive && !d.IsDeleted)
            .Select(d => (decimal?)d.AmtGross)
            .SumAsync() ?? 0m;
        contract.Amount = sumAmount;

        // Validate variant availability for each non-deleted item before submission
        var dets = await db.ContractDets.Where(d => d.ContractId == id && !d.IsDeleted).ToListAsync();
        foreach (var d in dets)
        {
            var ok = await stockSvc.CheckVariantAvailabilityAsync(d.ProductVariantId, d.Quantity);
            if (!ok)
            {
                // Compute available quantity to return standardized message
                var available = await db.ProductInventories.AsNoTracking()
                    .Where(i => i.ProductVariantId == d.ProductVariantId && i.IsActive && !i.IsDeleted)
                    .Select(i => (int?)i.QtyOnHand).SumAsync() ?? 0;
                return StableJson(new { success = false, message = $"Insufficient stock: requested {d.Quantity}, available {available}." });
            }
        }

        // Validate profile completeness (after stock check)
        var user = await db.AxUsers.FirstOrDefaultAsync(u => u.Id == currentUserId && !u.IsDeleted);
        if (user == null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(user.Email)) errors["email"] = "Email is required.";
        else if (!user.Email.Contains('@')) errors["email"] = "Email format is invalid.";
        var phone = user.Phone ?? "";
        var (okPhone, _, phoneErr) = PhoneHelper.NormalizeSerbianPhone(phone);
        if (!okPhone) errors["phoneNumber"] = phoneErr ?? "Phone number format is invalid.";
        if (string.IsNullOrWhiteSpace(user.City)) errors["city"] = "City is required.";
        if (string.IsNullOrWhiteSpace(user.StreetAddress)) errors["streetAddress"] = "Street address is required.";
        var postal = (user.PostalCode ?? "").Trim();
        if (postal.Length != 5) errors["postalCode"] = "Postal code must be exactly 5 characters.";
        if (errors.Count > 0)
        {
            var msg = errors.Count == 1 ? errors.Values.First() : "Please review your profile details and try again.";
            return StableJson(new { success = false, message = msg, errors });
        }

        // Bind method from query when not provided by minimal API binder
        string? mRaw = method;
        if (string.IsNullOrWhiteSpace(mRaw))
        {
            try { mRaw = http.Request.Query["method"].ToString(); } catch { mRaw = ""; }
        }
        var m = (mRaw ?? "").Trim().ToLowerInvariant();
        if (m == "cod")
        {
            // Transactional: update details and header atomically
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var d in dets)
            {
                d.ItemStateId = ProductStateInOrder.Submitted;
                d.LastModifiedById = currentUserId;
                d.LastUpdatedDt = DateTime.Now;
                d.Stamp = d.Stamp + 1;
            }

            contract.ContractState = ContractState.Submitted;
            contract.LastModifiedById = currentUserId;
            contract.LastUpdatedDt = DateTime.Now;
            contract.Stamp = contract.Stamp + 1;
            contract.AmtMatched = 0m;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            await LoggerHelper.LogEventAsync(db, EventType.Information, "Contract submitted (COD)", $"ContractId={id}", currentUserId);
            return StableJson(new { success = true, message = "Your order was submitted successfully. We’ll contact you shortly." });
        }
        else if (m == "online")
        {
            // Initiate payment; keep contract in Draft until callback confirms
            contract.LastModifiedById = currentUserId;
            contract.LastUpdatedDt = DateTime.Now;
            contract.Stamp = contract.Stamp + 1; // track submission attempt
            await db.SaveChangesAsync();
            await LoggerHelper.LogEventAsync(db, EventType.Information, "Contract payment initiated (Online)", $"ContractId={id}", currentUserId);
            return StableJson(new { success = true, message = "Payment initiated. You'll be notified upon confirmation.", paymentStatus = "initiated", reference = id });
        }
        else
        {
            return StableJson(new { success = false, message = "Unknown submit method. Use 'cod' or 'online'." });
        }
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Contract submit failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Change State modal data for a specific contract item
app.MapGet("/api/contracts/items/{contractDetId:int}/state/modal-data", async (AppDbContext db, HttpContext http, int contractDetId, CancellationToken ct) =>
{
    try
    {
        var svc = new OnlineContract.Services.ContractItemWorkflowService(db);
        var dto = await svc.GetModalDataAsync(contractDetId, ct);
        return StableJson(dto);
    }
    catch (KeyNotFoundException)
    {
        return Results.StatusCode(StatusCodes.Status404NotFound);
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, OnlineContract.Helpers.EventType.Error, "Contract item modal data failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Set state for a specific contract item
app.MapPost("/api/contracts/items/{contractDetId:int}/state/set", async (AppDbContext db, HttpContext http, int contractDetId, CancellationToken ct) =>
{
    try
    {
        var bodyDto = await http.Request.ReadFromJsonAsync<OnlineContract.Dtos.SetStateDto>(cancellationToken: ct);
        if (bodyDto == null) return Results.StatusCode(StatusCodes.Status400BadRequest);

        var claimUid = GetCurrentUserId(http);
        var svc = new OnlineContract.Services.ContractItemWorkflowService(db);
        var (newId, newName) = await svc.SetStateAsync(contractDetId, bodyDto.NextStateId, claimUid, ct);
        return StableJson(new { updated = true, contractDetId, newStateId = newId, newStateName = newName });
    }
    catch (InvalidOperationException)
    {
        return Results.StatusCode(StatusCodes.Status400BadRequest);
    }
    catch (KeyNotFoundException)
    {
        return Results.StatusCode(StatusCodes.Status404NotFound);
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, OnlineContract.Helpers.EventType.Error, "Contract item set state failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

app.MapGet("/api/contracts/export", async (AppDbContext db, HttpContext http, string? state, string? name, string? fromDate, string? toDate) =>
{
    try
    {
        var roleClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        int.TryParse(roleClaim, out var roleId);
        var userIdClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out var currentUserId);
        var isCustomer = roleId == (int)UserRole.Customer;

        var q =
            from c in db.Contracts.AsNoTracking()
            join u0 in db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
            from u in ug.DefaultIfEmpty()
            where c.Id > 0 && (!isCustomer || (c.InputUserId ?? 0) == currentUserId)
            select new
            {
                c.Id,
                c.EntryDate,
                c.ContractState,
                c.Amount,
                c.AmtMatched,
                CustomerFullName = u == null
                    ? ""
                    : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                CustomerCode = u == null ? "" : (u.Code ?? "")
            };

        if (!string.IsNullOrWhiteSpace(state) && Enum.TryParse<OnlineContract.Helpers.ContractState>(state, true, out var st))
        {
            q = q.Where(x => x.ContractState == st);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var n = name.Trim().ToLower();
            q = q.Where(x =>
                (x.CustomerFullName ?? "").ToLower().Contains(n) ||
                (x.CustomerCode ?? "").ToLower().Contains(n));
        }

        if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fd))
        {
            q = q.Where(x => x.EntryDate >= fd);
        }
        if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var td))
        {
            var tdEnd = td.Date.AddDays(1).AddTicks(-1);
            q = q.Where(x => x.EntryDate <= tdEnd);
        }

        // Safety cap to avoid exporting an unbounded dataset accidentally.
        var rows = await q
            .OrderByDescending(x => x.EntryDate)
            .ThenBy(x => x.Id)
            .Take(50000)
            .ToListAsync();

        static string CsvEscape(string? s)
        {
            var v = s ?? "";
            var needsQuotes = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
            if (v.Contains('"')) v = v.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{v}\"" : v;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Id,CustomerFullName,Amount,AmtMatched,ContractState,EntryDate");
        foreach (var r in rows)
        {
            sb.Append(CsvEscape(r.Id.ToString()));
            sb.Append(',');
            sb.Append(CsvEscape(r.CustomerFullName));
            sb.Append(',');
            sb.Append(CsvEscape(r.Amount.ToString()));
            sb.Append(',');
            sb.Append(CsvEscape(r.AmtMatched.ToString()));
            sb.Append(',');
            sb.Append(CsvEscape(r.ContractState.ToString()));
            sb.Append(',');
            sb.Append(CsvEscape(r.EntryDate.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine();
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var fname = $"Contract_{ts}.csv";
        return Results.File(bytes, "text/csv; charset=utf-8", fname);
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, OnlineContract.Helpers.EventType.Error, "Contracts export failed", ex.ToString(), 2);
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// -------------------------
// Products API (Admin/Manager/Worker)
// -------------------------

static int GetCurrentUserId(HttpContext http)
{
    try
    {
        var uid = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type.EndsWith("/nameidentifier", StringComparison.OrdinalIgnoreCase))?.Value;
        return int.TryParse(uid, out var id) ? id : 2;
    }
    catch { return 2; }
}

// -------------------------
// Profile API
// -------------------------

app.MapGet("/api/profile", async (AppDbContext db, HttpContext http) =>
{
    if (!http.User?.Identity?.IsAuthenticated ?? true) return Results.StatusCode(StatusCodes.Status401Unauthorized);
    var uid = GetCurrentUserId(http);
    var u = await db.AxUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid && !x.IsDeleted);
    if (u == null) return Results.StatusCode(StatusCodes.Status404NotFound);
    var dto = new OnlineContract.Dtos.ProfileDto
    {
        Id = u.Id,
        Code = u.Code ?? string.Empty,
        FirstName = u.FirstName ?? string.Empty,
        LastName = u.LastName ?? string.Empty,
        Email = u.Email ?? string.Empty,
        PhoneNumber = u.Phone ?? string.Empty,
        City = u.City ?? string.Empty,
        StreetAddress = u.StreetAddress ?? string.Empty,
        PostalCode = u.PostalCode ?? string.Empty,
        Stamp = u.Stamp
    };
    return StableJson(dto);
}).RequireAuthorization();

app.MapPut("/api/profile", async (AppDbContext db, HttpContext http) =>
{
    if (!http.User?.Identity?.IsAuthenticated ?? true) return Results.StatusCode(StatusCodes.Status401Unauthorized);
    var uid = GetCurrentUserId(http);
    var dto = await http.Request.ReadFromJsonAsync<OnlineContract.Dtos.ProfileUpdateDto>();
    if (dto == null) return Results.StatusCode(StatusCodes.Status400BadRequest);

    // Basic required checks
    string err(string m) => m;
    if (string.IsNullOrWhiteSpace(dto.Code)) return StableJson(new { success = false, message = err("Username is required." )});
    if (string.IsNullOrWhiteSpace(dto.FirstName)) return StableJson(new { success = false, message = err("First name is required.") });
    if (string.IsNullOrWhiteSpace(dto.LastName)) return StableJson(new { success = false, message = err("Last name is required.") });
    if (string.IsNullOrWhiteSpace(dto.Email)) return StableJson(new { success = false, message = err("Email is required.") });
    if (string.IsNullOrWhiteSpace(dto.PhoneNumber)) return StableJson(new { success = false, message = err("Phone number is required.") });
    if (string.IsNullOrWhiteSpace(dto.City)) return StableJson(new { success = false, message = err("City is required.") });
    if (string.IsNullOrWhiteSpace(dto.StreetAddress)) return StableJson(new { success = false, message = err("Street address is required.") });
    if (string.IsNullOrWhiteSpace(dto.PostalCode)) return StableJson(new { success = false, message = err("Postal code is required.") });
    // Postal code length must be exactly 5
    var postal = dto.PostalCode.Trim();
    if (postal.Length != 5)
        return StableJson(new { success = false, message = "Postal code must be exactly 5 characters." });

    // Username format (fallback rule if no central validator)
    var code = dto.Code.Trim();
    if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z][A-Za-z0-9._-]{2,29}$"))
        return StableJson(new { success = false, message = "Username must start with a letter and be 3-30 chars (letters, digits, ., _, -)." });

    // Email format
    var email = dto.Email.Trim();
    var emailAttr = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();
    if (!emailAttr.IsValid(email)) return StableJson(new { success = false, message = "Email format is invalid." });

    // Phone normalize via PhoneHelper
    var (okPhone, normalizedPhone, phoneErr) = OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone(dto.PhoneNumber);
    if (!okPhone) return StableJson(new { success = false, message = phoneErr });

    // Uniqueness checks (exclude soft-deleted and current user)
    var existsCode = await db.AxUsers.AnyAsync(x => x.Code == code && !x.IsDeleted && x.Id != uid);
    if (existsCode) return StableJson(new { success = false, message = "Username is already taken." });
    var existsEmail = await db.AxUsers.AnyAsync(x => (x.Email ?? "") == email && !x.IsDeleted && x.Id != uid);
    if (existsEmail) return StableJson(new { success = false, message = "Email is already in use." });

    // Load current for update with concurrency check
    var user = await db.AxUsers.FirstOrDefaultAsync(x => x.Id == uid && !x.IsDeleted);
    if (user == null) return Results.StatusCode(StatusCodes.Status404NotFound);
    if (user.Stamp != dto.Stamp)
        return Results.StatusCode(StatusCodes.Status409Conflict);

    // Password change logic
    var changingPassword = !string.IsNullOrWhiteSpace(dto.CurrentPassword) || !string.IsNullOrWhiteSpace(dto.NewPassword) || !string.IsNullOrWhiteSpace(dto.ConfirmNewPassword);
    bool passwordActuallyChanged = false;
    if (changingPassword)
    {
        var cur = (dto.CurrentPassword ?? "").Trim();
        var np = (dto.NewPassword ?? "").Trim();
        var cp = (dto.ConfirmNewPassword ?? "").Trim();
        if (string.IsNullOrEmpty(cur) || string.IsNullOrEmpty(np) || string.IsNullOrEmpty(cp))
            return StableJson(new { success = false, message = "To change your password, fill all three fields: current, new, and confirm." });
        if (!OnlineContract.Helpers.PasswordHelper.VerifyPassword(cur, user.Password))
            return StableJson(new { success = false, message = "Current password is incorrect." });
        if (np != cp) return StableJson(new { success = false, message = "New password and confirmation do not match." });
        // Password complexity: require length >= 8, at least one uppercase letter, and at least one digit.
        // Special characters are allowed but not required; lowercase is optional.
        if (np.Length < 8 || !np.Any(char.IsUpper) || !np.Any(char.IsDigit))
            return StableJson(new { success = false, message = "New password must be at least 8 chars and include an uppercase letter and a digit." });
        // Must differ from existing
        if (OnlineContract.Helpers.PasswordHelper.VerifyPassword(np, user.Password))
            return StableJson(new { success = false, message = "New password must be different from the current password." });
        // Ok: hash and set; mark password changed
        user.Password = OnlineContract.Helpers.PasswordHelper.HashPassword(np);
        user.PasswordDt = DateTime.Now;
        passwordActuallyChanged = true;
    }

    // Apply other field updates
    user.Code = code;
    user.FirstName = dto.FirstName.Trim();
    user.LastName = dto.LastName.Trim();
    user.Email = email;
    user.Phone = normalizedPhone ?? dto.PhoneNumber.Trim();
    user.City = dto.City.Trim();
    user.StreetAddress = dto.StreetAddress.Trim();
    user.PostalCode = dto.PostalCode.Trim();
    user.Stamp = user.Stamp + 1;

    await db.SaveChangesAsync();
    return StableJson(new { success = true, passwordChanged = passwordActuallyChanged, stamp = user.Stamp });
}).RequireAuthorization();

static bool IsValidEmail(string? email)
{
    if (string.IsNullOrWhiteSpace(email)) return false;
    var e = email.Trim();
    return System.Text.RegularExpressions.Regex.IsMatch(e, @"^\S+@\S+\.\S+$");
}

// Phone normalization now handled by OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone
app.MapGet("/api/products", async (AppDbContext db, HttpContext http, string? q, int? storeId, int page, int pageSize, string? sortBy, string? sortDir) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        IQueryable<Product> products = db.Products
            .AsNoTracking()
            .Where(p => p.Id > 0 && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            products = products.Where(p =>
                EF.Functions.Like(p.Name ?? "", $"%{s}%"));
        }

        // Apply server-side sorting on the products query before constructing the projection
        var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new OnlineContract.Helpers.SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
        var sortMap = new Dictionary<string, System.Linq.Expressions.Expression<Func<Product, object?>>> {
            { "id", p => p.Id },
            { "name", p => p.Name },
            { "inputDt", p => p.InputDt },
            { "isActive", p => p.IsActive }
        };

        IQueryable<Product> orderedProducts;
        if (sortSpec == null)
        {
            orderedProducts = products.OrderBy(p => p.Id);
        }
        else if (string.Equals(sortSpec.By, "qtyStore1", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(sortSpec.By, "qtyStore2", StringComparison.OrdinalIgnoreCase))
        {
            var storeIdSort = string.Equals(sortSpec.By, "qtyStore1", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
            if (sortSpec.Desc)
            {
                orderedProducts = products.OrderByDescending(p => (
                    from v in db.ProductVariants.AsNoTracking()
                    where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                    join i in db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                    where !i.IsDeleted && i.StoreId == storeIdSort && i.IsActive
                    select (int?)i.QtyOnHand).Sum() ?? 0).ThenBy(p => p.Id);
            }
            else
            {
                orderedProducts = products.OrderBy(p => (
                    from v in db.ProductVariants.AsNoTracking()
                    where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                    join i in db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                    where !i.IsDeleted && i.StoreId == storeIdSort && i.IsActive
                    select (int?)i.QtyOnHand).Sum() ?? 0).ThenBy(p => p.Id);
            }
        }
        else
        {
            orderedProducts = products.ApplySort(sortSpec, sortMap, p => p.Id);
        }

        var baseQuery =
            from p in products
            let qty1 =
                (from v in db.ProductVariants.AsNoTracking()
                  where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                 join i in db.ProductInventories.AsNoTracking()
                      on v.Id equals i.ProductVariantId
                  where !i.IsDeleted && i.StoreId == 1 && i.IsActive
                 select (int?)i.QtyOnHand).Sum()
            let qty2 =
                (from v in db.ProductVariants.AsNoTracking()
                  where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                 join i in db.ProductInventories.AsNoTracking()
                      on v.Id equals i.ProductVariantId
                  where !i.IsDeleted && i.StoreId == 2 && i.IsActive
                 select (int?)i.QtyOnHand).Sum()
            select new
            {
                p.Id,
                p.Name,
                p.InputDt,
                p.IsActive,
                QtyStore1 = qty1 ?? 0,
                QtyStore2 = qty2 ?? 0,
                Stamp = p.Stamp
            };

        // Store availability filter ("All Stores" => storeId is null/0)
        if (storeId.HasValue && storeId.Value > 0)
        {
            if (storeId.Value == 1) baseQuery = baseQuery.Where(x => x.QtyStore1 > 0);
            else if (storeId.Value == 2) baseQuery = baseQuery.Where(x => x.QtyStore2 > 0);
            else
            {
                // Generic store filter for any store_id
                var sid = storeId.Value;
                baseQuery =
                    from r in baseQuery
                    let qtySelected =
                        (from v in db.ProductVariants.AsNoTracking()
                        where !v.IsDeleted && v.ProductId == r.Id && v.IsActive
                         join i in db.ProductInventories.AsNoTracking()
                              on v.Id equals i.ProductVariantId
                         where !i.IsDeleted && i.StoreId == sid && i.IsActive
                         select (int?)i.QtyOnHand).Sum()
                    where (qtySelected ?? 0) > 0
                    select r;
            }
        }

        var totalCount = await baseQuery.CountAsync();

        var proj =
            from p in orderedProducts
            let qty1 =
                (from v in db.ProductVariants.AsNoTracking()
                  where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                 join i in db.ProductInventories.AsNoTracking()
                      on v.Id equals i.ProductVariantId
                  where !i.IsDeleted && i.StoreId == 1 && i.IsActive
                 select (int?)i.QtyOnHand).Sum()
            let qty2 =
                (from v in db.ProductVariants.AsNoTracking()
                  where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                 join i in db.ProductInventories.AsNoTracking()
                      on v.Id equals i.ProductVariantId
                  where !i.IsDeleted && i.StoreId == 2 && i.IsActive
                 select (int?)i.QtyOnHand).Sum()
            select new
            {
                p.Id,
                p.Name,
                p.InputDt,
                p.IsActive,
                QtyStore1 = qty1 ?? 0,
                QtyStore2 = qty2 ?? 0,
                Stamp = p.Stamp
            };

        if (storeId.HasValue && storeId.Value > 0)
        {
            if (storeId.Value == 1) proj = proj.Where(x => x.QtyStore1 > 0);
            else if (storeId.Value == 2) proj = proj.Where(x => x.QtyStore2 > 0);
            else
            {
                var sid = storeId.Value;
                proj = proj.Where(r => (
                    (from v in db.ProductVariants.AsNoTracking()
                      where !v.IsDeleted && v.ProductId == r.Id && v.IsActive
                     join i in db.ProductInventories.AsNoTracking()
                          on v.Id equals i.ProductVariantId
                      where !i.IsDeleted && i.StoreId == sid && i.IsActive
                     select (int?)i.QtyOnHand).Sum() ?? 0) > 0);
            }
        }

        var rows = await proj
            .Skip(Math.Max(0, (pageIndex - 1) * size))
            .Take(size)
            .ToListAsync();

        var items = rows.Select(r => new
        {
            id = r.Id,
            name = r.Name ?? "",
            inputDt = r.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
            qtyStore1 = r.QtyStore1,
            qtyStore2 = r.QtyStore2,
            isActive = r.IsActive,
            stamp = r.Stamp
        });

        return StableJson(new
        {
            items,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)size)
        });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Products fetch failed", ex.ToString(), 2);
        return StableJson(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

app.MapGet("/api/products/{id:int}", async (AppDbContext db, HttpContext http, int id, string? sortBy, string? sortDir) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (id <= 0) return Results.NotFound(new { message = "Product not found. Please verify the product ID and try again." });

    var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    if (p == null) return Results.NotFound(new { message = "Product not found. The product may have been removed." });

    var inputUserCode = "";
    if ((p.InputUserId ?? 0) > 0)
    {
        inputUserCode = await db.AxUsers.AsNoTracking()
            .Where(u => u.Id == p.InputUserId)
            .Select(u => u.Code)
            .FirstOrDefaultAsync() ?? "";
    }

    var lastModifiedByCode = "";
    if ((p.LastModifiedById ?? 0) > 0)
    {
        lastModifiedByCode = await db.AxUsers.AsNoTracking()
            .Where(u => u.Id == p.LastModifiedById)
            .Select(u => u.Code)
            .FirstOrDefaultAsync() ?? "";
    }

    // Apply server-side sorting for variants when requested (Sort -> Filter -> Paginate)
    var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new SortSpec(sortBy, (sortDir ?? "").ToLowerInvariant() == "desc");

    var variantsQuery = db.ProductVariants.AsNoTracking().Where(v => v.ProductId == id && !v.IsDeleted);

    var variantMap = new Dictionary<string, System.Linq.Expressions.Expression<Func<ProductVariant, object?>>>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = v => v.Id,
        ["size"] = v => v.Size ?? "",
        ["color"] = v => v.Color ?? "",
        ["amount"] = v => v.Amount,
        ["isActive"] = v => v.IsActive,
        ["stamp"] = v => v.Stamp
    };

    if (sortSpec == null)
    {
        variantsQuery = variantsQuery.OrderBy(v => v.Id);
    }
    else
    {
        // Support sorting by aggregated inventory quantities per store (qtyStore1, qtyStore2)
        if (string.Equals(sortSpec.By, "qtyStore1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sortSpec.By, "qtyStore2", StringComparison.OrdinalIgnoreCase))
        {
            var storeId = string.Equals(sortSpec.By, "qtyStore1", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
            if (sortSpec.Desc)
            {
                var ordered = variantsQuery.OrderByDescending(v => db.ProductInventories
                    .Where(i => i.ProductVariantId == v.Id && !i.IsDeleted && i.StoreId == storeId)
                    .Select(i => (int?)i.QtyOnHand).Sum() ?? 0);
                variantsQuery = System.Linq.Queryable.ThenBy((IOrderedQueryable<ProductVariant>)ordered, v => v.Id);
            }
            else
            {
                var ordered = variantsQuery.OrderBy(v => db.ProductInventories
                    .Where(i => i.ProductVariantId == v.Id && !i.IsDeleted && i.StoreId == storeId)
                    .Select(i => (int?)i.QtyOnHand).Sum() ?? 0);
                variantsQuery = System.Linq.Queryable.ThenBy((IOrderedQueryable<ProductVariant>)ordered, v => v.Id);
            }
        }
        else
        {
            variantsQuery = variantsQuery.ApplySort(sortSpec, variantMap, v => v.Id);
        }
    }

    var variants = await variantsQuery.ToListAsync();

    var variantIds = variants.Select(v => v.Id).ToList();
    var inv = await db.ProductInventories.AsNoTracking()
        .Where(i => variantIds.Contains(i.ProductVariantId) && !i.IsDeleted)
        .ToListAsync();

    // Build maps for qty and stamp per variant+store
    var invQtyMap = inv
        .GroupBy(i => new { i.ProductVariantId, i.StoreId })
        .ToDictionary(g => (g.Key.ProductVariantId, g.Key.StoreId), g => g.Sum(x => x.QtyOnHand));
    var invStampMap = inv
        .GroupBy(i => new { i.ProductVariantId, i.StoreId })
        .ToDictionary(g => (g.Key.ProductVariantId, g.Key.StoreId), g => g.OrderByDescending(x => x.LastUpdatedDt ?? x.InputDt).FirstOrDefault()?.Stamp ?? 0);

    var vDtos = variants.Select(v => new
    {
        id = v.Id,
        size = v.Size ?? "",
        color = v.Color ?? "",
        amount = v.Amount,
        isActive = v.IsActive,
        sizeKey = v.SizeKey,
        colorKey = v.ColorKey,
        photoFileName = v.PhotoFileName,
        qtyStore1 = invQtyMap.TryGetValue((v.Id, 1), out var q1) ? q1 : 0,
        qtyStore1Stamp = invStampMap.TryGetValue((v.Id, 1), out var s1) ? s1 : 0,
        qtyStore2 = invQtyMap.TryGetValue((v.Id, 2), out var q2) ? q2 : 0,
        qtyStore2Stamp = invStampMap.TryGetValue((v.Id, 2), out var s2) ? s2 : 0,
        stamp = v.Stamp
    });

    return Results.Json(new
    {
        id = p.Id,
        name = p.Name ?? "",
        inputDt = p.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
        inputUserId = p.InputUserId,
        inputUserCode,
        lastModifiedById = p.LastModifiedById,
        lastModifiedByCode,
        lastUpdatedDt = p.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss"),
        isActive = p.IsActive,
        isDeleted = p.IsDeleted,
        stamp = p.Stamp,
        variants = vDtos
    });
}).RequireAuthorization();

app.MapPost("/api/products", async (AppDbContext db, HttpContext http, ProductCreateDto dto) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Results.Json(new { success = false, message = "Product name is required. Please enter a name and try again." });

        var uid = GetCurrentUserId(http);
        var p = new Product
        {
            Name = name,
            IsActive = dto.IsActive,
            IsDeleted = false,
            InputDt = DateTime.Now,
            InputUserId = uid,
            LastModifiedById = uid,
            LastUpdatedDt = DateTime.Now,
            Stamp = 0
        };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product created", $"ProductId={p.Id}", uid);
        return Results.Json(new { success = true, id = p.Id, message = "Product has been created successfully." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Create product failed", ex.ToString(), 2);
        return Results.Json(new { success = false, message = "Failed to create the product. Please try again later." });
    }
}).RequireAuthorization();

app.MapPut("/api/products/{id:int}", async (AppDbContext db, HttpContext http, int id, ProductUpdateDto dto) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Results.NotFound(new { message = "Product not found. It may have been deleted by another user." });

        if (!dto.Stamp.HasValue || dto.Stamp.Value != p.Stamp)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Product update conflict - stamp mismatch", $"ProductId={id}", GetCurrentUserId(http));
            return Results.Json(new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
        }

        if (dto.Name != null) p.Name = dto.Name.Trim();
        if (dto.IsActive.HasValue) p.IsActive = dto.IsActive.Value;

        var uid = GetCurrentUserId(http);
        p.LastModifiedById = uid;
        p.LastUpdatedDt = DateTime.Now;

        p.Stamp = p.Stamp + 1;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product updated", $"ProductId={p.Id}", uid);
        return Results.Json(new { success = true, message = "Product has been updated successfully." });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Update product failed", ex.ToString(), 2);
        return Results.Json(new { success = false, message = "Failed to update the product. Please try again later." });
    }
}).RequireAuthorization();

app.MapPut("/api/products/{id:int}/details",
async (AppDbContext db, HttpContext http, int id, HttpRequest request) =>
{
    if (!CanManageProducts(http))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var jsonOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    await using var tx = await db.Database.BeginTransactionAsync();

    static string? NormalizePhotoFileName(string? value)
    {
        var s = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;

        // Only store a filename (no path)
        s = Path.GetFileName(s);
        if (string.IsNullOrWhiteSpace(s)) return null;

        // Remove invalid chars
        foreach (var ch in Path.GetInvalidFileNameChars())
            s = s.Replace(ch, '_');

        var ext = (Path.GetExtension(s) ?? "").ToLowerInvariant();
        var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp"
        };
        if (!allowedExt.Contains(ext))
            throw new InvalidOperationException("Invalid image format. Allowed formats: PNG, JPG/JPEG, GIF, WEBP.");

        return s;
    }
    try
    {
        var dto = await request.ReadFromJsonAsync<ProductDetailsUpdateDto>(jsonOptions);
        if (dto is null)
            return Results.BadRequest(new { success = false, message = "Invalid JSON payload. Please check your request format and try again." });

        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null)
            return Results.NotFound(new { message = "Product not found. The product may have been removed." });

        int uid = GetCurrentUserId(http);
        // Diagnostic: log how many variants/deleted ids were received
        try { await LoggerHelper.LogEventAsync(db, EventType.Information, "ProductDetails received", $"ProductId={id}; Variants={(dto.Variants?.Count ?? 0)}; DeletedIds={(dto.DeletedVariantIds?.Count ?? 0)}; StampPresent={dto.Stamp.HasValue}", uid); } catch { }
        // Product-level optimistic concurrency: require client to supply current stamp
        // Allow missing stamp when server-side product stamp is zero (newly created product path)
        if (!dto.Stamp.HasValue)
        {
            if (p.Stamp != 0)
            {
                await tx.RollbackAsync();
                await LoggerHelper.LogEventAsync(db, EventType.Warning, "Product details save conflict - missing stamp", $"ProductId={id}", uid);
                return Results.Json(new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
            }
        }
        else if (dto.Stamp.Value != p.Stamp)
        {
            await tx.RollbackAsync();
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Product details save conflict - stamp mismatch", $"ProductId={id}", uid);
            return Results.Json(new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
        }

        // If the product is currently inactive/deleted, disallow changes to variants/inventories/notes.
        // The only allowed change while inactive is activating the product itself (dto.IsActive = true).
        if (!p.IsActive && !(dto.IsActive.HasValue && dto.IsActive.Value))
        {
            await tx.RollbackAsync();
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Product details save blocked - product inactive", $"ProductId={id}", uid);
            return Results.BadRequest(new { success = false, message = "This product is deactivated or deleted. Activate the product before modifying its variants, inventories or notes." });
        }

        // Basic product fields
        if (dto.Name is not null)        p.Name = dto.Name.Trim();
        if (dto.IsActive.HasValue)       p.IsActive = dto.IsActive.Value;
        p.LastModifiedById = uid;
        p.LastUpdatedDt    = DateTime.Now;

        // Delete variants by id (>0)
        var deletedIds = (dto.DeletedVariantIds ?? new List<int>()).Where(x => x > 0).ToList();
        if (deletedIds.Count > 0)
        {
            var toDelete = await db.ProductVariants
                .Where(v => v.ProductId == id && deletedIds.Contains(v.Id))
                .ToListAsync();

            foreach (var v in toDelete)
            {
                v.IsDeleted        = true;
                v.IsActive         = false;
                v.LastModifiedById = uid;
                v.LastUpdatedDt    = DateTime.Now;

                var invs = await db.ProductInventories
                    .Where(i => i.ProductVariantId == v.Id && !i.IsDeleted)
                    .ToListAsync();

                foreach (var inv in invs)
                {
                    inv.IsDeleted        = true;
                    inv.IsActive         = false;
                    inv.LastModifiedById = uid;
                    inv.LastUpdatedDt    = DateTime.Now;
                }
            }
        }

        foreach (var vd in dto.Variants ?? Enumerable.Empty<ProductVariantDto>())
        {
            if (vd.IsDeleted == true) continue;

            int     vId      = vd.Id.GetValueOrDefault(0);
            string  size     = vd.Size ?? "";
            string  color    = vd.Color ?? "";
            decimal amount    = vd.Amount;
            string? photo    = NormalizePhotoFileName(vd.PhotoFileName);
            bool    isActive = vd.IsActive;
            int     qty1     = Math.Max(0, vd.QtyStore1);
            int     qty2     = Math.Max(0, vd.QtyStore2);

            ProductVariant? v;

            if (vId <= 0)
            {
                v = new ProductVariant
                {
                    ProductId        = id,
                    Size             = size,
                    Color            = color,
                    Amount            = amount,
                    PhotoFileName    = photo,
                    IsActive         = isActive,
                    IsDeleted        = false,
                    InputDt          = DateTime.Now,
                    InputUserId      = uid,
                    LastModifiedById = uid,
                    LastUpdatedDt    = DateTime.Now,
                    Stamp            = 0
                };
                db.ProductVariants.Add(v);
                await db.SaveChangesAsync();
                // Log created variant for diagnostics
                try { await LoggerHelper.LogEventAsync(db, EventType.Information, "Variant created", $"ProductId={id}; VariantId={v.Id}", uid); } catch { }
            }
            else
            {
                v = await db.ProductVariants
                    .FirstOrDefaultAsync(x => x.Id == vId && x.ProductId == id);
                if (v == null) continue;
                // Variant-level optimistic concurrency
                if (vd.Stamp.HasValue && vd.Stamp.Value != v.Stamp)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Variant save conflict - stamp mismatch", $"ProductId={id}; VariantId={vId}", uid);
                    return Results.Json(new { success = false, message = $"Variant {vId} was changed by another user. Reload and try again." });
                }

                v.IsDeleted        = false;
                v.Size             = size;
                v.Color            = color;
                v.Amount            = amount;
                v.PhotoFileName    = photo;
                v.IsActive         = isActive;
                v.LastModifiedById = uid;
                v.LastUpdatedDt    = DateTime.Now;
                // bump variant stamp
                v.Stamp = v.Stamp + 1;
            }

            static int Clamp(int n) => n < 0 ? 0 : n;

            async Task UpsertInvAsync(int storeId, int qty, int? expectedStamp)
            {
                var inv = await db.ProductInventories
                    .FirstOrDefaultAsync(x => x.ProductVariantId == v!.Id && x.StoreId == storeId);
                if (inv == null)
                {
                    inv = new ProductInventory
                    {
                        ProductVariantId = v!.Id,
                        StoreId          = storeId,
                        QtyOnHand        = Clamp(qty),
                        IsActive         = true,
                        IsDeleted        = false,
                        InputDt          = DateTime.Now,
                        InputUserId      = uid,
                        LastModifiedById = uid,
                        LastUpdatedDt    = DateTime.Now,
                        Stamp            = 0
                    };
                    db.ProductInventories.Add(inv);
                }
                else
                {
                    // Inventory-level optimistic concurrency
                    if (expectedStamp.HasValue && expectedStamp.Value != inv.Stamp)
                    {
                        await LoggerHelper.LogEventAsync(db, EventType.Warning, "Inventory save conflict - stamp mismatch", $"ProductId={id}; VariantId={v!.Id}; StoreId={storeId}", uid);
                        throw new InvalidOperationException("Inventory stamp mismatch");
                    }

                    inv.IsDeleted        = false;
                    inv.QtyOnHand        = Clamp(qty);
                    inv.IsActive         = true;
                    inv.LastModifiedById = uid;
                    inv.LastUpdatedDt    = DateTime.Now;
                    inv.Stamp = inv.Stamp + 1;
                }
            }

            await UpsertInvAsync(1, qty1, vd.QtyStore1Stamp);
            await UpsertInvAsync(2, qty2, vd.QtyStore2Stamp);
            // Log inventory state after upsert attempt
            try { await LoggerHelper.LogEventAsync(db, EventType.Information, "Variant inventory upserted", $"ProductId={id}; VariantTmpId={vId}; VariantRealId={v?.Id}; Qty1={qty1}; Qty2={qty2}", uid); } catch { }
        }

        // Ensure any newly added inventories are persisted before bumping product stamp
        await db.SaveChangesAsync();

        // If payload included notes, process them here inside same transaction so product+notes save atomically
        if (dto.Notes != null)
        {
            var notesDto = dto.Notes;
            // Reuse similar logic as /api/products/{id}/notes endpoint but operate within this transaction
            // Add
            foreach (var add in notesDto.Add ?? new List<OnlineContract.Dtos.NoteCreateDto>())
            {
                var text = (add.Comment ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp) VALUES ({id}, NULL, {text}, '', 0, 0, {(add.IsActive ? 1 : 0)}, dbo.GetLocalTime(), {GetCurrentUserId(http)}, {GetCurrentUserId(http)}, dbo.GetLocalTime(), 0);");
            }

            // Update
            var updatedIds = (notesDto.Update ?? new List<OnlineContract.Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
            foreach (var upd in notesDto.Update ?? new List<OnlineContract.Dtos.NoteUpdateDto>())
            {
                var existing = await db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == upd.Id && x.ProductId == id && !x.IsDeleted);
                if (existing == null) continue;
                var newComment = (upd.Comment != null) ? upd.Comment.Trim() : existing.Comment;
                var newIsActive = upd.IsActive.HasValue ? upd.IsActive.Value : existing.IsActive;
                var newIsDeleted = upd.IsDeleted.HasValue ? upd.IsDeleted.Value : existing.IsDeleted;
                var newIsMain = (notesDto.SetMainId.HasValue && notesDto.SetMainId.Value == upd.Id) ? 1 : (existing.IsMain ? 1 : 0);
                if (!upd.Stamp.HasValue)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - missing stamp for update (details)", $"ProductId={id}; NoteId={upd.Id}", GetCurrentUserId(http));
                    return Results.Json(new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }

                var sql = "UPDATE dbo.note SET comment = @pComment, is_deleted = @pDeleted, is_active = @pActive, is_main = @pMain, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE note_id = @pNid AND product_id = @pPid AND stamp = @pStamp;";
                var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)(newComment ?? string.Empty) };
                var pDeleted = new Microsoft.Data.SqlClient.SqlParameter("@pDeleted", System.Data.SqlDbType.Int) { Value = (newIsDeleted ? 1 : 0) };
                var pActive = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = (newIsActive ? 1 : 0) };
                var pMain = new Microsoft.Data.SqlClient.SqlParameter("@pMain", System.Data.SqlDbType.Int) { Value = newIsMain };
                var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = GetCurrentUserId(http) };
                var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = upd.Id };
                var pPid = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                var pStamp = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = upd.Stamp.Value };
                var affected = await db.Database.ExecuteSqlRawAsync(sql, pComment, pDeleted, pActive, pMain, pUid, pNid, pPid, pStamp);
                if (affected == 0)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - update (details)", $"ProductId={id}; NoteId={upd.Id}", GetCurrentUserId(http));
                    return Results.Json(new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
            }

            // Delete
            var delItems = (notesDto.Delete ?? new List<OnlineContract.Dtos.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
            if (delItems.Count > 0)
            {
                var sql = "UPDATE dbo.note SET is_deleted = 1, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE product_id = @pPid AND note_id = @pNid AND stamp = @pStamp;";
                foreach (var did in delItems)
                {
                    if (!did.Stamp.HasValue)
                    {
                        await tx.RollbackAsync();
                        await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - missing stamp for delete (details)", $"ProductId={id}; NoteId={did.Id}", GetCurrentUserId(http));
                        return Results.Json(new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                    }
                    var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = GetCurrentUserId(http) };
                    var pPid = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                    var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = did.Id };
                    var pStamp = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = did.Stamp.Value };
                    var affected = await db.Database.ExecuteSqlRawAsync(sql, pUid, pPid, pNid, pStamp);
                    if (affected == 0)
                    {
                        await tx.RollbackAsync();
                        await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - delete (details)", $"ProductId={id}; NoteId={did.Id}", GetCurrentUserId(http));
                        return Results.Json(new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                    }
                }
            }

            if (notesDto.SetMainId.HasValue && notesDto.SetMainId.Value > 0)
            {
                var targetId = notesDto.SetMainId.Value;
                updatedIds = (notesDto.Update ?? new List<OnlineContract.Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
                var unsetSql = "UPDATE dbo.note SET is_main = 0, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE product_id = @pPid AND is_deleted = 0 AND is_main = 1 AND note_id != @pTarget";
                if (updatedIds != null && updatedIds.Count > 0)
                {
                    unsetSql += " AND note_id NOT IN (" + string.Join(',', updatedIds) + ")";
                }
                var pUidU = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = GetCurrentUserId(http) };
                var pPidU = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                var pTargetU = new Microsoft.Data.SqlClient.SqlParameter("@pTarget", System.Data.SqlDbType.Int) { Value = targetId };
                await db.Database.ExecuteSqlRawAsync(unsetSql, pUidU, pPidU, pTargetU);

                if (!(updatedIds?.Contains(targetId) ?? false))
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.note SET is_main = 1, last_modified_by_id = {GetCurrentUserId(http)}, last_updated_dt = dbo.GetLocalTime() WHERE note_id = {targetId} AND product_id = {id} AND is_deleted = 0;");
                }
            }
        }

        // bump product stamp and save
        p.Stamp = p.Stamp + 1;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await LoggerHelper.LogEventAsync(db, EventType.Information,
            "Product details saved", $"ProductId={p.Id}", uid);

        return Results.Json(new { success = true, message = "Product details were successfully saved." });
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();

        // If SaveChanges failed, tracked entities can prevent logging from saving.
        // Clearing the tracker ensures the event_log insert is independent.
        try { db.ChangeTracker.Clear(); } catch { }

        await LoggerHelper.LogEventAsync(
            db, EventType.Error, "Save product details failed", ex.ToString(), GetCurrentUserId(http));

        var msg = ex is InvalidOperationException
            ? ex.Message
            : "Product details could not be saved. Please try again.";
        return Results.Json(new { success = false, message = msg });
    }
})
.RequireAuthorization();

app.MapPost("/api/products/upload-photo", async (AppDbContext db, HttpContext http) =>
{
    if (!CanManageProducts(http))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        if (!http.Request.HasFormContentType)
            return Results.BadRequest(new { success = false, message = "Expected multipart/form-data. Please submit the form with file upload." });

        var form = await http.Request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        if (file == null || file.Length <= 0)
            return Results.BadRequest(new { success = false, message = "No image was uploaded. Please choose an image file and try again." });

        var uploadDir = @"C:\Projects\Build\InstallDocs";
        Directory.CreateDirectory(uploadDir);

        var originalName = Path.GetFileName(file.FileName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(originalName))
            originalName = "upload.bin";

        var ext = (Path.GetExtension(originalName) ?? "").ToLowerInvariant();
        var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
        if (!allowedExt.Contains(ext))
            return Results.BadRequest(new { success = false, message = "Invalid image format. Allowed formats are PNG, JPG/JPEG, GIF, and WEBP." });

        var ct = (file.ContentType ?? "").ToLowerInvariant();
        if (!ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { success = false, message = "Invalid file type. Please upload a valid image file." });

        // Very small sanitization: remove invalid chars
        foreach (var ch in Path.GetInvalidFileNameChars())
            originalName = originalName.Replace(ch, '_');

        var targetPath = Path.Combine(uploadDir, originalName);
        if (System.IO.File.Exists(targetPath))
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(originalName);
            var existingExt = Path.GetExtension(originalName);
            var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            originalName = $"{nameNoExt}_{stamp}{existingExt}";
            targetPath = Path.Combine(uploadDir, originalName);
        }

        await using (var fs = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(fs);
        }

        var uid = GetCurrentUserId(http);
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product photo uploaded", $"File={originalName}", uid);

        return Results.Json(new
        {
            success = true,
            fileName = originalName,
            uploadPath = uploadDir
        });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Upload product photo failed", ex.ToString(), GetCurrentUserId(http));
        return Results.Json(new { success = false, message = "Image upload failed. Please try again later." });
    }
}).RequireAuthorization();

app.MapPost("/api/products/{id:int}/deactivate", async (AppDbContext db, HttpContext http, int id, int? stamp) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Results.NotFound(new { message = "Product not found. The product may have been removed." });
        // optimistic concurrency: require client to supply current stamp
        if (!stamp.HasValue || stamp.Value != p.Stamp)
        {
            await tx.RollbackAsync();
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Product deactivate conflict - stamp mismatch", $"ProductId={id}", GetCurrentUserId(http));
            return Results.Json(new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
        }
        var uid = GetCurrentUserId(http);

        // Use atomic SQL updates to ensure stamp is incremented exactly by 1 at the DB level
        var now = DateTime.Now;
        var updatedProduct = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product SET is_active = 0, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
        var updatedVariants = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_variant SET is_active = 0, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
        var updatedInventories = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_inventory SET is_active = 0, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_variant_id IN (SELECT product_variant_id FROM dbo.product_variant WHERE product_id = {id} AND is_deleted = 0) AND is_deleted = 0;");
        var updatedNotes = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.note SET is_active = 0, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");

        await tx.CommitAsync();

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product deactivated", $"ProductId={p.Id}; variantsUpdated={updatedVariants}; inventoriesUpdated={updatedInventories}; notesUpdated={updatedNotes}", uid);
        return Results.Ok(new { success = true, message = "Product has been deactivated successfully.", variantsUpdated = updatedVariants, inventoriesUpdated = updatedInventories, notesUpdated = updatedNotes });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Deactivate product failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

app.MapPost("/api/products/{id:int}/activate", async (AppDbContext db, HttpContext http, int id, int? stamp) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Results.NotFound(new { message = "Product not found. The product may have been removed." });
        var uid = GetCurrentUserId(http);
        // optimistic concurrency: require client to supply current stamp
        if (!stamp.HasValue || stamp.Value != p.Stamp)
        {
            await tx.RollbackAsync();
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Product activate conflict - stamp mismatch", $"ProductId={id}", uid);
            return Results.Json(new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
        }

        var now = DateTime.Now;
        // Use atomic SQL updates to ensure stamp increments by exactly 1
        var updatedProduct = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product SET is_active = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
        var updatedVariants = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_variant SET is_active = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
        var updatedInventories = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_inventory SET is_active = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_variant_id IN (SELECT product_variant_id FROM dbo.product_variant WHERE product_id = {id} AND is_deleted = 0) AND is_deleted = 0;");
        var updatedNotes = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.note SET is_active = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");

        await tx.CommitAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product activated", $"ProductId={p.Id}; variantsUpdated={updatedVariants}; inventoriesUpdated={updatedInventories}; notesUpdated={updatedNotes}", uid);
        return Results.Ok(new { success = true, message = "Product has been activated successfully.", variantsUpdated = updatedVariants, inventoriesUpdated = updatedInventories, notesUpdated = updatedNotes });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Activate product failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

app.MapPost("/api/products/{id:int}/delete", async (AppDbContext db, HttpContext http, int id) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

    await using var tx = await db.Database.BeginTransactionAsync();
    try
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Results.NotFound(new { message = "Product not found. The product may have been removed." });
        var uid = GetCurrentUserId(http);
        var now = DateTime.Now;

        // Perform atomic SQL updates to mark product, variants, inventories and notes deleted/inactive and increment stamps by 1
        var updatedProduct = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product SET is_active = 0, is_deleted = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
        var updatedVariants = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_variant SET is_active = 0, is_deleted = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
        var updatedInventories = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_inventory SET is_active = 0, is_deleted = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_variant_id IN (SELECT product_variant_id FROM dbo.product_variant WHERE product_id = {id} AND is_deleted = 0) AND is_deleted = 0;");
        var updatedNotes = await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.note SET is_active = 0, is_deleted = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");

        await tx.CommitAsync();

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product deleted", $"ProductId={p.Id}; variantsUpdated={updatedVariants}; inventoriesUpdated={updatedInventories}; notesUpdated={updatedNotes}", uid);
        return Results.Ok(new { success = true, message = "Product has been deleted successfully.", variantsUpdated = updatedVariants, inventoriesUpdated = updatedInventories, notesUpdated = updatedNotes });
    }
    catch (Exception ex)
    {
        try { await tx.RollbackAsync(); } catch { }
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Delete product failed", ex.ToString(), GetCurrentUserId(http));
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Product Notes endpoints

// Notes page route
app.MapGet("/notes", (HttpContext context) =>
{
    var filePath = Path.Combine(app.Environment.WebRootPath, "notes.html");
    return Results.File(filePath, "text/html");
}).RequireAuthorization();

// API: list notes (paged, filters)
app.MapGet("/api/notes", async (AppDbContext db, int? contractId, int? productId, int page, int pageSize, string? sortBy, string? sortDir) =>
{
    try
    {
        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        var q = db.Notes.AsNoTracking().Where(n => !n.IsDeleted);
        if (contractId.HasValue && contractId.Value > 0) q = q.Where(n => n.ContractId == contractId.Value);
        if (productId.HasValue && productId.Value > 0) q = q.Where(n => n.ProductId == productId.Value);
        // Apply server-side sort (if requested) before paging
        var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new OnlineContract.Helpers.SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
        var sortMap = new Dictionary<string, System.Linq.Expressions.Expression<Func<OnlineContract.Models.Note, object?>>> {
            { "id", n => n.Id },
            { "contractId", n => n.ContractId },
            { "productId", n => n.ProductId },
            { "subject", n => n.Subject },
            { "inputDt", n => n.InputDt },
            { "inputUserId", n => n.InputUserId },
            { "status", n => n.IsActive }
        };

        var totalCount = await q.CountAsync();

        var ordered = sortSpec == null
            ? q.OrderByDescending(n => n.InputDt).ThenBy(n => n.Id)
            : q.ApplySort(sortSpec, sortMap, n => n.Id);

        var rows = await ordered
                          .Skip(Math.Max(0, (pageIndex - 1) * size))
                          .Take(size)
                          .Select(n => new {
                              id = n.Id,
                              contractId = n.ContractId,
                              productId = n.ProductId,
                              isActive = n.IsActive,
                              subject = n.Subject ?? "",
                              inputDt = n.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                              inputUserId = n.InputUserId,
                              inputUserCode = (from u in db.AxUsers.AsNoTracking() where u.Id == n.InputUserId select u.Code).FirstOrDefault(),
                              status = n.IsActive ? "Active" : "Inactive"
                          })
                          .ToListAsync();

        return Results.Json(new { items = rows, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size), sortBy = sortBy ?? "", sortDir = sortDir ?? "" });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Notes fetch failed", ex.ToString(), 2);
        return Results.Json(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

// API: get single note by id
app.MapGet("/api/notes/{id:int}", async (AppDbContext db, int id) =>
{
    if (id <= 0) return Results.NotFound(new { message = "Note not found. Please verify the note ID and try again." });
    try
    {
        var n = await db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (n == null) return Results.NotFound(new { message = "Note not found. The note may have been removed." });

        // resolve related user codes for display
        var inputUserCode = await db.AxUsers.Where(u => u.Id == n.InputUserId).Select(u => u.Code).FirstOrDefaultAsync();
        var lastModifiedByCode = await db.AxUsers.Where(u => u.Id == n.LastModifiedById).Select(u => u.Code).FirstOrDefaultAsync();

        return Results.Json(new
        {
            id = n.Id,
            subject = n.Subject ?? "",
            comment = n.Comment ?? "",
            contractId = n.ContractId,
            productId = n.ProductId,
            isActive = n.IsActive,
            isMain = n.IsMain,
            inputDt = n.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
            inputUserId = n.InputUserId,
            inputUserCode = inputUserCode ?? "",
            lastModifiedById = n.LastModifiedById,
            lastModifiedByCode = lastModifiedByCode ?? "",
            lastUpdatedDt = n.LastUpdatedDt.HasValue ? n.LastUpdatedDt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
            stamp = n.Stamp,
            isDeleted = n.IsDeleted
        });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Note fetch failed", ex.ToString(), 2);
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// API: add note (simple)
app.MapPost("/api/notes", async (AppDbContext db, NoteCreateSimpleDto dto, HttpContext http) =>
{
    try
    {
        var uid = GetCurrentUserId(http);
        // Validate required fields
        var subj = (dto.Subject ?? "").Trim();
        var comm = (dto.Comment ?? "").Trim();
        if (string.IsNullOrWhiteSpace(subj) || string.IsNullOrWhiteSpace(comm))
        {
            return Results.BadRequest(new { success = false, message = "Subject and Comment are required. Please provide both before saving the note." });
        }
        // require at least one of contractId or productId
        var hasContract = dto.ContractId.HasValue && dto.ContractId.Value > 0;
        var hasProduct = dto.ProductId.HasValue && dto.ProductId.Value > 0;
        if (!hasContract && !hasProduct)
        {
            return Results.BadRequest(new { success = false, message = "Please provide either a Contract Id or a Product Id. Enter a numeric id from the Contracts or Products list." });
        }
        // disallow specifying both at once
        if (hasContract && hasProduct)
        {
            return Results.BadRequest(new { success = false, message = "Please provide only one target: either a Contract Id or a Product Id, not both." });
        }
        // validate referenced contract/product existence and active status
        if (hasContract)
        {
            var contractIdCheck = dto.ContractId!.Value;
            var existsC = await db.Contracts.AsNoTracking().AnyAsync(c => c.Id == contractIdCheck && c.IsActive && !c.IsDeleted);
            if (!existsC) return Results.BadRequest(new { success = false, message = $"Please provide a valid Contract Id. Contract {contractIdCheck} was not found or is no longer active." });
        }
        if (hasProduct)
        {
            var productIdCheck = dto.ProductId!.Value;
            var existsP = await db.Products.AsNoTracking().AnyAsync(p => p.Id == productIdCheck && p.IsActive && !p.IsDeleted);
            if (!existsP) return Results.BadRequest(new { success = false, message = $"Please provide a valid Product Id. Product {productIdCheck} was not found or is no longer active." });
        }

        var note = new OnlineContract.Models.Note
        {
            ContractId = (dto.ContractId.HasValue && dto.ContractId.Value > 0) ? dto.ContractId : null,
            ProductId = (dto.ProductId.HasValue && dto.ProductId.Value > 0) ? dto.ProductId : null,
            Comment = comm,
            Subject = subj,
            IsActive = dto.IsActive ?? true,
            IsDeleted = false,
            InputDt = DateTime.Now,
            InputUserId = uid,
            LastModifiedById = uid,
            LastUpdatedDt = DateTime.Now,
            Stamp = 0
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        // Provide created note metadata so clients can merge into local draft without reloading full product
        var inputUserCode = await db.AxUsers.Where(u => u.Id == uid).Select(u => u.Code).FirstOrDefaultAsync();
        return Results.Json(new { success = true, id = note.Id, stamp = note.Stamp, inputDt = note.InputDt.ToString("yyyy-MM-dd HH:mm:ss"), inputUserCode = inputUserCode ?? "" });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Create note failed", ex.ToString(), 2);
        return Results.Json(new { success = false });
    }
}).RequireAuthorization();

// API: update single note (simple edit)
app.MapPut("/api/notes/{id:int}", async (AppDbContext db, int id, NoteCreateSimpleDto dto, HttpContext http) =>
{
    if (id <= 0) return Results.NotFound(new { message = "Note not found. Please verify the note ID and try again." });
    try
    {
        var n = await db.Notes.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (n == null) return Results.NotFound(new { message = "Note not found. The note may have been removed." });

        // If note is linked to a product that is inactive/deleted, disallow modifications
        if (n.ProductId.HasValue)
        {
            var parent = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == n.ProductId.Value);
            if (parent == null || !parent.IsActive || parent.IsDeleted)
            {
                return Results.BadRequest(new { success = false, message = "Note for this product cannot be changed because this product is deactivated or deleted." });
            }
        }

        var uid = GetCurrentUserId(http);

        // Prepare values (preserve existing when client omitted)
        var newSubject = dto.Subject != null ? dto.Subject.Trim() : n.Subject ?? string.Empty;
        var newComment = dto.Comment != null ? dto.Comment.Trim() : n.Comment ?? string.Empty;
        int? newContractId = dto.ContractId.HasValue ? ((dto.ContractId.Value > 0) ? dto.ContractId : null) : n.ContractId;
        int? newProductId = dto.ProductId.HasValue ? ((dto.ProductId.Value > 0) ? dto.ProductId : null) : n.ProductId;

        var now = DateTime.Now; // use server local time for last_updated_dt

        // Use direct SQL update to avoid EF OUTPUT clause when table has triggers
        // Do not set `stamp` here — let any DB-side trigger or logic increment it by exactly 1.
        var updSql = $@"UPDATE dbo.note SET comment = @pComment, subject = @pSubject, contract_id = @pContract, product_id = @pProduct, is_main = @pMain, is_active = @pIsActive, last_modified_by_id = @pUid, last_updated_dt = @pNow WHERE note_id = @pNid;";
        var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)(newComment ?? string.Empty) };
        var pSubject = new Microsoft.Data.SqlClient.SqlParameter("@pSubject", System.Data.SqlDbType.NVarChar, 250) { Value = (object)(newSubject ?? string.Empty) };
        var pContract = new Microsoft.Data.SqlClient.SqlParameter("@pContract", System.Data.SqlDbType.Int) { Value = (object?)newContractId ?? DBNull.Value };
        var pProduct = new Microsoft.Data.SqlClient.SqlParameter("@pProduct", System.Data.SqlDbType.Int) { Value = (object?)newProductId ?? DBNull.Value };
        var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
        var pNow = new Microsoft.Data.SqlClient.SqlParameter("@pNow", System.Data.SqlDbType.DateTime2) { Value = now };
        var pIsActive = new Microsoft.Data.SqlClient.SqlParameter("@pIsActive", System.Data.SqlDbType.Bit) { Value = (object?)DBNull.Value };
        // main flag parameter
        var pMain = new Microsoft.Data.SqlClient.SqlParameter("@pMain", System.Data.SqlDbType.Int) { Value = DBNull.Value };
        var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = id };

        // Validate referenced contract/product existence when client provided them
        if (newContractId.HasValue)
        {
            var newContractIdCheck = newContractId.Value;
            var existsC = await db.Contracts.AsNoTracking().AnyAsync(c => c.Id == newContractIdCheck && c.IsActive && !c.IsDeleted);
            if (!existsC) return Results.BadRequest(new { success = false, message = $"Contract with id {newContractIdCheck} was not found or is not active. Please provide a valid, active Contract Id from the Contracts list." });
        }
        if (newProductId.HasValue)
        {
            var existsP = await db.Products.AsNoTracking().AnyAsync(p => p.Id == newProductId.Value && p.IsActive && !p.IsDeleted);
            if (!existsP) return Results.BadRequest(new { success = false, message = $"Product with id {newProductId.Value} was not found or is not active. Please provide a valid, active Product Id from the Products list." });
        }

        // Determine active flag: preserve existing when client omitted
        bool newIsActive;
        if (dto.IsActive.HasValue)
        {
            newIsActive = dto.IsActive.Value;
        }
        else
        {
            newIsActive = n.IsActive;
        }
        pIsActive.Value = newIsActive;

        // Determine main flag: if client provided IsMain, validate it's only allowed for product-linked notes
        int mainVal;
        if (dto.IsMain.HasValue)
        {
            mainVal = dto.IsMain.Value ? 1 : 0;
            var targetHasProduct = newProductId.HasValue || (n.ProductId.HasValue && n.ProductId.Value > 0);
            if (dto.IsMain.Value && !targetHasProduct)
            {
                return Results.BadRequest(new { success = false, message = "A note can be marked as 'Main' only when it is linked to a product. Please set Product Id first." });
            }
            pMain.Value = mainVal;
        }
        else
        {
            // preserve existing value
            mainVal = n.IsMain ? 1 : 0;
            pMain.Value = mainVal;
        }

        var affected = await db.Database.ExecuteSqlRawAsync(updSql, pComment, pSubject, pContract, pProduct, pMain, pIsActive, pUid, pNow, pNid);
        if (affected == 0)
        {
            return Results.Json(new { success = false, message = "The note could not be updated (it may have been changed by another user)." });
        }
        // Read back the actual stamp and last_updated_dt from the database (ensure we return authoritative values)
        var refreshed = await db.Notes.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Stamp, x.LastUpdatedDt }).FirstOrDefaultAsync();
        var lastModifiedByCode = await db.AxUsers.Where(u => u.Id == uid).Select(u => u.Code).FirstOrDefaultAsync();

        return Results.Json(new
        {
            success = true,
            id = id,
            stamp = refreshed?.Stamp ?? 0,
            lastUpdatedDt = (refreshed?.LastUpdatedDt.HasValue == true)
                ? refreshed.LastUpdatedDt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : now.ToString("yyyy-MM-dd HH:mm:ss"),
            lastModifiedByCode = lastModifiedByCode ?? ""
        });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Note update failed", ex.ToString(), GetCurrentUserId(http));
        return Results.Json(new { success = false, message = "Failed to save the note. Please try again later." });
    }
}).RequireAuthorization();

app.MapGet("/api/products/{id:int}/notes", async (AppDbContext db, HttpContext http, int id, string? q, int page, int pageSize) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Results.NotFound(new { message = "Product not found. The product may have been removed." });

        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        IQueryable<OnlineContract.Models.Note> notes = db.Notes
            .AsNoTracking()
            .Where(n => n.ProductId == id && !n.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            notes = notes.Where(n => EF.Functions.Like(n.Comment ?? "", $"%{s}%"));
        }

        var baseQuery =
            from n in notes
            let inputUserCode = (from u in db.AxUsers.AsNoTracking()
                                 where u.Id == n.InputUserId
                                 select u.Code).FirstOrDefault()
            let lastModifiedByCode = (from u in db.AxUsers.AsNoTracking()
                                      where u.Id == n.LastModifiedById
                                      select u.Code).FirstOrDefault()
            select new
            {
                n.Id,
                n.Comment,
                n.IsActive,
                n.IsMain,
                n.InputDt,
                inputUserCode = inputUserCode ?? "",
                lastModifiedByCode = lastModifiedByCode ?? "",
                n.LastUpdatedDt,
                n.Stamp
            };

        var totalCount = await baseQuery.CountAsync();
        var rows = await baseQuery
            .OrderByDescending(x => x.InputDt)
            .Skip(Math.Max(0, (pageIndex - 1) * size))
            .Take(size)
            .ToListAsync();

        var items = rows.Select(r => new OnlineContract.Dtos.NoteDto
        {
            Id = r.Id,
            Comment = r.Comment ?? "",
            IsActive = r.IsActive,
            IsMain = r.IsMain,
            InputDt = r.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
            InputUserId = null,
            InputUserCode = r.inputUserCode,
            LastModifiedById = null,
            LastModifiedByCode = r.lastModifiedByCode,
            LastUpdatedDt = r.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
            Stamp = r.Stamp
        });

        return Results.Json(new
        {
            items,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)size)
        });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Product notes fetch failed", ex.ToString(), GetCurrentUserId(http));
        return Results.Json(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

app.MapPut("/api/products/{id:int}/notes", async (AppDbContext db, HttpContext http, int id, NotesBulkSaveDto dto) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

    await using var tx = await db.Database.BeginTransactionAsync();
    try
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Results.NotFound(new { message = "Product not found. The product may have been removed." });

        int uid = GetCurrentUserId(http);
        // If client requested SetMainId, validate target exists, is active and stamp matches (cannot set inactive or stale note as main)
        if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
        {
            var targetCheckId = dto.SetMainId.Value;
            var targetNote = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == targetCheckId && n.ProductId == id && !n.IsDeleted);
            if (targetNote == null)
            {
                return Results.Json(new { success = false, message = "The selected note was not found. Please refresh and try again." });
            }
            if (!targetNote.IsActive)
            {
                return Results.Json(new { success = false, message = "Cannot set an inactive note as main. Please activate the note first and try again." });
            }
            if (targetNote.IsMain)
            {
                return Results.Json(new { success = false, message = "This note is already set as main. No changes were made." });
            }
            if (!dto.SetMainStamp.HasValue || dto.SetMainStamp.Value != targetNote.Stamp)
            {
                await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - setmain stamp mismatch", $"ProductId={id}; NoteId={targetCheckId}", GetCurrentUserId(http));
                return Results.Json(new { success = false, message = $"Your changes to note {targetCheckId} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
            }
        }

        // Use direct SQL operations to avoid EF OUTPUT clause issues when DB triggers are present
        foreach (var add in dto.Add ?? new List<OnlineContract.Dtos.NoteCreateDto>())
        {
            var text = (add.Comment ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp)
                VALUES ({id}, NULL, {text}, '', 0, 0, {(add.IsActive ? 1 : 0)}, dbo.GetLocalTime(), {uid}, {uid}, dbo.GetLocalTime(), 0);");
        }

        var updatedIds = (dto.Update ?? new List<OnlineContract.Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
        foreach (var upd in dto.Update ?? new List<OnlineContract.Dtos.NoteUpdateDto>())
        {
            var existing = await db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == upd.Id && x.ProductId == id && !x.IsDeleted);
            if (existing == null) continue;
            var newComment = (upd.Comment != null) ? upd.Comment.Trim() : existing.Comment;
            var newIsActive = upd.IsActive.HasValue ? upd.IsActive.Value : existing.IsActive;
            var newIsDeleted = upd.IsDeleted.HasValue ? upd.IsDeleted.Value : existing.IsDeleted;
            // Determine desired is_main for this note (if SetMain requested)
            var newIsMain = (dto.SetMainId.HasValue && dto.SetMainId.Value == upd.Id) ? 1 : (existing.IsMain ? 1 : 0);
            if (!upd.Stamp.HasValue)
            {
                await tx.RollbackAsync();
                await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - missing stamp for update", $"ProductId={id}; NoteId={upd.Id}", uid);
                return Results.Json(new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
            }

            var sql = "UPDATE dbo.note SET comment = @pComment, is_deleted = @pDeleted, is_active = @pActive, is_main = @pMain, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE note_id = @pNid AND product_id = @pPid AND stamp = @pStamp;";
            var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)(newComment ?? string.Empty) };
            var pDeleted = new Microsoft.Data.SqlClient.SqlParameter("@pDeleted", System.Data.SqlDbType.Int) { Value = (newIsDeleted ? 1 : 0) };
            var pActive = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = (newIsActive ? 1 : 0) };
            var pMain = new Microsoft.Data.SqlClient.SqlParameter("@pMain", System.Data.SqlDbType.Int) { Value = newIsMain };
            var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
            var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = upd.Id };
            var pPid = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
            var pStamp = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = upd.Stamp.Value };
            var affected = await db.Database.ExecuteSqlRawAsync(sql, pComment, pDeleted, pActive, pMain, pUid, pNid, pPid, pStamp);
            if (affected == 0)
            {
                await tx.RollbackAsync();
                await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - update", $"ProductId={id}; NoteId={upd.Id}", uid);
                return Results.Json(new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
            }
        }

        var delItems = (dto.Delete ?? new List<OnlineContract.Dtos.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
        if (delItems.Count > 0)
        {
            // update matching notes to mark deleted - do per-item with stamp check
            var sql = "UPDATE dbo.note SET is_deleted = 1, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE product_id = @pPid AND note_id = @pNid AND stamp = @pStamp;";
            foreach (var did in delItems)
            {
                if (!did.Stamp.HasValue)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - missing stamp for delete", $"ProductId={id}; NoteId={did.Id}", uid);
                    return Results.Json(new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
                var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                var pPid = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = did.Id };
                var pStamp = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = did.Stamp.Value };
                var affected = await db.Database.ExecuteSqlRawAsync(sql, pUid, pPid, pNid, pStamp);
                if (affected == 0)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - delete", $"ProductId={id}; NoteId={did.Id}", uid);
                    return Results.Json(new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
            }
        }

        if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
        {
            var targetId = dto.SetMainId.Value;
            // Unset existing mains for notes that are not part of the per-note updates (avoid double-updating same row)
            var unsetSql = "UPDATE dbo.note SET is_main = 0, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE product_id = @pPid AND is_deleted = 0 AND is_main = 1 AND note_id != @pTarget";
            if (updatedIds != null && updatedIds.Count > 0)
            {
                // Exclude updatedIds from this unset to avoid touching them twice
                unsetSql += " AND note_id NOT IN (" + string.Join(',', updatedIds) + ")";
            }
            var pUidU = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
            var pPidU = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
            var pTargetU = new Microsoft.Data.SqlClient.SqlParameter("@pTarget", System.Data.SqlDbType.Int) { Value = targetId };
            await db.Database.ExecuteSqlRawAsync(unsetSql, pUidU, pPidU, pTargetU);

            // If the target wasn't part of per-note updates, set it explicitly
            if (!(updatedIds?.Contains(targetId) ?? false))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.note SET is_main = 1, last_modified_by_id = {uid}, last_updated_dt = dbo.GetLocalTime() WHERE note_id = {targetId} AND product_id = {id} AND is_deleted = 0;");
            }
        }
        await tx.CommitAsync();

            var totalNotes = await db.Notes.CountAsync(n => n.ProductId == id && !n.IsDeleted);
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product notes saved", $"ProductId={id}; totalNotes={totalNotes}", uid);
        return Results.Json(new { success = true, message = "All note changes have been saved successfully.", totalNotes });
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        try { db.ChangeTracker.Clear(); } catch { }
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Save product notes failed", ex.ToString(), GetCurrentUserId(http));
        return Results.Json(new { success = false, message = "Failed to save notes. Please try again later." });
    }
}).RequireAuthorization();

// Contract Notes endpoints (parity)
app.MapGet("/api/contracts/{id:int}/notes", async (AppDbContext db, HttpContext http, int id, string? q, int page, int pageSize) =>
{
    // Using general authorization; ownership enforcement can be added similarly to contracts endpoints
    if (!http.User?.Identity?.IsAuthenticated ?? true) return Results.StatusCode(StatusCodes.Status401Unauthorized);

    try
    {
        var c = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return Results.StatusCode(StatusCodes.Status404NotFound);

        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        IQueryable<OnlineContract.Models.Note> notes = db.Notes
            .AsNoTracking()
            .Where(n => n.ContractId == id && !n.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            notes = notes.Where(n => EF.Functions.Like(n.Comment ?? "", $"%{s}%"));
        }

        var baseQuery =
            from n in notes
            let inputUserCode = (from u in db.AxUsers.AsNoTracking()
                                 where u.Id == n.InputUserId
                                 select u.Code).FirstOrDefault()
            let lastModifiedByCode = (from u in db.AxUsers.AsNoTracking()
                                      where u.Id == n.LastModifiedById
                                      select u.Code).FirstOrDefault()
            select new
            {
                n.Id,
                n.Comment,
                n.IsActive,
                n.IsMain,
                n.IsDeleted,
                n.InputDt,
                inputUserCode = inputUserCode ?? "",
                lastModifiedByCode = lastModifiedByCode ?? "",
                n.LastUpdatedDt,
                n.Stamp
            };

        var totalCount = await baseQuery.CountAsync();
        var rows = await baseQuery
            .OrderByDescending(x => x.InputDt)
            .Skip(Math.Max(0, (pageIndex - 1) * size))
            .Take(size)
            .ToListAsync();

        var items = rows.Select(r => new OnlineContract.Dtos.NoteDto
        {
            Id = r.Id,
            Comment = r.Comment ?? "",
            IsActive = r.IsActive,
            IsMain = r.IsMain,
            IsDeleted = r.IsDeleted,
            InputDt = r.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
            InputUserId = null,
            InputUserCode = r.inputUserCode,
            LastModifiedById = null,
            LastModifiedByCode = r.lastModifiedByCode,
            LastUpdatedDt = r.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
            Stamp = r.Stamp
        });

        return StableJson(new
        {
            items,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)size)
        });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Contract notes fetch failed", ex.ToString(), GetCurrentUserId(http));
        return StableJson(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

app.MapPut("/api/contracts/{id:int}/notes", async (AppDbContext db, HttpContext http, int id) =>
{
    if (!http.User?.Identity?.IsAuthenticated ?? true) return Results.StatusCode(StatusCodes.Status401Unauthorized);

    await using var tx = await db.Database.BeginTransactionAsync();
    try
    {
        var c = await db.Contracts.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return Results.StatusCode(StatusCodes.Status404NotFound);

        int uid = GetCurrentUserId(http);
        var dto = await http.Request.ReadFromJsonAsync<NotesBulkSaveDto>();
        if (dto == null) return Results.StatusCode(StatusCodes.Status400BadRequest);
        // If client requested SetMainId, validate target exists, is active and stamp matches (cannot set inactive or stale note as main)
        if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
        {
            var targetCheckId = dto.SetMainId.Value;
            var targetNote = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == targetCheckId && n.ContractId == id && !n.IsDeleted);
            if (targetNote == null)
            {
                return StableJson(new { success = false, message = "The selected note was not found. Please refresh and try again." });
            }
            if (!targetNote.IsActive)
            {
                return StableJson(new { success = false, message = "Cannot set an inactive note as main. Please activate the note first and try again." });
            }
            if (targetNote.IsMain)
            {
                return StableJson(new { success = false, message = "This note is already set as main. No changes were made." });
            }
            if (!dto.SetMainStamp.HasValue || dto.SetMainStamp.Value != targetNote.Stamp)
            {
                await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - setmain stamp mismatch", $"ContractId={id}; NoteId={targetCheckId}", GetCurrentUserId(http));
                return StableJson(new { success = false, message = $"Your changes to note {targetCheckId} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
            }
        }

        var isSqlite = db.Database.ProviderName?.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!isSqlite)
        {
            // SQL Server path: use direct SQL operations to avoid EF OUTPUT clause issues when DB triggers are present
            foreach (var add in dto.Add ?? new List<OnlineContract.Dtos.NoteCreateDto>())
            {
                var comment = (add.Comment ?? "").Trim();
                if (string.IsNullOrWhiteSpace(comment)) continue;
                var sqlIns = "INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp) VALUES (NULL, @pId, @pComment, @pSubject, 0, 0, @pActive, dbo.GetLocalTime(), @pUid, @pUid, dbo.GetLocalTime(), 0);";
                var pId = new Microsoft.Data.SqlClient.SqlParameter("@pId", System.Data.SqlDbType.Int) { Value = id };
                var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)comment };
                var pSubject = new Microsoft.Data.SqlClient.SqlParameter("@pSubject", System.Data.SqlDbType.NVarChar, 255) { Value = (object)((add.Subject ?? "").Trim()) };
                var pActive = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = (add.IsActive ? 1 : 0) };
                var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                await db.Database.ExecuteSqlRawAsync(sqlIns, pId, pComment, pSubject, pActive, pUid);
            }

            var updatedIds = (dto.Update ?? new List<OnlineContract.Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
            foreach (var upd in dto.Update ?? new List<OnlineContract.Dtos.NoteUpdateDto>())
            {
                var existing = await db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == upd.Id && x.ContractId == id && !x.IsDeleted);
                if (existing == null) continue;
                var newComment = (upd.Comment != null) ? upd.Comment.Trim() : existing.Comment;
                var newIsActive = upd.IsActive.HasValue ? upd.IsActive.Value : existing.IsActive;
                var newIsDeleted = upd.IsDeleted.HasValue ? upd.IsDeleted.Value : existing.IsDeleted;
                var newIsMain = (dto.SetMainId.HasValue && dto.SetMainId.Value == upd.Id) ? 1 : (existing.IsMain ? 1 : 0);

                if (!upd.Stamp.HasValue)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - missing stamp for update", $"ContractId={id}; NoteId={upd.Id}", uid);
                    return StableJson(new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }

                var sql = "UPDATE dbo.note SET comment = @pComment, is_deleted = @pDeleted, is_active = @pActive, is_main = @pMain, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE note_id = @pNid AND contract_id = @pCid AND stamp = @pStamp;";
                var pCommentU = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)(newComment ?? string.Empty) };
                var pDeletedU = new Microsoft.Data.SqlClient.SqlParameter("@pDeleted", System.Data.SqlDbType.Int) { Value = (newIsDeleted ? 1 : 0) };
                var pActiveU = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = (newIsActive ? 1 : 0) };
                var pMainU = new Microsoft.Data.SqlClient.SqlParameter("@pMain", System.Data.SqlDbType.Int) { Value = newIsMain };
                var pUidU = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                var pNidU = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = upd.Id };
                var pCid = new Microsoft.Data.SqlClient.SqlParameter("@pCid", System.Data.SqlDbType.Int) { Value = id };
                var pStampU = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = upd.Stamp.Value };
                var affectedU = await db.Database.ExecuteSqlRawAsync(sql, pCommentU, pDeletedU, pActiveU, pMainU, pUidU, pNidU, pCid, pStampU);
                if (affectedU == 0)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - update", $"ContractId={id}; NoteId={upd.Id}", uid);
                    return StableJson(new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
            }

            var delItems = (dto.Delete ?? new List<OnlineContract.Dtos.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
            if (delItems.Count > 0)
            {
                var sqlDel = "UPDATE dbo.note SET is_deleted = 1, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE contract_id = @pCid AND note_id = @pNid AND stamp = @pStamp;";
                foreach (var did in delItems)
                {
                    if (!did.Stamp.HasValue)
                    {
                        await tx.RollbackAsync();
                        await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - missing stamp for delete", $"ContractId={id}; NoteId={did.Id}", uid);
                        return StableJson(new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                    }
                    var pUidD = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                    var pCidD = new Microsoft.Data.SqlClient.SqlParameter("@pCid", System.Data.SqlDbType.Int) { Value = id };
                    var pNidD = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = did.Id };
                    var pStampD = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = did.Stamp.Value };
                    var affectedD = await db.Database.ExecuteSqlRawAsync(sqlDel, pUidD, pCidD, pNidD, pStampD);
                    if (affectedD == 0)
                    {
                        await tx.RollbackAsync();
                        await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - delete", $"ContractId={id}; NoteId={did.Id}", uid);
                        return StableJson(new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                    }
                }
            }

            if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
            {
                var targetId = dto.SetMainId.Value;
                var unsetSql = "UPDATE dbo.note SET is_main = 0, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE contract_id = @pCid AND is_deleted = 0 AND is_main = 1 AND note_id != @pTarget";
                if (updatedIds != null && updatedIds.Count > 0)
                {
                    unsetSql += " AND note_id NOT IN (" + string.Join(',', updatedIds) + ")";
                }
                var pUidU = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                var pCidU = new Microsoft.Data.SqlClient.SqlParameter("@pCid", System.Data.SqlDbType.Int) { Value = id };
                var pTargetU = new Microsoft.Data.SqlClient.SqlParameter("@pTarget", System.Data.SqlDbType.Int) { Value = targetId };
                await db.Database.ExecuteSqlRawAsync(unsetSql, pUidU, pCidU, pTargetU);

                if (!(updatedIds?.Contains(targetId) ?? false))
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.note SET is_main = 1, last_modified_by_id = {uid}, last_updated_dt = dbo.GetLocalTime() WHERE note_id = {targetId} AND contract_id = {id} AND is_deleted = 0;");
                }
            }
        }
        else
        {
            // SQLite path: perform operations via EF Core
            foreach (var add in dto.Add ?? new List<OnlineContract.Dtos.NoteCreateDto>())
            {
                var comment = (add.Comment ?? "").Trim();
                if (string.IsNullOrWhiteSpace(comment)) continue;
                var note = new OnlineContract.Models.Note
                {
                    ProductId = null,
                    ContractId = id,
                    Comment = comment,
                    Subject = (add.Subject ?? "").Trim(),
                    IsMain = false,
                    IsDeleted = false,
                    IsActive = add.IsActive,
                    InputDt = DateTime.Now,
                    InputUserId = uid,
                    LastModifiedById = uid,
                    LastUpdatedDt = DateTime.Now,
                    Stamp = 0
                };
                db.Notes.Add(note);
            }

            var updatedIds = (dto.Update ?? new List<OnlineContract.Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
            foreach (var upd in dto.Update ?? new List<OnlineContract.Dtos.NoteUpdateDto>())
            {
                var existing = await db.Notes.FirstOrDefaultAsync(x => x.Id == upd.Id && x.ContractId == id && !x.IsDeleted);
                if (existing == null) continue;
                if (!upd.Stamp.HasValue || upd.Stamp.Value != existing.Stamp)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - update", $"ContractId={id}; NoteId={upd.Id}", uid);
                    return StableJson(new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }

                existing.Comment = (upd.Comment != null) ? upd.Comment.Trim() : existing.Comment;
                existing.IsActive = upd.IsActive.HasValue ? upd.IsActive.Value : existing.IsActive;
                existing.IsDeleted = upd.IsDeleted.HasValue ? upd.IsDeleted.Value : existing.IsDeleted;
                existing.IsMain = (dto.SetMainId.HasValue && dto.SetMainId.Value == upd.Id) ? true : existing.IsMain;
                existing.LastModifiedById = uid;
                existing.LastUpdatedDt = DateTime.Now;
                existing.Stamp = existing.Stamp + 1;
            }

            var delItems = (dto.Delete ?? new List<OnlineContract.Dtos.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
            foreach (var did in delItems)
            {
                var existing = await db.Notes.FirstOrDefaultAsync(x => x.Id == did.Id && x.ContractId == id && !x.IsDeleted);
                if (existing == null) continue;
                if (!did.Stamp.HasValue || did.Stamp.Value != existing.Stamp)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - delete", $"ContractId={id}; NoteId={did.Id}", uid);
                    return StableJson(new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
                existing.IsDeleted = true;
                existing.LastModifiedById = uid;
                existing.LastUpdatedDt = DateTime.Now;
                existing.Stamp = existing.Stamp + 1;
            }

            if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
            {
                var targetId = dto.SetMainId.Value;
                var others = await db.Notes.Where(n => n.ContractId == id && !n.IsDeleted && n.Id != targetId).ToListAsync();
                foreach (var n in others)
                {
                    n.IsMain = false;
                    n.LastModifiedById = uid;
                    n.LastUpdatedDt = DateTime.Now;
                }
                if (!(updatedIds?.Contains(targetId) ?? false))
                {
                    var tgt = await db.Notes.FirstOrDefaultAsync(n => n.Id == targetId && n.ContractId == id && !n.IsDeleted);
                    if (tgt != null)
                    {
                        tgt.IsMain = true;
                        tgt.LastModifiedById = uid;
                        tgt.LastUpdatedDt = DateTime.Now;
                    }
                }
            }

            await db.SaveChangesAsync();
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Contract notes saved", $"ContractId={id}", uid);
        return StableJson(new { success = true, message = "All note changes have been saved successfully." });
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        try { db.ChangeTracker.Clear(); } catch { }
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Save contract notes failed", ex.ToString(), GetCurrentUserId(http));
        return StableJson(new { success = false, message = "Failed to save notes. Please try again later." });
    }
}).RequireAuthorization();


// Lifecycle log
var lifetime = app.Lifetime;
lifetime.ApplicationStarted.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStarted callback"));
lifetime.ApplicationStopping.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStopping callback"));

app.Run();