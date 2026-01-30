using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Dtos;
using OnlineContract.Infrastructure;
using OnlineContract.Models;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/reviews")]
    public class ReviewsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ReviewsController(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Get list of reviews with pagination, average rating, and total count.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var pageIndex = page < 1 ? 1 : page;
                var size = pageSize <= 0 ? 10 : (pageSize > 100 ? 100 : pageSize);

                var query = _db.Reviews
                    .AsNoTracking()
                    .Where(r => !r.IsDeleted && r.IsActive);

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)size);

                // Calculate average rating
                decimal averageRating = 0;
                if (totalCount > 0)
                {
                    averageRating = Math.Round((decimal)await query.AverageAsync(r => r.Mark), 1);
                }

                var items = await (from r in query
                                   join u in _db.AxUsers.AsNoTracking() on r.InputUserId equals u.Id into uj
                                   from u in uj.DefaultIfEmpty()
                                   orderby r.InputDt descending
                                   select new ReviewDto
                                   {
                                       Id = r.Id,
                                       InputDt = r.InputDt,
                                       InputUserId = r.InputUserId,
                                       UserCode = r.InputUserId == 0 ? "Anonymous" : (u != null ? u.Code : "Anonymous"),
                                       Comment = r.Comment,
                                       Mark = r.Mark,
                                       Stamp = r.Stamp
                                   })
                    .Skip((pageIndex - 1) * size)
                    .Take(size)
                    .ToListAsync();

                return Ok(new ReviewListResponse
                {
                    Items = items,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Page = pageIndex,
                    PageSize = size,
                    AverageRating = averageRating,
                    TotalReviews = totalCount
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get summary statistics for reviews (average rating and total count).
        /// </summary>
        [HttpGet("summary")]
        [AllowAnonymous]
        public async Task<IActionResult> Summary()
        {
            try
            {
                var query = _db.Reviews
                    .AsNoTracking()
                    .Where(r => !r.IsDeleted && r.IsActive);

                var totalCount = await query.CountAsync();
                decimal averageRating = 0;
                if (totalCount > 0)
                {
                    averageRating = Math.Round((decimal)await query.AverageAsync(r => r.Mark), 1);
                }

                return Ok(new
                {
                    averageRating,
                    totalReviews = totalCount
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Create a new review. Anonymous users get input_user_id = 0.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Create([FromBody] ReviewCreateDto dto)
        {
            try
            {
                // Validate mark
                if (dto.Mark < 1 || dto.Mark > 5)
                {
                    return BadRequest(new { success = false, message = "Rating must be between 1 and 5." });
                }

                // Get current user ID (0 if not logged in)
                int userId = 0;
                if (User.Identity?.IsAuthenticated == true)
                {
                    userId = UserContextHelper.GetCurrentUserId(HttpContext);
                }

                var review = new Review
                {
                    InputDt = DateTime.UtcNow,
                    InputUserId = userId,
                    Comment = dto.Comment?.Trim(),
                    Mark = dto.Mark,
                    IsActive = true,
                    IsDeleted = false,
                    Stamp = 0
                };

                _db.Reviews.Add(review);
                await _db.SaveChangesAsync();

                // Get user code for response
                string userCode = "Anonymous";
                if (userId > 0)
                {
                    var user = await _db.AxUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                    if (user != null)
                    {
                        userCode = user.Code;
                    }
                }

                return Ok(new
                {
                    success = true,
                    review = new ReviewDto
                    {
                        Id = review.Id,
                        InputDt = review.InputDt,
                        InputUserId = review.InputUserId,
                        UserCode = userCode,
                        Comment = review.Comment,
                        Mark = review.Mark,
                        Stamp = review.Stamp
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Delete a review (soft delete). Only admins or the review owner can delete.
        /// </summary>
        [HttpPost("{id}/delete")]
        [Authorize]
        public async Task<IActionResult> Delete(int id, [FromQuery] int stamp)
        {
            try
            {
                var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == id);
                if (review == null || review.IsDeleted)
                {
                    return NotFound(new { success = false, message = "Review not found." });
                }

                // Check if current user can delete (admin or owner)
                int userId = UserContextHelper.GetCurrentUserId(HttpContext);
                var currentUser = await _db.AxUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                bool isAdmin = currentUser?.RoleId == 8; // Admin role

                if (!isAdmin && review.InputUserId != userId)
                {
                    return StatusCode(403, new { success = false, message = "You can only delete your own reviews." });
                }

                // Optimistic concurrency check
                if (review.Stamp != stamp)
                {
                    return Conflict(new { success = false, message = "Review was modified by another user. Please refresh and try again." });
                }

                review.IsDeleted = true;
                review.IsActive = false;
                review.Stamp++;
                await _db.SaveChangesAsync();

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
