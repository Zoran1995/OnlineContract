using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Rewrite;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;


var builder = WebApplication.CreateBuilder(args);

// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Rewrite rules: /login → /login.html
var rewriteOptions = new RewriteOptions()
	    .AddRewrite("(?i)^login$", "login.html", skipRemainingRules: true)
	    .AddRewrite("(?i)^home$", "home.html", skipRemainingRules: true)
	    .AddRewrite("(?i)^eventlog$", "eventlog.html", skipRemainingRules: true)
	    .AddRewrite("(?i)^about$", "about.html", skipRemainingRules: true)
        .AddRewrite("(?i)^address$", "address.html", skipRemainingRules: true)
    .AddRewrite("(?i)^products$", "products.html", skipRemainingRules: true)
    .AddRewrite("(?i)^changestore$", "changestore.html", skipRemainingRules: true);
app.UseRewriter(rewriteOptions);

// Serve static files from wwwroot
app.UseStaticFiles();

// Root → redirect na login
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

// LOGIN ENDPOINT
app.MapPost("/api/login", async (AppDbContext db, LoginDto dto) =>
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

    await LoggerHelper.LogEventAsync(db, EventType.Information, "Login successful", $"User {user.Code} logged in.", user.Id);
    return Results.Json(new { success = true, userId = user.Id, roleId = user.RoleId });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Login endpoint exception", ex.ToString(), 2);
        return Results.Json(new { success = false, message = "Login failed due to server error." });
    }
});

// REGISTER ENDPOINT
app.MapPost("/api/register", async (AppDbContext db, RegisterDto dto) =>
{
    try
    {
        var username = (dto.Username ?? "").Trim();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(dto.Password))
        {
            return Results.Json(new { success = false, message = "Username and password are required." });
        }

        // Prevent duplicate usernames
        var exists = await db.AxUsers.AnyAsync(u => u.Code == username);
        if (exists)
        {
            return Results.Json(new { success = false, message = "Username already exists." });
        }

        // Prevent duplicate emails
        var email = (dto.Email ?? "").Trim();
        // Basic email format validation
        var emailValid = System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
        if (!emailValid)
        {
            return Results.Json(new { success = false, message = "Invalid email format." });
        }
        exists = await db.AxUsers.AnyAsync(u => u.Email == email);
        if (exists)
        {
            return Results.Json(new { success = false, message = "Email already exists." });
        }

        // Assign role: default Customer when not provided
        var assignedRole = dto.RoleId ?? (int)UserRole.Customer;

        var newUser = new AxUser
        {
            FirstName = dto.FirstName ?? "",
            LastName = dto.LastName ?? "",
            Email = dto.Email ?? "",
            Phone = "+381" + dto.Phone ?? "",
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

// CLIENT ERROR ENDPOINT
app.MapPost("/api/log-client-error", async (AppDbContext db, ClientErrorDto dto) =>
{
    try
    {
        var type = dto.EventTypeOverride?.ToLower() switch
        {
            "warning" => EventType.Warning,
            "information" => EventType.Information,
            _ => EventType.Error
        };

        await LoggerHelper.LogEventAsync(db, type, dto.Description, dto.StackTrace ?? "", dto.UserId);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "Client-error endpoint failed", ex.ToString(), 2);
        return Results.StatusCode(500);
    }
});

// Simple GET for /api/login
app.MapGet("/api/login", () =>
{
    return Results.Json(new { status = "Login endpoint is alive." });
});

// EVENT LOG ENDPOINT
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

        if (from.HasValue)
            query = query.Where(e => e.InputDt >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.InputDt <= to.Value);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

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
            .Select(e => new {
                id = e.EventLogId,
                type = e.EventTypeId == 2 ? "Information" : e.EventTypeId == 3 ? "Warning" : "Error",
                date = e.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                description = e.Description,
                user = e.Code,
                stackTrace = e.StackTrace
            })
            .ToListAsync();

        return Results.Json(new { items, totalPages, totalCount });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(db, EventType.Error, "EventLog fetch failed", ex.ToString(), userId);
        return Results.Json(new { items = new object[0], totalPages = 0, totalCount = 0 });
    }
});

// EXPORT ENDPOINT
app.MapGet("/api/event-log/export", async (AppDbContext db, int userId) =>
{
    try
    {
        var logs = await (from e in db.EventLogs
                          join u in db.AxUsers on e.UserId equals u.Id into users
                          from u in users.DefaultIfEmpty()
                          select new {
                              e.EventLogId,
                              TypeName = e.EventTypeId == 2 ? "Information" : e.EventTypeId == 3 ? "Warning" : "Error",
                              e.InputDt,
                              e.Description,
                              UserFullName = u != null ? (u.FirstName + " " + u.LastName).Trim() : ($"User {e.UserId}"),
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

// STORES ENDPOINT
app.MapGet("/api/stores", async (AppDbContext db, int? userId) =>
{
    try
    {
        var items = await db.Stores
            .OrderBy(s => s.StoreId)
            .Select(s => new {
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


// UPDATE STORE ENDPOINT
app.MapPut("/api/stores/{id}", async (AppDbContext db, int id, StoreUpdateDto dto, int? userId) =>
{
    try
    {
        var store = await db.Stores.FirstOrDefaultAsync(s => s.StoreId == id);
        if (store == null)
        {
            return Results.NotFound(new { message = "Store not found." });
        }

        // Basic field normalization
        var name    = dto.Name?.Trim();
        var address = dto.Address?.Trim();
        var phone   = dto.Phone_Number?.Trim();
        var email   = dto.Email?.Trim();
        var hours   = dto.Working_Hours?.Trim();

        if (name    is not null) store.Name          = name;
        if (address is not null) store.Address       = address;
        if (phone   is not null) store.Phone_Number  = phone;
        if (email   is not null) store.Email         = email;
        if (hours   is not null) store.Working_Hours = hours;

        // Track last modifier (ax_user.id), default to system (2) when missing
        store.Last_Modified_User_Id = (userId ?? 2);

        await db.SaveChangesAsync();

        await LoggerHelper.LogEventAsync(
            db,
            EventType.Information,
            "Store details updated",
            $"StoreId={store.StoreId}, Name={store.Name}",
            userId ?? 2
        );

        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        await LoggerHelper.LogEventAsync(
            db,
            EventType.Error,
            "Update store failed",
            ex.ToString(),
            userId ?? 2
        );

        return Results.StatusCode(500);
    }
});

// Log application lifecycle events to aid debugging when the host shuts down unexpectedly
var lifetime = app.Lifetime;
lifetime.ApplicationStarted.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStarted callback"));
lifetime.ApplicationStopping.Register(() => Console.WriteLine(">>> LIFECYCLE: ApplicationStopping callback"));

app.Run();