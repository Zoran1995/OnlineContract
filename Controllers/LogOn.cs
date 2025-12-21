
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Security.Claims;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;
using System.ComponentModel.DataAnnotations;

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
     .AddRewrite("(?i)^products$", "products.html", skipRemainingRules: true)
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
        var user = await db.AxUsers.FirstOrDefaultAsync(u => u.Code == dto.Code && u.IsActive && !u.IsDeleted);
        if (user == null)
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login failed - invalid user code", $"Code={dto.Code}", 2);
            return Results.Json(new { success = false, message = "Invalid user code." });
        }
        if (!PasswordHelper.VerifyPassword(dto.Password, user.Password))
        {
            await LoggerHelper.LogEventAsync(db, EventType.Warning, "Login failed - invalid password", $"Code={dto.Code}", user.Id);
            return Results.Json(new { success = false, message = "Invalid password." });
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

        await LoggerHelper.LogEventAsync(db, EventType.Information, "Login successful", $"User {user.Code} logged in.", user.Id);
        return Results.Json(new { success = true, userId = user.Id, roleId = user.RoleId });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Login endpoint exception", ex.ToString(), 2);
        return Results.Json(new { success = false, message = "Login failed due to server error." });
    }
});

app.MapPost("/api/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/register", async (AppDbContext db, RegisterDto dto) =>
{
    try
    {
        var username = (dto.Username ?? "").Trim();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(dto.Password))
            return Results.Json(new { success = false, message = "Username and password are required." });

        var exists = await db.AxUsers.AnyAsync(u => u.Code == username);
        if (exists)
            return Results.Json(new { success = false, message = "Username already exists." });

        var email = (dto.Email ?? "").Trim();
        var emailValid = System.Text.RegularExpressions.Regex.IsMatch(email, @"^\S+@\S+\.\S+$");
        if (!emailValid)
            return Results.Json(new { success = false, message = "Invalid email format." });

        exists = await db.AxUsers.AnyAsync(u => u.Email == email);
        if (exists)
            return Results.Json(new { success = false, message = "Email already exists." });

        var assignedRole = dto.RoleId ?? (int)UserRole.Customer;

        var newUser = new AxUser
        {
            FirstName = dto.FirstName ?? "",
            LastName = dto.LastName ?? "",
            Email = dto.Email ?? "",
            Phone = "+381" + (dto.Phone ?? ""),
            Code = username,
            Password = PasswordHelper.HashPassword(dto.Password),
            IsGroup = false,
            IsActive = true,
            IsDeleted = false,
            OwnerId = 0,
            CreatedDt = DateTime.UtcNow,
            PasswordDt = DateTime.UtcNow,
            LastLoginDt = DateTime.UtcNow,
            City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City?.Trim(),
            StreetAddress = string.IsNullOrWhiteSpace(dto.StreetAddress) ? null : dto.StreetAddress?.Trim(),
            PostalCode = string.IsNullOrWhiteSpace(dto.PostalCode) ? null : dto.PostalCode?.Trim(),
            RoleId = assignedRole
        };

        db.AxUsers.Add(newUser);
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "New Account successfully created", $"User {username} created.", newUser.Id);
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
                isActive = u.IsActive,
                isDeleted = u.IsDeleted,
                isGroup = u.IsGroup,
                ownerId = u.OwnerId,

                // Group column should show human-readable name (Code of the group user)
                groupName = g != null ? g.Code : null
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
        var query = db.AxUsers.Where(x => x.IsGroup && !x.IsDeleted);

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
            return Results.Json(new { success = false, message = "Code is required." });

        var exists = await db.AxUsers.AnyAsync(u => u.Code == code);
        if (exists)
            return Results.Json(new { success = false, message = "Code already exists." });

        var u = new AxUser
        {
            Code = code,
            FirstName = dto.FirstName ?? "",
            LastName = dto.LastName ?? "",
            Email = dto.Email ?? "",
            Phone = dto.Phone ?? "",
            RoleId = dto.RoleId,
            IsGroup = dto.IsGroup,
            OwnerId = dto.OwnerId,
            City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City?.Trim(),
            StreetAddress = string.IsNullOrWhiteSpace(dto.StreetAddress) ? null : dto.StreetAddress?.Trim(),
            PostalCode = string.IsNullOrWhiteSpace(dto.PostalCode) ? null : dto.PostalCode?.Trim(),
            IsActive = true,
            IsDeleted = false,
            CreatedDt = DateTime.UtcNow,
            PasswordDt = DateTime.UtcNow,
            LastLoginDt = null,
            Stamp = 0,
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
        return Results.Json(new { success = false, message = "Create failed." });
    }
}).RequireAuthorization();

app.MapPut("/api/users/{id}", async (AppDbContext db, int id, UserUpdateDto dto, int? userId) =>
{
    try
    {
        var u = await db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return Results.NotFound(new { message = "User not found." });

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var email = dto.Email.Trim();
            var existsEmail = await db.AxUsers.AnyAsync(x => x.Email == email && x.Id != id);
            if (existsEmail) return Results.Json(new { success = false, message = "Email already exists." });
        }

        if (!string.IsNullOrWhiteSpace(dto.Code))
        {
            var newCode = dto.Code.Trim();
            if (!string.Equals(newCode, u.Code, StringComparison.Ordinal))
            {
                var existsCode = await db.AxUsers.AnyAsync(x => x.Code == newCode && x.Id != id);
                if (existsCode) return Results.Json(new { success = false, message = "Code already exists." });
                u.Code = newCode;
            }
        }

        if (dto.FirstName != null) u.FirstName = dto.FirstName.Trim();
        if (dto.LastName != null) u.LastName = dto.LastName.Trim();
        if (dto.Email != null) u.Email = dto.Email.Trim();
        if (dto.Phone != null) u.Phone = dto.Phone.Trim();
        if (dto.RoleId.HasValue) u.RoleId = dto.RoleId.Value;
        if (dto.IsActive.HasValue) u.IsActive = dto.IsActive.Value;
        if (dto.OwnerId.HasValue) u.OwnerId = dto.OwnerId.Value;
        if (dto.City != null) u.City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim();
        if (dto.StreetAddress != null) u.StreetAddress = string.IsNullOrWhiteSpace(dto.StreetAddress) ? null : dto.StreetAddress.Trim();
        if (dto.PostalCode != null) u.PostalCode = string.IsNullOrWhiteSpace(dto.PostalCode) ? null : dto.PostalCode.Trim();
        if (!u.IsGroup && dto.Password != null)
        {
            u.Password = PasswordHelper.HashPassword(dto.Password);
            u.PasswordDt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "User updated", $"Code={u.Code}", userId ?? 2);
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Update user failed", ex.ToString(), userId ?? 2);
        return Results.Json(new { success = false });
    }
}).RequireAuthorization();

app.MapPost("/api/users/{id}/deactivate", async (AppDbContext db, int id, int? userId) =>
{
    try
    {
        var u = await db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return Results.NotFound(new { message = "User not found." });
        u.IsActive = false;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "User deactivated", $"Code={u.Code}", userId ?? 2);
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Deactivate user failed", ex.ToString(), userId ?? 2);
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

app.MapPost("/api/users/{id}/activate", async (AppDbContext db, int id, int? userId) =>
{
    try
    {
        var u = await db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return Results.NotFound(new { message = "User not found." });
        u.IsActive = true;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "User activated", $"Code={u.Code}", userId ?? 2);
        return Results.Ok(new { success = true });
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
        if (u == null) return Results.NotFound(new { message = "User not found." });
        u.IsDeleted = true;
        await db.SaveChangesAsync();
        await LoggerHelper.LogEventAsync(db, EventType.Information, "User deleted", $"Code={u.Code}", userId ?? 2);
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Delete user failed", ex.ToString(), userId ?? 2);
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// -------------------------
// EventLog + Stores (kao i do sada)
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

app.MapGet("/api/event-log/export", async (AppDbContext db, int userId) =>
{
    try
    {
        var logs = await (from e in db.EventLogs
                          join u in db.AxUsers on e.UserId equals u.Id into users
                          from u in users.DefaultIfEmpty()
                          select new
                          {
                              e.EventLogId,
                              TypeName = e.EventTypeId == 2 ? "Information" : e.EventTypeId == 3 ? "Warning" : "Error",
                              e.InputDt,
                              e.Description,
                              UserFullName = u != null ? (u.FirstName + " " + u.LastName).Trim() : $"User {e.UserId}",
                              e.StackTrace
                          })
                          .OrderByDescending(x => x.InputDt)
                          .ToListAsync();

        var csv = "Id,Type,Date,Description,User,StackTrace\n" +
                  string.Join("\n", logs.Select(e =>
                      $"{e.EventLogId},{e.TypeName},{e.InputDt:yyyy-MM-dd HH:mm:ss},{e.Description},{e.UserFullName},{e.StackTrace?.Replace(",", ";")}"));

        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return Results.File(bytes, "text/csv", "eventlog.csv");
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
        var items = await db.Stores
            .OrderBy(s => s.StoreId)
            .Select(s => new
            {
                id = s.StoreId,
                name = s.Name,
                address = s.Address,
                phone = s.Phone_Number,
                email = s.Email,
                hours = s.Working_Hours
            })
            .ToListAsync();

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
        if (store == null) return Results.NotFound(new { message = "Store not found." });

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
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Update store failed", ex.ToString(), userId ?? 2);
        return Results.StatusCode(500);
    }
});

// Contracts API (Authorized)
app.MapGet("/api/contracts", async (AppDbContext db, string? state, string? name, int page, int pageSize) =>
{
    try
    {
        var pageIndex = page < 1 ? 1 : page;
        var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

        var q =
            from c in db.Contracts.AsNoTracking()
            join u0 in db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
            from u in ug.DefaultIfEmpty()
            where c.Id > 0
            select new
            {
                c.Id,
                c.EntryDate,
                c.ContractState,
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

app.MapGet("/api/contracts/{id:int}", async (AppDbContext db, int id) =>
{
    if (id <= 0) return Results.NotFound(new { message = "Contract not found." });

    var row = await (
        from c in db.Contracts.AsNoTracking()
        join u0 in db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
        from u in ug.DefaultIfEmpty()
        where c.Id > 0 && c.Id == id
        select new
        {
            c.Id,
            c.EntryDate,
            c.ContractState,
            CustomerFullName = u == null
                ? ""
                : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
        }
    ).FirstOrDefaultAsync();

    if (row == null) return Results.NotFound(new { message = "Contract not found." });

    return Results.Json(new
    {
        id = row.Id,
        customerFullName = row.CustomerFullName,
        contractState = row.ContractState.ToString(),
        entryDate = row.EntryDate.ToString("yyyy-MM-dd HH:mm:ss")
    });
}).RequireAuthorization();

app.MapGet("/api/contracts/export", async (AppDbContext db, string? state, string? name) =>
{
    try
    {
        var q =
            from c in db.Contracts.AsNoTracking()
            join u0 in db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
            from u in ug.DefaultIfEmpty()
            where c.Id > 0
            select new
            {
                c.Id,
                c.EntryDate,
                c.ContractState,
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
        sb.AppendLine("Id,CustomerFullName,ContractState,EntryDate");
        foreach (var r in rows)
        {
            sb.Append(CsvEscape(r.Id.ToString()));
            sb.Append(',');
            sb.Append(CsvEscape(r.CustomerFullName));
            sb.Append(',');
            sb.Append(CsvEscape(r.ContractState.ToString()));
            sb.Append(',');
            sb.Append(CsvEscape(r.EntryDate.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine();
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return Results.File(bytes, "text/csv; charset=utf-8", "contracts.csv");
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, OnlineContract.Helpers.EventType.Error, "Contracts export failed", ex.ToString(), 2);
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Lifecycle log
var lifetime = app.Lifetime;
lifetime.ApplicationStarted.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStarted callback"));
lifetime.ApplicationStopping.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStopping callback"));

app.Run();
