using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineContract.Data;
using OnlineContract.Infrastructure;
using OnlineContract.Services;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/contracts/items")]
    public class ContractItemsWorkflowController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public ContractItemsWorkflowController(AppDbContext db, IHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet("{contractDetId:int}/state/modal-data")]
        [Authorize]
        public async Task<IActionResult> GetModalData(int contractDetId, CancellationToken ct)
        {
            try
            {
                var svc = new ContractItemWorkflowService(_db);
                var dto = await svc.GetModalDataAsync(contractDetId, ct);
                return JsonResultHelper.StableJson(_env, dto);
            }
            catch (KeyNotFoundException)
            {
                return StatusCode(StatusCodes.Status404NotFound);
            }
            catch (Exception ex)
            {
                await Helpers.LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Contract item modal data failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("{contractDetId:int}/state/set")]
        [Authorize]
        public async Task<IActionResult> SetState(int contractDetId, [FromBody] Dtos.SetStateDto bodyDto, CancellationToken ct)
        {
            try
            {
                if (bodyDto == null) return StatusCode(StatusCodes.Status400BadRequest);
                var claimUid = Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext);
                var svc = new ContractItemWorkflowService(_db);
                var (newId, newName) = await svc.SetStateAsync(contractDetId, bodyDto.NextStateId, claimUid, ct);
                return JsonResultHelper.StableJson(_env, new { updated = true, contractDetId, newStateId = newId, newStateName = newName });
            }
            catch (InvalidOperationException)
            {
                return StatusCode(StatusCodes.Status400BadRequest);
            }
            catch (KeyNotFoundException)
            {
                return StatusCode(StatusCodes.Status404NotFound);
            }
            catch (Exception ex)
            {
                await Helpers.LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Contract item set state failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
