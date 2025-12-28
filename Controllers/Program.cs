
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;

var builder = WebApplication.CreateBuilder(args);

// -------------------------
// Services
// -------------------------

// DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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

var rewriteOptions = new RewriteOptions()
     .AddRewrite("(?i)^login$", "login.html", skipRemainingRules: true)
     .AddRewrite("(?i)^home$", "home.html", skipRemainingRules: true)
     .AddRewrite("(?i)^eventlog$", "eventlog.html", skipRemainingRules: true)
     .AddRewrite("(?i)^about$", "about.html", skipRemainingRules: true)
     .AddRewrite("(?i)^address$", "address.html", skipRemainingRules: true)
    .AddRewrite("(?i)^collections$", "collections.html", skipRemainingRules: true)
     .AddRewrite("(?i)^changestore$", "changestore.html", skipRemainingRules: true)
     .AddRewrite("(?i)^users$", "users.html", skipRemainingRules: true)
     .AddRewrite("(?i)^contracts$", "contracts.html", true);

app.UseRewriter(rewriteOptions);

if (!app.Environment.IsDevelopment())
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

// -------------------------
// Routes
// -------------------------

// Root -> /login
app.MapGet("/", context =>
{
    context.Response.Redirect("/login");
    return Task.CompletedTask;
});

// Contract details page shell
app.MapGet("/contracts/{id:int}", (HttpContext context, int id) =>
{
    // HTML shell is static; data loads via /api/contracts/{id}
    var filePath = Path.Combine(app.Environment.WebRootPath, "contract-details.html");
    return Results.File(filePath, "text/html");
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
            return Results.Json(new { success = false, message = "Invalid user code. Please check your credentials and try again." });
        }

        // verify password first so we can detect the case of correct credentials but deactivated account
        if (!PasswordHelper.VerifyPassword(dto.Password, user.Password))
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login failed - invalid password", $"Code={dto.Code}", user.Id);
            return Results.Json(new { success = false, message = "Invalid password. Please check your credentials and try again." });
        }

        // disallow signing in with team/group accounts
        if (user.IsGroup)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login failed - team account attempted", $"Code={dto.Code}", user.Id);
            return Results.Json(new { success = false, message = "Team accounts cannot be used to sign in. Please use a personal account or contact our administrator for access." });
        }

        // If user must change password, interrupt normal login and prompt client to change it now
        if (user.IsTempPassword)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login blocked - temp password requires change", $"Code={dto.Code}", user.Id);
            return Results.Json(new { success = false, mustChangePassword = true, message = "Your account requires a password change before you can continue. Please set a new password now.", stamp = user.Stamp });
        }

        // correct credentials but account inactive -> return a friendly, specific message
        if (!user.IsActive)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login failed - deactivated account", $"Code={dto.Code}", user.Id);
            return Results.Json(new { success = false, message = "Your account has been deactivated. If you need it reactivated, please contact our administrator." });
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
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
        // record last-login time using server local time
        user.LastLoginDt = DateTime.Now;
        await db.SaveChangesAsync();

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Login successful", $"User {user.Code} logged in.", user.Id);
        return Results.Json(new { success = true, userId = user.Id, roleId = user.RoleId });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Login endpoint exception", ex.ToString(), 2);
        return Results.Json(new { success = false, message = "Login failed due to a server error. Please try again later." });
    }
});

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
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });

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
app.MapGet("/api/users", async (AppDbContext db, string? name, string? team, int page, int pageSize, int? userId) =>
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

        var totalCount = await baseQuery.CountAsync();
        var items = await baseQuery
            .OrderBy(x => x.id)
            .Skip(Math.Max(0, (pageIndex - 1) * size))
            .Take(size)
            .ToListAsync();

        return Results.Json(new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size) });
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
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });

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
// EventLog + Stores (as before)
// -------------------------

app.MapGet("/api/event-log", async (AppDbContext db, int userId, int type, DateTime? from, DateTime? to, int page, int pageSize) =>
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
        var items = await (from e in query
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
                           .OrderByDescending(e => e.InputDt)
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

        return Results.Json(new { items, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize), totalCount });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "EventLog fetch failed", ex.ToString(), userId);
        return Results.Json(new { items = new object[0], totalPages = 0, totalCount = 0 });
    }
});

app.MapGet("/api/event-log/export", async (AppDbContext db, int userId, string? type, string? from, string? to) =>
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

        if (!string.IsNullOrWhiteSpace(type))
        {
            // UI sends numeric values (1,2,3) for the Type select; accept both numeric ids and textual names.
            if (int.TryParse(type.Trim(), out var typeId))
            {
                // Map UI values (1,2,3) to DB EventTypeId used elsewhere: 1->2, 2->3, 3->4
                var mappedType = typeId == 1 ? 2 : typeId == 2 ? 3 : typeId == 3 ? 4 : typeId;
                q = q.Where(x => x.EventTypeId == mappedType);
            }
            else
            {
                var t = type.Trim().ToLower();
                if (t == "information" || t == "info") q = q.Where(x => x.TypeName.ToLower() == "information");
                else if (t == "warning") q = q.Where(x => x.TypeName.ToLower() == "warning");
                else if (t == "error") q = q.Where(x => x.TypeName.ToLower() == "error");
            }
        }

        if (!string.IsNullOrWhiteSpace(from) && DateTime.TryParse(from, out var fd))
        {
            q = q.Where(x => x.InputDt >= fd);
        }
        if (!string.IsNullOrWhiteSpace(to) && DateTime.TryParse(to, out var td))
        {
            // Respect provided time component as well (do not expand to end-of-day)
            q = q.Where(x => x.InputDt <= td);
        }

        var logs = await q.OrderByDescending(x => x.InputDt).ToListAsync();

        var csv = "Id,Type,Date,Description,User,StackTrace\n" +
                  string.Join("\n", logs.Select(e =>
                      $"{e.EventLogId},{e.TypeName},{e.InputDt:yyyy-MM-dd HH:mm:ss},{e.Description?.Replace(',', ';')},{e.UserFullName?.Replace(',', ';')},{(e.StackTrace ?? string.Empty).Replace(',', ';')}"));

        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
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

app.MapGet("/api/stores", async (AppDbContext db, int? userId) =>
{
    try
    {
        var items = await (from s in db.Stores.OrderBy(s => s.StoreId)
                           join u in db.AxUsers.AsNoTracking() on s.Last_Modified_User_Id equals u.Id into uu
                           from u in uu.DefaultIfEmpty()
                           select new
                           {
                               id = s.StoreId,
                               name = s.Name,
                               address = s.Address,
                               phone = s.Phone_Number,
                               email = s.Email,
                               hours = s.Working_Hours,
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
        var phone = dto.Phone_Number?.Trim();
        var email = dto.Email?.Trim();
        var hours = dto.Working_Hours?.Trim();

        if (name is not null) store.Name = name;
        if (address is not null) store.Address = address;
        if (phone is not null) store.Phone_Number = phone;
        if (email is not null) store.Email = email;
        if (hours is not null) store.Working_Hours = hours;

        store.Last_Modified_User_Id = userId ?? 2;

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
app.MapGet("/api/contracts", async (AppDbContext db, HttpContext http, string? state, string? name, string? fromDate, string? toDate, int page, int pageSize) =>
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

        // Date range filters (EntryDate)
        if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fd))
        {
            q = q.Where(x => x.EntryDate >= fd);
        }
        if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var td))
        {
            var tdEnd = td.Date.AddDays(1).AddTicks(-1);
            q = q.Where(x => x.EntryDate <= tdEnd);
        }

        var totalCount = await q.CountAsync();

        var pageRows = await q
            .OrderByDescending(x => x.EntryDate)
            .ThenBy(x => x.Id)
            .Skip(Math.Max(0, (pageIndex - 1) * size))
            .Take(size)
            .ToListAsync();

        var items = pageRows.Select(x => new
        {
            id = x.Id,
            customerFullName = x.CustomerFullName,
            amount = x.Amount,
            contractState = x.ContractState.ToString(),
            entryDate = x.EntryDate.ToString("yyyy-MM-dd HH:mm:ss")
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
        await LoggerHelper.LogEventAsync(db, OnlineContract.Helpers.EventType.Error, "Contracts fetch failed", ex.ToString(), 2);
        return Results.Json(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

app.MapGet("/api/contracts/{id:int}", async (AppDbContext db, HttpContext http, int id) =>
{
    if (id <= 0) return Results.NotFound(new { message = "Contract not found. Please verify the contract ID and try again." });

    var roleClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
    int.TryParse(roleClaim, out var roleId);
    var userIdClaim = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
    int.TryParse(userIdClaim, out var currentUserId);
    var isCustomer = roleId == (int)UserRole.Customer;

    var row = await (
        from c in db.Contracts.AsNoTracking()
        join u0 in db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
        from u in ug.DefaultIfEmpty()
        where c.Id > 0 && c.Id == id && (!isCustomer || (c.InputUserId ?? 0) == currentUserId)
        select new
        {
            c.Id,
            c.EntryDate,
            c.InputUserId,
            c.ContractState,
            c.LastModifiedById,
            c.LastUpdatedDt,
            c.Stamp,
            CustomerFullName = u == null
                ? ""
                : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
        }
    ).FirstOrDefaultAsync();

    if (row == null) return Results.NotFound(new { message = "Contract not found. The contract may have been removed." });

    return Results.Json(new
    {
        id = row.Id,
        customerFullName = row.CustomerFullName,
        inputUserId = row.InputUserId,
        contractState = row.ContractState.ToString(),
        entryDate = row.EntryDate.ToString("yyyy-MM-dd HH:mm:ss"),
        lastModifiedById = row.LastModifiedById,
        lastUpdatedDt = row.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss"),
        stamp = row.Stamp
    });
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
        sb.AppendLine("Id,CustomerFullName,Amount,ContractState,EntryDate");
        foreach (var r in rows)
        {
            sb.Append(CsvEscape(r.Id.ToString()));
            sb.Append(',');
            sb.Append(CsvEscape(r.CustomerFullName));
            sb.Append(',');
            sb.Append(CsvEscape(r.Amount.ToString()));
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

static bool IsValidEmail(string? email)
{
    if (string.IsNullOrWhiteSpace(email)) return false;
    var e = email.Trim();
    return System.Text.RegularExpressions.Regex.IsMatch(e, @"^\S+@\S+\.\S+$");
}

// Phone normalization now handled by OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone

app.MapGet("/api/products", async (AppDbContext db, HttpContext http, string? q, int? storeId, int page, int pageSize) =>
{
    if (!CanManageProducts(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        IQueryable<Product> products = db.Products
            .AsNoTracking()
            .Where(p => p.Id > 0 && !p.IsDeleted && p.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            products = products.Where(p =>
                EF.Functions.Like(p.Name ?? "", $"%{s}%"));
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

        var rows = await baseQuery
            .OrderBy(x => x.Id)
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

        return Results.Json(new
        {
            items,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)size)
        });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Products fetch failed", ex.ToString(), 2);
        return Results.Json(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

app.MapGet("/api/products/{id:int}", async (AppDbContext db, HttpContext http, int id) =>
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

    var variants = await db.ProductVariants.AsNoTracking()
        .Where(v => v.ProductId == id && !v.IsDeleted)
        .OrderBy(v => v.Id)
        .ToListAsync();

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
            InputDt = DateTime.UtcNow,
            InputUserId = uid,
            LastModifiedById = uid,
            LastUpdatedDt = DateTime.UtcNow,
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
        p.LastUpdatedDt = DateTime.UtcNow;

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
        
        

        // Basic product fields
        if (dto.Name is not null)        p.Name = dto.Name.Trim();
        if (dto.IsActive.HasValue)       p.IsActive = dto.IsActive.Value;
        p.LastModifiedById = uid;
        p.LastUpdatedDt    = DateTime.UtcNow;

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
                v.LastUpdatedDt    = DateTime.UtcNow;

                var invs = await db.ProductInventories
                    .Where(i => i.ProductVariantId == v.Id && !i.IsDeleted)
                    .ToListAsync();

                foreach (var inv in invs)
                {
                    inv.IsDeleted        = true;
                    inv.IsActive         = false;
                    inv.LastModifiedById = uid;
                    inv.LastUpdatedDt    = DateTime.UtcNow;
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
                    InputDt          = DateTime.UtcNow,
                    InputUserId      = uid,
                    LastModifiedById = uid,
                    LastUpdatedDt    = DateTime.UtcNow,
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
                v.LastUpdatedDt    = DateTime.UtcNow;
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
                        InputDt          = DateTime.UtcNow,
                        InputUserId      = uid,
                        LastModifiedById = uid,
                        LastUpdatedDt    = DateTime.UtcNow,
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
                    inv.LastUpdatedDt    = DateTime.UtcNow;
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
            foreach (var add in notesDto.Add ?? new List<OnlineContract.Models.NoteCreateDto>())
            {
                var text = (add.Comment ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp) VALUES ({id}, NULL, {text}, '', 0, 0, {(add.IsActive ? 1 : 0)}, GETUTCDATE(), {GetCurrentUserId(http)}, {GetCurrentUserId(http)}, GETUTCDATE(), 0);");
            }

            // Update
            var updatedIds = (notesDto.Update ?? new List<OnlineContract.Models.NoteUpdateDto>()).Select(u => u.Id).ToList();
            foreach (var upd in notesDto.Update ?? new List<OnlineContract.Models.NoteUpdateDto>())
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

                var sql = "UPDATE dbo.note SET comment = @pComment, is_deleted = @pDeleted, is_active = @pActive, is_main = @pMain, last_modified_by_id = @pUid, last_updated_dt = GETUTCDATE() WHERE note_id = @pNid AND product_id = @pPid AND stamp = @pStamp;";
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
            var delItems = (notesDto.Delete ?? new List<OnlineContract.Models.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
            if (delItems.Count > 0)
            {
                var sql = "UPDATE dbo.note SET is_deleted = 1, last_modified_by_id = @pUid, last_updated_dt = GETUTCDATE() WHERE product_id = @pPid AND note_id = @pNid AND stamp = @pStamp;";
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
                updatedIds = (notesDto.Update ?? new List<OnlineContract.Models.NoteUpdateDto>()).Select(u => u.Id).ToList();
                var unsetSql = "UPDATE dbo.note SET is_main = 0, last_modified_by_id = @pUid, last_updated_dt = GETUTCDATE() WHERE product_id = @pPid AND is_deleted = 0 AND is_main = 1 AND note_id != @pTarget";
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
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.note SET is_main = 1, last_modified_by_id = {GetCurrentUserId(http)}, last_updated_dt = GETUTCDATE() WHERE note_id = {targetId} AND product_id = {id} AND is_deleted = 0;");
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
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
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

        // Deactivate product
        p.IsActive = false;
        p.Stamp = p.Stamp + 1;
        p.LastModifiedById = uid;
        p.LastUpdatedDt = DateTime.UtcNow;

        // Deactivate and mark as inactive the product variants
        var variants = await db.ProductVariants.Where(v => v.ProductId == id && !v.IsDeleted).ToListAsync();
        var now = DateTime.UtcNow;
        var variantIds = new List<int>();
        foreach (var v in variants)
        {
            v.IsActive = false;
            v.LastModifiedById = uid;
            v.LastUpdatedDt = now;
            variantIds.Add(v.Id);
        }

        // Deactivate related inventory rows
        if (variantIds.Count > 0)
        {
            var inventories = await db.ProductInventories.Where(i => variantIds.Contains(i.ProductVariantId) && !i.IsDeleted).ToListAsync();
            foreach (var inv in inventories)
            {
                inv.IsActive = false;
                inv.LastModifiedById = uid;
                inv.LastUpdatedDt = now;
            }
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product deactivated", $"ProductId={p.Id}; variants={variantIds.Count}", uid);
        return Results.Ok(new { success = true, message = "Product has been deactivated successfully.", variantCount = variantIds.Count });
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
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Results.NotFound(new { message = "Product not found. The product may have been removed." });
        var uid = GetCurrentUserId(http);
        // optimistic concurrency: require client to supply current stamp
        if (!stamp.HasValue || stamp.Value != p.Stamp)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Product activate conflict - stamp mismatch", $"ProductId={id}", uid);
            return Results.Json(new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
        }
        p.IsActive = true;
        p.Stamp = p.Stamp + 1;
        p.LastModifiedById = uid;
        p.LastUpdatedDt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product activated", $"ProductId={p.Id}", uid);
        return Results.Ok(new { success = true, message = "Product has been activated successfully." });
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

        // Deactivate and mark as deleted the product itself
        p.IsActive = false;
        p.IsDeleted = true;
        p.LastModifiedById = uid;
        p.LastUpdatedDt = DateTime.UtcNow;

        // Find variants for this product and mark them deleted/deactivated
        var variants = await db.ProductVariants.Where(v => v.ProductId == id && !v.IsDeleted).ToListAsync();
        var now = DateTime.UtcNow;
        var variantIds = new List<int>();
        foreach (var v in variants)
        {
            v.IsActive = false;
            v.IsDeleted = true;
            v.LastModifiedById = uid;
            v.LastUpdatedDt = now;
            variantIds.Add(v.Id);
        }

        // Find related inventory rows and mark them deleted/deactivated
        if (variantIds.Count > 0)
        {
            var inventories = await db.ProductInventories.Where(i => variantIds.Contains(i.ProductVariantId) && !i.IsDeleted).ToListAsync();
            foreach (var inv in inventories)
            {
                inv.IsActive = false;
                inv.IsDeleted = true;
                inv.LastModifiedById = uid;
                inv.LastUpdatedDt = now;
            }
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Product deleted", $"ProductId={p.Id}; variants={variantIds.Count}", uid);
        return Results.Ok(new { success = true, message = "Product has been deleted successfully.", variantCount = variantIds.Count });
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
app.MapGet("/api/notes", async (AppDbContext db, int? contractId, int? productId, int page, int pageSize) =>
{
    try
    {
        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        var q = db.Notes.AsNoTracking().Where(n => !n.IsDeleted);
        if (contractId.HasValue && contractId.Value > 0) q = q.Where(n => n.ContractId == contractId.Value);
        if (productId.HasValue && productId.Value > 0) q = q.Where(n => n.ProductId == productId.Value);

        var totalCount = await q.CountAsync();

        var rows = await q.OrderByDescending(n => n.InputDt)
                          .Skip(Math.Max(0, (pageIndex - 1) * size))
                          .Take(size)
                          .Select(n => new {
                              id = n.Id,
                              contractId = n.ContractId,
                              productId = n.ProductId,
                              subject = n.Subject ?? "",
                              inputDt = n.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                              inputUserId = n.InputUserId,
                              status = n.IsActive ? "Active" : "Inactive"
                          })
                          .ToListAsync();

        return Results.Json(new { items = rows, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size) });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Notes fetch failed", ex.ToString(), 2);
        return Results.Json(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

// API: add note (simple)
app.MapPost("/api/notes", async (AppDbContext db, NoteCreateSimpleDto dto, HttpContext http) =>
{
    try
    {
        var uid = GetCurrentUserId(http);
        var note = new OnlineContract.Models.Note
        {
            ContractId = (dto.ContractId.HasValue && dto.ContractId.Value > 0) ? dto.ContractId : null,
            ProductId = (dto.ProductId.HasValue && dto.ProductId.Value > 0) ? dto.ProductId : null,
            Comment = (dto.Comment ?? dto.Subject) ?? string.Empty,
            Subject = dto.Subject ?? dto.Comment ?? string.Empty,
            IsActive = dto.IsActive ?? true,
            IsDeleted = false,
            InputDt = DateTime.UtcNow,
            InputUserId = uid,
            LastModifiedById = uid,
            LastUpdatedDt = DateTime.UtcNow,
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

        var items = rows.Select(r => new OnlineContract.Models.NoteDto
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
        foreach (var add in dto.Add ?? new List<OnlineContract.Models.NoteCreateDto>())
        {
            var text = (add.Comment ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp)
                VALUES ({id}, NULL, {text}, '', 0, 0, {(add.IsActive ? 1 : 0)}, GETUTCDATE(), {uid}, {uid}, GETUTCDATE(), 0);");
        }

        var updatedIds = (dto.Update ?? new List<OnlineContract.Models.NoteUpdateDto>()).Select(u => u.Id).ToList();
        foreach (var upd in dto.Update ?? new List<OnlineContract.Models.NoteUpdateDto>())
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

            var sql = "UPDATE dbo.note SET comment = @pComment, is_deleted = @pDeleted, is_active = @pActive, is_main = @pMain, last_modified_by_id = @pUid, last_updated_dt = GETUTCDATE() WHERE note_id = @pNid AND product_id = @pPid AND stamp = @pStamp;";
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

        var delItems = (dto.Delete ?? new List<OnlineContract.Models.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
        if (delItems.Count > 0)
        {
            // update matching notes to mark deleted - do per-item with stamp check
            var sql = "UPDATE dbo.note SET is_deleted = 1, last_modified_by_id = @pUid, last_updated_dt = GETUTCDATE() WHERE product_id = @pPid AND note_id = @pNid AND stamp = @pStamp;";
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
            var unsetSql = "UPDATE dbo.note SET is_main = 0, last_modified_by_id = @pUid, last_updated_dt = GETUTCDATE() WHERE product_id = @pPid AND is_deleted = 0 AND is_main = 1 AND note_id != @pTarget";
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
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.note SET is_main = 1, last_modified_by_id = {uid}, last_updated_dt = GETUTCDATE() WHERE note_id = {targetId} AND product_id = {id} AND is_deleted = 0;");
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
        if (c == null) return Results.NotFound(new { message = "Contract not found. The contract may have been removed." });

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

        var items = rows.Select(r => new OnlineContract.Models.NoteDto
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
            LastUpdatedDt = r.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
            ,
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
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Contract notes fetch failed", ex.ToString(), GetCurrentUserId(http));
        return Results.Json(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
    }
}).RequireAuthorization();

app.MapPut("/api/contracts/{id:int}/notes", async (AppDbContext db, HttpContext http, int id, NotesBulkSaveDto dto) =>
{
    if (!http.User?.Identity?.IsAuthenticated ?? true) return Results.StatusCode(StatusCodes.Status401Unauthorized);

    await using var tx = await db.Database.BeginTransactionAsync();
    try
    {
        var c = await db.Contracts.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return Results.NotFound(new { message = "Contract not found. The contract may have been removed." });

        int uid = GetCurrentUserId(http);

        // If client requested SetMainId, validate target exists, is active and stamp matches (cannot set inactive or stale note as main)
        if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
        {
            var targetCheckId = dto.SetMainId.Value;
            var targetNote = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == targetCheckId && n.ContractId == id && !n.IsDeleted);
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
                await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - setmain stamp mismatch", $"ContractId={id}; NoteId={targetCheckId}", GetCurrentUserId(http));
                return Results.Json(new { success = false, message = $"Your changes to note {targetCheckId} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
            }
        }

        // Use direct SQL operations to avoid EF OUTPUT clause issues when DB triggers are present
        foreach (var add in dto.Add ?? new List<OnlineContract.Models.NoteCreateDto>())
        {
            var comment = (add.Comment ?? "").Trim();
            if (string.IsNullOrWhiteSpace(comment)) continue;
            var sqlIns = "INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp) VALUES (NULL, @pId, @pComment, '', 0, 0, @pActive, GETUTCDATE(), @pUid, @pUid, GETUTCDATE(), 0);";
            var pId = new Microsoft.Data.SqlClient.SqlParameter("@pId", System.Data.SqlDbType.Int) { Value = id };
            var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)comment };
            var pActive = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = (add.IsActive ? 1 : 0) };
            var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
            await db.Database.ExecuteSqlRawAsync(sqlIns, pId, pComment, pActive, pUid);
        }

        var updatedIds = (dto.Update ?? new List<OnlineContract.Models.NoteUpdateDto>()).Select(u => u.Id).ToList();
        foreach (var upd in dto.Update ?? new List<OnlineContract.Models.NoteUpdateDto>())
        {
            var existing = await db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == upd.Id && x.ContractId == id && !x.IsDeleted);
            if (existing == null) continue;
            var newComment = (upd.Comment != null) ? upd.Comment.Trim() : existing.Comment;
            var newIsActive = upd.IsActive.HasValue ? upd.IsActive.Value : existing.IsActive;
            var newIsDeleted = upd.IsDeleted.HasValue ? upd.IsDeleted.Value : existing.IsDeleted;
            // Determine desired is_main for this note (if SetMain requested)
            var newIsMain = (dto.SetMainId.HasValue && dto.SetMainId.Value == upd.Id) ? 1 : (existing.IsMain ? 1 : 0);

            if (!upd.Stamp.HasValue)
            {
                await tx.RollbackAsync();
                await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - missing stamp for update", $"ContractId={id}; NoteId={upd.Id}", uid);
                return Results.Json(new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
            }

            var sql = "UPDATE dbo.note SET comment = @pComment, is_deleted = @pDeleted, is_active = @pActive, is_main = @pMain, last_modified_by_id = @pUid, last_updated_dt = GETUTCDATE() WHERE note_id = @pNid AND contract_id = @pCid AND stamp = @pStamp;";
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
                return Results.Json(new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
            }
        }

        var delItems = (dto.Delete ?? new List<OnlineContract.Models.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
        if (delItems.Count > 0)
        {
            var sqlDel = "UPDATE dbo.note SET is_deleted = 1, last_modified_by_id = @pUid, last_updated_dt = GETUTCDATE() WHERE contract_id = @pCid AND note_id = @pNid AND stamp = @pStamp;";
            foreach (var did in delItems)
            {
                if (!did.Stamp.HasValue)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(db, EventType.Warning, "Note save conflict - missing stamp for delete", $"ContractId={id}; NoteId={did.Id}", uid);
                    return Results.Json(new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
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
                    return Results.Json(new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
            }
        }

        if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
        {
            var targetId = dto.SetMainId.Value;
            var unsetSql = "UPDATE dbo.note SET is_main = 0, last_modified_by_id = @pUid, last_updated_dt = GETUTCDATE() WHERE contract_id = @pCid AND is_deleted = 0 AND is_main = 1 AND note_id != @pTarget";
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
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.note SET is_main = 1, last_modified_by_id = {uid}, last_updated_dt = GETUTCDATE() WHERE note_id = {targetId} AND contract_id = {id} AND is_deleted = 0;");
            }
        }
        await tx.CommitAsync();

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Contract notes saved", $"ContractId={id}", uid);
        return Results.Json(new { success = true, message = "All note changes have been saved successfully." });
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        try { db.ChangeTracker.Clear(); } catch { }
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Save contract notes failed", ex.ToString(), GetCurrentUserId(http));
        return Results.Json(new { success = false, message = "Failed to save notes. Please try again later." });
    }
}).RequireAuthorization();

// Lifecycle log
var lifetime = app.Lifetime;
lifetime.ApplicationStarted.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStarted callback"));
lifetime.ApplicationStopping.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStopping callback"));

app.Run();
