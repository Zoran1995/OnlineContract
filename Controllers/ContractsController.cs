using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Infrastructure;
using OnlineContract.Models;
using OnlineContract.Services;
using System.Security.Claims;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/contracts")]
    public class ContractsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        // Typed projection for contract grid rows to avoid dynamic expression issues
        private class ContractPageRow
        {
            public int Id { get; set; }
            public DateTime EntryDate { get; set; }
            public Helpers.ContractState ContractState { get; set; }
            public decimal Amount { get; set; }
            public decimal AmtMatched { get; set; }
            public DateTime? DeliveredDt { get; set; }
            public DateTime? WrittenOffDt { get; set; }
            public DateTime? RejectedDt { get; set; }
            public DateTime? CancelledDt { get; set; }
            public string CustomerFullName { get; set; } = "";
            public string CustomerCode { get; set; } = "";
        }

        public ContractsController(AppDbContext db, IHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetContracts([FromQuery] string? state, [FromQuery] string? name, [FromQuery] string? fromDate, [FromQuery] string? toDate, [FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? sortBy, [FromQuery] string? sortDir)
        {
            try
            {
                var pageIndex = page < 1 ? 1 : page;
                var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

                var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                int.TryParse(roleClaim, out var roleId);
                var userIdClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdClaim, out var currentUserId);
                var isCustomer = roleId == (int)UserRole.Customer;

                // Single-join query to avoid duplicate ax_user joins in SQL
                var baseQuery =
                    from c in _db.Contracts.AsNoTracking()
                    where c.Id > 0 && c.IsActive && !c.IsDeleted && (!isCustomer || ((c.InputUserId ?? 0) == currentUserId))
                    join u0 in _db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
                    from u in ug.DefaultIfEmpty()
                    select new { c, u };

                // Status filter: always by lookup_set_id (never internal contract_state_id)
                if (!string.IsNullOrWhiteSpace(state))
                {
                    int? lookupId = null;
                    if (Enum.TryParse<ContractState>(state, true, out var st))
                    {
                        lookupId = (int)st; // enum values map to lookup_set_id
                    }
                    else
                    {
                        lookupId = await _db.LookupSets.AsNoTracking()
                            .Where(l => l.SetName == "ContractState" && l.Value == state)
                            .Select(l => (int?)l.LookupSetId)
                            .FirstOrDefaultAsync();
                    }
                    if (lookupId.HasValue)
                    {
                        baseQuery = baseQuery.Where(x => (int)x.c.ContractState == lookupId.Value);
                    }
                }

                // Name search (first+last or code), single join reused for both filter and select
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var n = name.Trim().ToLower();
                    baseQuery = baseQuery.Where(x => ((((x.u!.FirstName ?? "") + " " + (x.u!.LastName ?? "")).Trim().ToLower().Contains(n))
                                                 || ((x.u!.Code ?? "").ToLower().Contains(n))));
                }

                // Date filters on creation (input_dt aka EntryDate)
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fd))
                {
                    baseQuery = baseQuery.Where(x => x.c.EntryDate >= fd);
                }
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var td))
                {
                    var tdEnd = td.Date.AddDays(1).AddTicks(-1);
                    baseQuery = baseQuery.Where(x => x.c.EntryDate <= tdEnd);
                }

                var totalCount = await baseQuery.CountAsync();

                // Sorting
                var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));

                List<ContractPageRow> pageRows;
                if (sortSpec != null && string.Equals(sortSpec.By, "customerFullName", StringComparison.OrdinalIgnoreCase))
                {
                    var qWithName = baseQuery.Select(x => new { x.c, x.u, name = (((x.u!.FirstName ?? "") + " " + (x.u!.LastName ?? "")).Trim()) });
                    var orderedName = sortSpec.Desc
                        ? qWithName.OrderByDescending(y => y.name).ThenBy(y => y.c.Id)
                        : qWithName.OrderBy(y => y.name).ThenBy(y => y.c.Id);
                    pageRows = await orderedName
                        .Skip(Math.Max(0, (pageIndex - 1) * size))
                        .Take(size)
                        .Select(y => new ContractPageRow
                        {
                            Id = y.c.Id,
                            EntryDate = y.c.EntryDate,
                            ContractState = y.c.ContractState,
                            Amount = y.c.Amount,
                            AmtMatched = y.c.AmtMatched,
                            DeliveredDt = y.c.DeliveredDt,
                            WrittenOffDt = y.c.WrittenOffDt,
                            RejectedDt = y.c.RejectedDt,
                            CancelledDt = y.c.CancelledDt,
                            CustomerFullName = y.u == null ? "" : y.name,
                            CustomerCode = y.u == null ? "" : (y.u.Code ?? "")
                        })
                        .ToListAsync();
                }
                else
                {
                    var orderedOther = baseQuery.AsQueryable();
                    if (sortSpec == null)
                    {
                        orderedOther = baseQuery.OrderByDescending(x => x.c.EntryDate).ThenBy(x => x.c.Id);
                    }
                    else if (string.Equals(sortSpec.By, "id", StringComparison.OrdinalIgnoreCase))
                    {
                        orderedOther = sortSpec.Desc ? baseQuery.OrderByDescending(x => x.c.Id) : baseQuery.OrderBy(x => x.c.Id);
                    }
                    else if (string.Equals(sortSpec.By, "entryDate", StringComparison.OrdinalIgnoreCase))
                    {
                        orderedOther = sortSpec.Desc ? baseQuery.OrderByDescending(x => x.c.EntryDate) : baseQuery.OrderBy(x => x.c.EntryDate);
                    }
                    else if (string.Equals(sortSpec.By, "amount", StringComparison.OrdinalIgnoreCase))
                    {
                        orderedOther = sortSpec.Desc ? baseQuery.OrderByDescending(x => x.c.Amount) : baseQuery.OrderBy(x => x.c.Amount);
                    }
                    else if (string.Equals(sortSpec.By, "contractState", StringComparison.OrdinalIgnoreCase))
                    {
                        orderedOther = sortSpec.Desc ? baseQuery.OrderByDescending(x => x.c.ContractState) : baseQuery.OrderBy(x => x.c.ContractState);
                    }
                    else
                    {
                        orderedOther = baseQuery.OrderByDescending(x => x.c.EntryDate).ThenBy(x => x.c.Id);
                    }

                    pageRows = await orderedOther
                        .Skip(Math.Max(0, (pageIndex - 1) * size))
                        .Take(size)
                        .Select(x => new ContractPageRow
                        {
                            Id = x.c.Id,
                            EntryDate = x.c.EntryDate,
                            ContractState = x.c.ContractState,
                            Amount = x.c.Amount,
                            AmtMatched = x.c.AmtMatched,
                            DeliveredDt = x.c.DeliveredDt,
                            WrittenOffDt = x.c.WrittenOffDt,
                            RejectedDt = x.c.RejectedDt,
                            CancelledDt = x.c.CancelledDt,
                            CustomerFullName = x.u == null ? "" : (((x.u.FirstName ?? "") + " " + (x.u.LastName ?? "")).Trim()),
                            CustomerCode = x.u == null ? "" : (x.u.Code ?? "")
                        })
                        .ToListAsync();
                }

                static string FmtDt(DateTime? d)
                {
                    if (!d.HasValue) return "";
                    var v = d.Value;
                    if (v.Year == 1900 && v.Month == 1 && v.Day == 1) return "";
                    return v.ToString("yyyy-MM-dd HH:mm:ss");
                }

                var items = new List<object>();
                foreach (var x in pageRows)
                {
                    // Resolve ContractState from lookup_set; fallback to 'Unknown' if not found
                    string stateText = "Unknown";
                    try
                    {
                        stateText = await _db.LookupSets.AsNoTracking()
                            .Where(l => l.SetName == "ContractState" && l.LookupSetId == (int)x.ContractState)
                            .Select(l => l.Value)
                            .FirstOrDefaultAsync() ?? "Unknown";
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to resolve contract state lookup for value '{x.ContractState}': {ex}");
                        stateText = "Unknown";
                    }

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

                return JsonResultHelper.StableJson(_env, new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size), sortBy = sortBy ?? "", sortDir = sortDir ?? "" });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Contracts fetch failed", ex.ToString(), 2);
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
            }
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetContract(int id)
        {
            if (id <= 0) return JsonResultHelper.StableJson(_env, new { message = "Contract not found. Please verify the contract ID and try again." }, StatusCodes.Status404NotFound);

            var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
            int.TryParse(roleClaim, out var roleId);
            var userIdClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out var currentUserId);
            var isCustomer = roleId == (int)UserRole.Customer;

            var contractEntity = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            if (contractEntity == null) return JsonResultHelper.StableJson(_env, new { message = "Contract not found. The contract may have been removed." }, StatusCodes.Status404NotFound);
            if (isCustomer && (contractEntity.InputUserId ?? 0) != currentUserId) return StatusCode(StatusCodes.Status403Forbidden);

            var row = await (
                from c in _db.Contracts.AsNoTracking()
                join u0 in _db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
                from u in ug.DefaultIfEmpty()
                join u1 in _db.AxUsers.AsNoTracking() on c.LastModifiedById equals (int?)u1.Id into ug1
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
                    CustomerFullName = u == null ? "" : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                    InputUserCode = u == null ? "" : (u.Code ?? ""),
                    LastModifiedByCode = lm == null ? "" : (lm.Code ?? "")
                }
            ).FirstOrDefaultAsync();

            if (row == null)
                return JsonResultHelper.StableJson(_env, new { message = "Contract not found. The contract may have been removed." }, StatusCodes.Status404NotFound);

            // Resolve from lookup_set; fallback to 'Unknown' if not found
            string contractStateText = "Unknown";
            try
            {
                contractStateText = await _db.LookupSets.AsNoTracking()
                    .Where(l => l.SetName == "ContractState" && l.LookupSetId == (int)row.ContractState)
                    .Select(l => l.Value)
                    .FirstOrDefaultAsync() ?? "Unknown";
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"Failed to get lookup value for contract state '{row.ContractState}': {ex}");
                contractStateText = "Unknown";
            }

            return JsonResultHelper.StableJson(_env, new
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
        }

        [HttpGet("{id:int}/items")]
        [Authorize]
        public async Task<IActionResult> GetContractItems(int id, int page, int pageSize)
        {
            if (id <= 0) return JsonResultHelper.StableJson(_env, new { message = "Contract not found. Please verify the contract ID and try again." }, StatusCodes.Status404NotFound);

            var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
            int.TryParse(roleClaim, out var roleId);
            var userIdClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out var currentUserId);
            var isCustomer = roleId == (int)UserRole.Customer;

            var contractEntity = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            if (contractEntity == null) return JsonResultHelper.StableJson(_env, new { message = "Contract not found. The contract may have been removed." }, StatusCodes.Status404NotFound);
            if (isCustomer && (contractEntity.InputUserId ?? 0) != currentUserId) return StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var pageIndex = page < 1 ? 1 : page;
                var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

                var baseQ = _db.ContractDets.AsNoTracking().Where(d => d.ContractId == id && !d.IsDeleted);
                var totalCount = await baseQ.CountAsync();

                var rows = await (
                    from d in baseQ
                    join v in _db.ProductVariants.AsNoTracking() on d.ProductVariantId equals v.Id
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
                    try 
                    { 
                        stateText = await LookupHelper.GetLookupValueAsync(_db, (int)r.ItemStateId); 
                    } 
                    catch (Exception ex) 
                    { 
                        // Fallback to numeric value if lookup fails
                        System.Diagnostics.Debug.WriteLine($"Failed to get lookup value for ItemStateId {r.ItemStateId}: {ex}");
                    }
                    string? photoFileName = null;
                    try
                    {
                        photoFileName = await _db.ProductVariants.AsNoTracking()
                            .Where(v => v.Id == r.ProductVariantId)
                            .Select(v => v.PhotoFileName)
                            .FirstOrDefaultAsync();
                    }
                    catch (Exception ex)
                    {
                        // Photo lookup is non-critical; log and continue
                        System.Diagnostics.Debug.WriteLine($"Failed to get photo for variant {r.ProductVariantId}: {ex}");
                    }
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

                return JsonResultHelper.StableJson(_env, new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size) });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Contract items fetch failed", ex.ToString(), 2);
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
            }
        }

        [HttpGet("{id:int}/state/modal-data")]
        [Authorize]
        public async Task<IActionResult> GetStateModalData(int id)
        {
            if (id <= 0) return JsonResultHelper.StableJson(_env, new { message = "Contract not found." }, StatusCodes.Status404NotFound);
            var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
            int.TryParse(roleClaim, out var roleId);
            var userIdClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out var currentUserId);
            var isCustomer = roleId == (int)UserRole.Customer;

            var c = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (c == null) return JsonResultHelper.StableJson(_env, new { message = "Contract not found." }, StatusCodes.Status404NotFound);
            if (isCustomer && (c.InputUserId ?? 0) != currentUserId) return StatusCode(StatusCodes.Status403Forbidden);

            string currentName = c.ContractState.ToString();
            try 
            { 
                currentName = await LookupHelper.GetLookupValueAsync(_db, (int)c.ContractState); 
            } 
            catch (Exception ex) 
            { 
                // Fallback to enum value if lookup fails
                System.Diagnostics.Debug.WriteLine($"Failed to get lookup value for ContractState {c.ContractState}: {ex}");
            }

            var list = new List<object>();
            var provider = _db.Database.ProviderName ?? string.Empty;
            if (provider.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                foreach (var val in Enum.GetValues(typeof(ContractState)).Cast<ContractState>().Where(v => v != c.ContractState))
                {
                    string name = val.ToString();
                    try 
                    { 
                        name = await LookupHelper.GetLookupValueAsync(_db, (int)val); 
                    } 
                    catch (Exception ex) 
                    { 
                        // Fallback to enum name if lookup fails
                        System.Diagnostics.Debug.WriteLine($"Failed to get lookup value for ContractState {val}: {ex}");
                    }
                    list.Add(new { id = (int)val, name });
                }
            }
            else
            {
                try
                {
                    var cnn = _db.Database.GetDbConnection();
                    await cnn.OpenAsync();
                    using var cmd = cnn.CreateCommand();
                    cmd.CommandText = "SELECT next_state_id, next_state_name FROM dbo.ufn_contract_next_state_names(@p0)";
                    var p = cmd.CreateParameter(); p.ParameterName = "@p0"; p.Value = (int)c.ContractState; cmd.Parameters.Add(p);
                    using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        var nid = rdr.GetInt32(0);
                        var nname = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                        list.Add(new { id = nid, name = nname });
                    }
                }
                catch (Exception ex)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Error, "Next states fetch failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                }
            }

            return JsonResultHelper.StableJson(_env, new { currentStateName = currentName, nextStates = list });
        }

        [HttpPost("{id:int}/state")]
        [Authorize]
        public async Task<IActionResult> SetState(int id, [FromServices] WriteOffApprovalTaskService writeOffSvc)
        {
            if (id <= 0) return StatusCode(StatusCodes.Status404NotFound);
            try
            {
                var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                int.TryParse(roleClaim, out var roleId);
                if (!(roleId == 6 || roleId == 7 || roleId == 8)) return StatusCode(StatusCodes.Status403Forbidden);

                var body = await Request.ReadFromJsonAsync<Dictionary<string, object?>>();
                if (body == null) return StatusCode(StatusCodes.Status400BadRequest);
                int nextStateId = 0; string comment = string.Empty;
                try
                {
                    if (body.TryGetValue("nextStateId", out var rawId))
                    {
                        if (rawId is System.Text.Json.JsonElement jel)
                        {
                            if (jel.ValueKind == System.Text.Json.JsonValueKind.Number) nextStateId = jel.GetInt32();
                            else if (jel.ValueKind == System.Text.Json.JsonValueKind.String) int.TryParse(jel.GetString(), out nextStateId);
                        }
                        else if (rawId is int i) nextStateId = i;
                        else if (rawId is string s) int.TryParse(s, out nextStateId);
                    }
                    if (body.TryGetValue("comment", out var rawComment))
                    {
                    if (rawComment is System.Text.Json.JsonElement jec)
                    {
                        comment = jec.ValueKind == System.Text.Json.JsonValueKind.String ? (jec.GetString() ?? string.Empty) : jec.ToString();
                    }
                    else
                    {
                        comment = rawComment?.ToString() ?? string.Empty;
                    }
                    comment = comment.Trim();
                    }
                }
                catch (System.Exception ex)
                {
                    await LoggerHelper.LogEventAsync(
                        _db,
                        EventType.Error,
                        "Failed to parse next state/change request body",
                        ex.ToString(),
                        Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                    return JsonResultHelper.StableJson(_env, new { message = "Invalid request payload." }, StatusCodes.Status400BadRequest);
                }
                if (nextStateId <= 0) return JsonResultHelper.StableJson(_env, new { message = "Next state is required." }, StatusCodes.Status400BadRequest);

                var contract = await _db.Contracts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (contract == null) return StatusCode(StatusCodes.Status404NotFound);
                if (contract.ContractState == ContractState.Draft)
                {
                    return JsonResultHelper.StableJson(_env, new { message = "State change is not allowed for Draft contracts." }, StatusCodes.Status400BadRequest);
                }

                var provider2 = _db.Database.ProviderName ?? string.Empty;
                var isSqlite = provider2.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isSqlite)
                {
                    var allowed = new HashSet<int>();
                    try
                    {
                        var cnn = _db.Database.GetDbConnection();
                        await cnn.OpenAsync();
                        using var cmd = cnn.CreateCommand();
                        cmd.CommandText = "SELECT next_state_id FROM dbo.ufn_contract_next_state_names(@p0)";
                        var p = cmd.CreateParameter(); p.ParameterName = "@p0"; p.Value = (int)contract.ContractState; cmd.Parameters.Add(p);
                        using var rdr = await cmd.ExecuteReaderAsync();
                        while (await rdr.ReadAsync()) allowed.Add(rdr.GetInt32(0));
                    }
                    catch (Exception ex)
                    {
                        await LoggerHelper.LogEventAsync(_db, EventType.Error, "Validate next states failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                    }
                    if (!allowed.Contains(nextStateId))
                    {
                        return JsonResultHelper.StableJson(_env, new { message = "The requested next state is not permitted for the current state." }, StatusCodes.Status400BadRequest);
                    }
                }

                string currentName = contract.ContractState.ToString();
                string nextName = nextStateId.ToString();
                try { currentName = await LookupHelper.GetLookupValueAsync(_db, (int)contract.ContractState); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to resolve current state lookup: {ex}"); }
                try { nextName = await LookupHelper.GetLookupValueAsync(_db, nextStateId); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to resolve next state lookup: {ex}"); }

                await using var tx = await _db.Database.BeginTransactionAsync();
                bool wroteOffImmediately = true;
                if ((ContractState)nextStateId == ContractState.WrittenOff)
                {
                    // Check for active approval rule for write-off; if present, do NOT change state now
                    var amount = await _db.Contracts.AsNoTracking().Where(c => c.Id == id).Select(c => (decimal?)c.Amount).FirstOrDefaultAsync() ?? 0m;
                    var contextId = (int)ApprovalRuleContext.ContractWriteOff;
                    var rules = await _db.ApprovalRules.AsNoTracking()
                        .Where(r => r.ApprovalRuleContextId == contextId)
                        .OrderByDescending(r => r.Id)
                        .ToListAsync();
                    var latest = rules.FirstOrDefault();
                    var hasActiveRule = latest != null && latest.IsActive && !latest.IsDeleted && amount >= latest.AmtThreshold;
                    if (hasActiveRule)
                    {
                        wroteOffImmediately = false;
                        try
                        {
                            var currentUserId2 = Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext);
                            await writeOffSvc.CreateWriteOffApprovalTaskIfNeededAsync(id, currentUserId2, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            await LoggerHelper.LogEventAsync(_db, EventType.Error, "Write-off approval task creation failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                        }
                    }
                }

                if (wroteOffImmediately)
                {
                    contract.ContractState = (ContractState)nextStateId;
                    contract.LastModifiedById = Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext);
                    contract.LastUpdatedDt = DateTime.Now;
                    try
                    {
                        var now = DateTime.Now;
                        switch ((ContractState)nextStateId)
                        {
                            case ContractState.Cancelled: contract.CancelledDt = now; break;
                            case ContractState.Delivered: contract.DeliveredDt = now; break;
                            case ContractState.WrittenOff: contract.WrittenOffDt = now; break;
                            case ContractState.Rejected: contract.RejectedDt = now; break;
                        }
                    }
                    catch (Exception ex)
                    {
                        await LoggerHelper.LogEventAsync(
                            _db,
                            EventType.Error,
                            "Contract state date update failed",
                            ex.ToString(),
                            Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                    }
                    contract.Stamp = contract.Stamp + 1;
                    await _db.SaveChangesAsync();
                }

                var note = new Models.Note
                {
                    ProductId = null,
                    ContractId = id,
                    Comment = comment ?? string.Empty,
                    Subject = wroteOffImmediately
                        ? $"Changed Contract state from {currentName} to {nextName}"
                        : $"Requested Write-Off; approval task created. State remains {currentName}",
                    IsMain = false,
                    IsDeleted = false,
                    IsActive = true,
                    InputDt = DateTime.Now,
                    InputUserId = 2,
                    LastModifiedById = 2,
                    LastUpdatedDt = DateTime.Now,
                    Stamp = 0
                };
                _db.Notes.Add(note);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                if (wroteOffImmediately)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Information, "Contract state changed", $"ContractId={id}; From={currentName}; To={nextName}", Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                    return JsonResultHelper.StableJson(_env, new { success = true, newStateName = nextName, message = "Contract state updated successfully." });
                }
                else
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Information, "Write-off pending approval", $"ContractId={id}; Current={currentName}", Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                    return JsonResultHelper.StableJson(_env, new { success = true, newStateName = currentName, message = "Write-off approval task created. Contract remains in current state until approved." });
                }
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Contract state change failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("{id:int}/items/{itemId:int}/delete")]
        [Authorize]
        public async Task<IActionResult> DeleteItem(int id, int itemId)
        {
            if (id <= 0 || itemId <= 0) return StatusCode(StatusCodes.Status404NotFound);
            try
            {
                var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                int.TryParse(roleClaim, out var roleId);
                var userIdClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdClaim, out var currentUserId);
                var isCustomer = roleId == (int)UserRole.Customer;
                if (!isCustomer) return StatusCode(StatusCodes.Status403Forbidden);

                var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
                if (contract == null) return StatusCode(StatusCodes.Status404NotFound);
                if ((contract.InputUserId ?? 0) != currentUserId) return StatusCode(StatusCodes.Status403Forbidden);
                if (contract.ContractState != ContractState.Draft) return StatusCode(StatusCodes.Status409Conflict);

                await using var tx = await _db.Database.BeginTransactionAsync();

                var det = await _db.ContractDets.FirstOrDefaultAsync(d => d.Id == itemId && d.ContractId == id && !d.IsDeleted);
                if (det == null) { await tx.RollbackAsync(); return StatusCode(StatusCodes.Status404NotFound); }

                det.IsDeleted = true;
                det.IsActive = false;
                det.LastModifiedById = currentUserId;
                det.LastUpdatedDt = DateTime.Now;
                det.Stamp = det.Stamp + 1;
                await _db.SaveChangesAsync();

                var newAmount = await _db.ContractDets.AsNoTracking()
                    .Where(d => d.ContractId == id && d.IsActive && !d.IsDeleted)
                    .Select(d => (decimal?)d.AmtGross)
                    .SumAsync() ?? 0m;
                contract.Amount = newAmount;
                contract.LastModifiedById = currentUserId;
                contract.LastUpdatedDt = DateTime.Now;
                contract.Stamp = contract.Stamp + 1;
                await _db.SaveChangesAsync();

                var remainingActive = await _db.ContractDets.AsNoTracking()
                    .CountAsync(d => d.ContractId == id && d.IsActive && !d.IsDeleted);
                if (remainingActive == 0)
                {
                    contract.IsDeleted = true;
                    contract.IsActive = false;
                    contract.LastModifiedById = currentUserId;
                    contract.LastUpdatedDt = DateTime.Now;
                    contract.Stamp = contract.Stamp + 1;
                    await _db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Contract item deleted", $"ContractId={id}; ItemId={itemId}", currentUserId);
                return JsonResultHelper.StableJson(_env, new { success = true, message = "The item was deleted successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Contract item delete failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("{id:int}/save")]
        [Authorize]
        public async Task<IActionResult> SaveContract(int id)
        {
            if (id <= 0) return StatusCode(StatusCodes.Status404NotFound);
            try
            {
                var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                int.TryParse(roleClaim, out var roleId);
                var userIdClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdClaim, out var currentUserId);
                var isCustomer = roleId == (int)UserRole.Customer;
                if (!isCustomer) return StatusCode(StatusCodes.Status403Forbidden);

                var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
                if (contract == null) return StatusCode(StatusCodes.Status404NotFound);
                if ((contract.InputUserId ?? 0) != currentUserId) return StatusCode(StatusCodes.Status403Forbidden);
                if (contract.ContractState != ContractState.Draft) return StatusCode(StatusCodes.Status409Conflict);

                try
                {
                    var body = await Request.ReadFromJsonAsync<Dictionary<string, int?>>();
                    if (body != null && body.TryGetValue("contractStamp", out var cs) && cs.HasValue && contract.Stamp != cs.Value)
                        return StatusCode(StatusCodes.Status409Conflict);
                }
                catch (System.Exception ex)
                {
                    await LoggerHelper.LogEventAsync(
                        _db,
                        EventType.Information,
                        "Failed to read contractStamp from request body",
                        $"ContractId={id}; Error={ex.Message}",
                        currentUserId);
                }

                var dets = await _db.ContractDets.Where(d => d.ContractId == id && !d.IsDeleted).ToListAsync();
                foreach (var d in dets)
                {
                    var ok = await new VariantAvailabilityService(_db).CheckVariantAvailabilityAsync(d.ProductVariantId, d.Quantity);
                    if (!ok)
                    {
                        var available = await _db.ProductInventories.AsNoTracking()
                            .Where(i => i.ProductVariantId == d.ProductVariantId && i.IsActive && !i.IsDeleted)
                            .Select(i => (int?)i.QtyOnHand).SumAsync() ?? 0;
                        return JsonResultHelper.StableJson(_env, new { success = false, message = $"Insufficient stock: requested {d.Quantity}, available {available}." });
                    }
                }

                var newAmount = await _db.ContractDets.AsNoTracking()
                    .Where(d => d.ContractId == id && d.IsActive && !d.IsDeleted)
                    .Select(d => (decimal?)d.AmtGross)
                    .SumAsync() ?? 0m;
                contract.Amount = newAmount;

                contract.LastModifiedById = currentUserId;
                contract.LastUpdatedDt = DateTime.Now;
                contract.Stamp = contract.Stamp + 1;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Contract saved", $"ContractId={id}", currentUserId);
                return JsonResultHelper.StableJson(_env, new { success = true, message = "Your changes have been saved successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Contract save failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("{id:int}/items/{itemId:int}/edit")]
        [Authorize]
        public async Task<IActionResult> EditItem(int id, int itemId, [FromServices] VariantAvailabilityService stockSvc)
        {
            if (id <= 0 || itemId <= 0) return StatusCode(StatusCodes.Status404NotFound);
            try
            {
                var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                int.TryParse(roleClaim, out var roleId);
                var userIdClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdClaim, out var currentUserId);
                var isCustomer = roleId == (int)UserRole.Customer;
                if (!isCustomer) return StatusCode(StatusCodes.Status403Forbidden);

                var dto = await Request.ReadFromJsonAsync<Dtos.ContractItemUpdateDto>();
                if (dto == null) return StatusCode(StatusCodes.Status400BadRequest);
                if (dto.ItemId != 0 && dto.ItemId != itemId) return StatusCode(StatusCodes.Status400BadRequest);
                if (dto.Quantity <= 0) return JsonResultHelper.StableJson(_env, new { success = false, message = "Quantity must be greater than 0." });

                var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
                if (contract == null) return StatusCode(StatusCodes.Status404NotFound);
                if ((contract.InputUserId ?? 0) != currentUserId) return StatusCode(StatusCodes.Status403Forbidden);
                if (contract.ContractState != ContractState.Draft) return StatusCode(StatusCodes.Status409Conflict);
                if (contract.Stamp != dto.ContractStamp) return StatusCode(StatusCodes.Status409Conflict);

                var det = await _db.ContractDets.FirstOrDefaultAsync(d => d.Id == itemId && d.ContractId == id && !d.IsDeleted);
                if (det == null) return StatusCode(StatusCodes.Status404NotFound);
                if (det.Stamp != dto.ItemStamp) return StatusCode(StatusCodes.Status409Conflict);

                var existingVariant = await _db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == det.ProductVariantId);
                if (existingVariant == null) return StatusCode(StatusCodes.Status404NotFound);
                var productId = existingVariant.ProductId;

                var vr = await stockSvc.CheckVariantAvailabilityAsync(productId, dto.Color ?? string.Empty, dto.Size ?? string.Empty, dto.Quantity, dto.StoreId, HttpContext.RequestAborted);
                if (vr.FoundVariantId <= 0)
                {
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Selected color/size variant is not available." });
                }
                if (!vr.IsAvailable)
                {
                    var err = vr.Errors.FirstOrDefault() ?? $"Insufficient stock: requested {dto.Quantity}, available {vr.AvailableQty}.";
                    return JsonResultHelper.StableJson(_env, new { success = false, message = err });
                }

                det.ProductVariantId = vr.FoundVariantId;
                det.Quantity = dto.Quantity;
                det.Size = (dto.Size ?? string.Empty).Trim();
                det.Color = (dto.Color ?? string.Empty).Trim();
                det.LastModifiedById = currentUserId;
                det.LastUpdatedDt = DateTime.Now;
                det.Stamp = det.Stamp + 1;
                await _db.SaveChangesAsync();

                var newAmount = await _db.ContractDets.AsNoTracking()
                    .Where(d => d.ContractId == id && d.IsActive && !d.IsDeleted)
                    .Select(d => (decimal?)d.AmtGross)
                    .SumAsync() ?? 0m;
                contract.Amount = newAmount;
                contract.LastModifiedById = currentUserId;
                contract.LastUpdatedDt = DateTime.Now;
                contract.Stamp = contract.Stamp + 1;

                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Contract item edited", $"ContractId={id}; ItemId={itemId}; VariantId={det.ProductVariantId}", currentUserId);
                return JsonResultHelper.StableJson(_env, new { success = true, message = "Item has been successfully updated.", stamp = det.Stamp, contractStamp = contract.Stamp });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Contract item edit failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("{id:int}/submit")]
        [Authorize]
        public async Task<IActionResult> Submit(int id, [FromQuery] string? method, [FromServices] VariantAvailabilityService stockSvc, [FromServices] IPaymentService paymentSvc, [FromServices] IConfiguration cfg)
        {
            if (id <= 0) return StatusCode(StatusCodes.Status404NotFound);
            try
            {
                var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                int.TryParse(roleClaim, out var roleId);
                var userIdClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdClaim, out var currentUserId);
                var isCustomer = roleId == (int)UserRole.Customer;
                if (!isCustomer) return StatusCode(StatusCodes.Status403Forbidden);

                var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
                if (contract == null) return StatusCode(StatusCodes.Status404NotFound);
                if ((contract.InputUserId ?? 0) != currentUserId) return StatusCode(StatusCodes.Status403Forbidden);
                if (contract.ContractState != ContractState.Draft) return StatusCode(StatusCodes.Status409Conflict);

                var sumAmount = await _db.ContractDets.AsNoTracking()
                    .Where(d => d.ContractId == id && d.IsActive && !d.IsDeleted)
                    .Select(d => (decimal?)d.AmtGross)
                    .SumAsync() ?? 0m;
                contract.Amount = sumAmount;

                var dets = await _db.ContractDets.Where(d => d.ContractId == id && !d.IsDeleted).ToListAsync();
                foreach (var d in dets)
                {
                    var ok = await stockSvc.CheckVariantAvailabilityAsync(d.ProductVariantId, d.Quantity);
                    if (!ok)
                    {
                        var available = await _db.ProductInventories.AsNoTracking()
                            .Where(i => i.ProductVariantId == d.ProductVariantId && i.IsActive && !i.IsDeleted)
                            .Select(i => (int?)i.QtyOnHand).SumAsync() ?? 0;
                        return JsonResultHelper.StableJson(_env, new { success = false, message = $"Insufficient stock: requested {d.Quantity}, available {available}." });
                    }
                }

                var user = await _db.AxUsers.FirstOrDefaultAsync(u => u.Id == currentUserId && !u.IsDeleted);
                if (user == null) return StatusCode(StatusCodes.Status403Forbidden);
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
                    return JsonResultHelper.StableJson(_env, new { success = false, message = msg, errors });
                }

                string? mRaw = method;
                if (string.IsNullOrWhiteSpace(mRaw)) { try { mRaw = HttpContext.Request.Query["method"].ToString(); } catch { mRaw = ""; } }
                var m = (mRaw ?? "").Trim().ToLowerInvariant();
                if (m == "cod")
                {
                    await using var tx = await _db.Database.BeginTransactionAsync();
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
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                    await LoggerHelper.LogEventAsync(_db, EventType.Information, "Contract submitted (COD)", $"ContractId={id}", currentUserId);
                    return JsonResultHelper.StableJson(_env, new { success = true, message = "Your order was submitted successfully. We’ll contact you shortly." });
                }
                else if (m == "online")
                {
                    var amount = await _db.ContractDets.AsNoTracking()
                        .Where(d => d.ContractId == id && d.IsActive && !d.IsDeleted)
                        .Select(d => (decimal?)d.AmtGross)
                        .SumAsync() ?? 0m;
                    contract.Amount = amount;

                    contract.LastModifiedById = currentUserId;
                    contract.LastUpdatedDt = DateTime.Now;
                    contract.Stamp = contract.Stamp + 1;
                    await _db.SaveChangesAsync();

                    var enabled = (cfg["Payments:Enabled"] ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (!enabled)
                    {
                        await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Payments disabled", $"ContractId={id}", currentUserId);
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "Online payments are temporarily unavailable. Please choose Cash on Delivery." });
                    }

                    var firstName = user?.FirstName ?? string.Empty;
                    var lastName = user?.LastName ?? string.Empty;
                    var userFullName = ($"{firstName} {lastName}").Trim();
                    var customerEmail = user?.Email ?? string.Empty;
                    var customerInfo = new CustomerInfo(customerEmail, userFullName);
                    var (ok, redirectUrl, externalOrderId, error) = await paymentSvc.CreatePaymentIntentAsync(id, amount, customerInfo);
                    if (!ok || string.IsNullOrWhiteSpace(redirectUrl))
                    {
                        if (_env.IsEnvironment("Testing"))
                        {
                            externalOrderId = $"C-{id}-TEST";
                            redirectUrl = "/payments/wspay/return/success?ref=" + externalOrderId;
                            var p = new Payment
                            {
                                ContractId = id,
                                Provider = "WSPay",
                                ExternalOrderId = externalOrderId,
                                AmountGross = amount,
                                Currency = cfg["Payments:WSPay:Currency"] ?? "RSD",
                                Status = "Pending",
                                CreatedDt = DateTime.Now,
                                Stamp = 0
                            };
                            _db.Payments.Add(p);
                            await _db.SaveChangesAsync();
                        }
                        else
                        {
                            await LoggerHelper.LogEventAsync(_db, EventType.Error, "Payment intent failed", error ?? "Unknown error", currentUserId);
                            return JsonResultHelper.StableJson(_env, new { success = false, message = "Payment initialization failed. Please try again." });
                        }
                    }
                    await LoggerHelper.LogEventAsync(_db, EventType.Information, "Contract payment initiated (Online)", $"ContractId={id}; ExternalOrderId={externalOrderId}", currentUserId);
                    return JsonResultHelper.StableJson(_env, new { success = true, redirectUrl = redirectUrl, reference = externalOrderId });
                }
                else
                {
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Unknown submit method. Use 'cod' or 'online'." });
                }
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Contract submit failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpGet("export")]
        [Authorize]
        public async Task<IActionResult> Export(string? state, string? name, string? fromDate, string? toDate)
        {
            try
            {
                var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                int.TryParse(roleClaim, out var roleId);
                var userIdClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdClaim, out var currentUserId);
                var isCustomer = roleId == (int)UserRole.Customer;

                var q =
                    from c in _db.Contracts.AsNoTracking()
                    join u0 in _db.AxUsers.AsNoTracking() on c.InputUserId equals (int?)u0.Id into ug
                    from u in ug.DefaultIfEmpty()
                    where c.Id > 0 && (!isCustomer || (c.InputUserId ?? 0) == currentUserId)
                    select new
                    {
                        c.Id,
                        c.EntryDate,
                        c.ContractState,
                        c.Amount,
                        c.AmtMatched,
                        CustomerFullName = u == null ? "" : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                        CustomerCode = u == null ? "" : (u.Code ?? "")
                    };

                if (!string.IsNullOrWhiteSpace(state) && Enum.TryParse<ContractState>(state, true, out var st))
                {
                    q = q.Where(x => x.ContractState == st);
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    var n = name.Trim().ToLower();
                    q = q.Where(x => (x.CustomerFullName ?? "").ToLower().Contains(n) || (x.CustomerCode ?? "").ToLower().Contains(n));
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

                var rows = await q.OrderByDescending(x => x.EntryDate).ThenBy(x => x.Id).Take(50000).ToListAsync();

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
                    sb.Append(CsvEscape(r.Id.ToString())); sb.Append(',');
                    sb.Append(CsvEscape(r.CustomerFullName)); sb.Append(',');
                    sb.Append(CsvEscape(r.Amount.ToString())); sb.Append(',');
                    sb.Append(CsvEscape(r.AmtMatched.ToString())); sb.Append(',');
                    sb.Append(CsvEscape(r.ContractState.ToString())); sb.Append(',');
                    sb.Append(CsvEscape(r.EntryDate.ToString("yyyy-MM-dd HH:mm:ss")));
                    sb.AppendLine();
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                var ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var fname = $"Contract_{ts}.csv";
                return File(bytes, "text/csv; charset=utf-8", fname);
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Contracts export failed", ex.ToString(), 2);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
