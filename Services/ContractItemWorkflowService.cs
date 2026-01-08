using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Dtos;

namespace OnlineContract.Services
{
    public class ContractItemWorkflowService
    {
        private readonly AppDbContext _db;
        public ContractItemWorkflowService(AppDbContext db) { _db = db; }

        public async Task<ChangeStateModalDto> GetModalDataAsync(int contractDetId, CancellationToken ct = default)
        {
            var det = await _db.ContractDets.AsNoTracking()
                .Where(d => d.Id == contractDetId && d.IsActive && !d.IsDeleted)
                .Select(d => new { d.ItemStateId })
                .FirstOrDefaultAsync(ct);
            if (det == null) throw new KeyNotFoundException("Item not found");

            var dto = new ChangeStateModalDto { CurrentStateId = (int)det.ItemStateId };

            if (_db.Database.IsSqlServer())
            {
                // Current state name via fn_get_lookup_value
                await using (var cmd1 = _db.Database.GetDbConnection().CreateCommand())
                {
                    cmd1.CommandText = "SELECT dbo.fn_get_lookup_value(@id);";
                    var p = new SqlParameter("@id", SqlDbType.Int) { Value = (int)det.ItemStateId };
                    cmd1.Parameters.Add(p);
                    await _db.Database.OpenConnectionAsync(ct);
                    dto.CurrentStateName = (string?)await cmd1.ExecuteScalarAsync(ct) ?? "";
                    await _db.Database.CloseConnectionAsync();
                }

                // is_end_state
                dto.IsEndState = await _db.ContractStates.AsNoTracking()
                    .Where(cs => cs.LookupSetId == (int)det.ItemStateId)
                    .Select(cs => cs.IsEndState)
                    .FirstOrDefaultAsync(ct);

                // next states from ufn_item_next_state_names
                await using (var cmd2 = _db.Database.GetDbConnection().CreateCommand())
                {
                    cmd2.CommandText = "SELECT next_state_id, next_state_name FROM dbo.ufn_item_next_state_names(@from_state_id);";
                    var p = new SqlParameter("@from_state_id", SqlDbType.Int) { Value = (int)det.ItemStateId };
                    cmd2.Parameters.Add(p);
                    await _db.Database.OpenConnectionAsync(ct);
                    await using var rdr = await cmd2.ExecuteReaderAsync(ct);
                    while (await rdr.ReadAsync(ct))
                    {
                        var id = rdr.GetInt32(0);
                        var name = rdr.GetString(1);
                        dto.NextStates.Add(new NextStateDto { Id = id, Name = name });
                    }
                    await _db.Database.CloseConnectionAsync();
                }
            }
            else
            {
                // SQLite Testing fallback: name via enum ToString, isEnd from table; NextStates empty
                dto.CurrentStateName = det.ItemStateId.ToString();
                dto.IsEndState = await _db.ContractStates.AsNoTracking()
                    .Where(cs => cs.LookupSetId == (int)det.ItemStateId)
                    .Select(cs => cs.IsEndState)
                    .FirstOrDefaultAsync(ct);
            }

            return dto;
        }

        public async Task<(int newId, string newName)> SetStateAsync(int contractDetId, int nextStateId, int userId, CancellationToken ct = default)
        {
            var det = await _db.ContractDets
                .FirstOrDefaultAsync(d => d.Id == contractDetId && d.IsActive && !d.IsDeleted, ct);
            if (det == null) throw new KeyNotFoundException("Item not found");

            var currentId = (int)det.ItemStateId;

            // Validate allowed transition
            bool allowed = false;
            if (_db.Database.IsSqlServer())
            {
                await using var cmd = _db.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = "SELECT 1 FROM dbo.ufn_item_next_state_names(@from_state_id) WHERE next_state_id = @to_state_id;";
                cmd.Parameters.Add(new SqlParameter("@from_state_id", SqlDbType.Int) { Value = currentId });
                cmd.Parameters.Add(new SqlParameter("@to_state_id", SqlDbType.Int) { Value = nextStateId });
                await _db.Database.OpenConnectionAsync(ct);
                allowed = (await cmd.ExecuteScalarAsync(ct)) != null;
                await _db.Database.CloseConnectionAsync();
            }
            else
            {
                // SQLite Testing: not allowed unless seeded transitions exist (not seeded here) → reject
                allowed = false;
            }

            if (!allowed) throw new InvalidOperationException("Next state not allowed from current state.");

            det.ItemStateId = (OnlineContract.Helpers.ProductStateInOrder)nextStateId;
            det.LastModifiedById = userId;
            det.LastUpdatedDt = DateTime.Now;
            det.Stamp = det.Stamp + 1;
            await _db.SaveChangesAsync(ct);

            string newName = string.Empty;
            if (_db.Database.IsSqlServer())
            {
                await using var cmdName = _db.Database.GetDbConnection().CreateCommand();
                cmdName.CommandText = "SELECT dbo.fn_get_lookup_value(@id);";
                cmdName.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = nextStateId });
                await _db.Database.OpenConnectionAsync(ct);
                newName = (string?)await cmdName.ExecuteScalarAsync(ct) ?? "";
                await _db.Database.CloseConnectionAsync();
            }
            else
            {
                newName = ((OnlineContract.Helpers.ProductStateInOrder)nextStateId).ToString();
            }

            return (nextStateId, newName);
        }
    }
}
