using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Domain.Common;
using CMS.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using StackExchange.Redis;
using CMS.Api.Helpers; // Added

namespace CMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [ValidationFilter]
    public class ComplaintsController : ControllerBase
    {
        private readonly IComplaintService _complaintService;
        private readonly IComplaintLockService _lockService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IConnectionMultiplexer _redis;
        private readonly IFileStorageService _fileStorageService;

        public ComplaintsController(
            IComplaintService complaintService,
            IComplaintLockService lockService,
            ICurrentUserService currentUserService,
            IConnectionMultiplexer redis,
            IFileStorageService fileStorageService)
        {
            _complaintService = complaintService;
            _lockService = lockService;
            _currentUserService = currentUserService;
            _redis = redis;
            _fileStorageService = fileStorageService;
        }

        // --- UPDATED HELPER METHOD ---
        private void PrepareComplaintUrl(ComplaintDto complaint)
        {
            if (complaint.Attachments == null) return;

            foreach (var attachment in complaint.Attachments)
            {
                // Use the static helper from CMS.Api.Helpers
                attachment.FilePath = attachment.FilePath.ToAbsoluteUrl(Request) ?? string.Empty;
            }
        }
        // -----------------------------

        [HttpPost]
        [Authorize(Roles = "Citizen")]
        [Transactional]
        [Idempotency]
        [InvalidateCache("complaints")]
        public async Task<IActionResult> CreateComplaint([FromBody] CreateComplaintDto request)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var complaint = await _complaintService.CreateComplaintAsync(request, userId);
            PrepareComplaintUrl(complaint);

            return CreatedAtAction(nameof(GetComplaintById), new { id = complaint.Id }, complaint);
        }

        [HttpGet]
        [Cached(60, "complaints")]
        public async Task<IActionResult> GetComplaints([FromQuery] ComplaintFilterDto filter)
        {
            var userId = _currentUserService.UserId;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role)) return Unauthorized();

            var complaints = await _complaintService.GetComplaintsForUserAsync(userId, role, filter);
            complaints.ForEach(PrepareComplaintUrl);

            return Ok(complaints);
        }

        [HttpGet("{id}")]
        [Cached(60, "complaints")]
        public async Task<IActionResult> GetComplaintById(Guid id)
        {
            var complaint = await _complaintService.GetComplaintByIdAsync(id);
            if (complaint == null) return NotFound();

            var userId = _currentUserService.UserId;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (role == "Citizen" && complaint.CitizenId != userId) return Forbid();

            PrepareComplaintUrl(complaint);

            return Ok(complaint);
        }

        [HttpPut("{id}/assign")]
        [Authorize(Roles = "DepartmentManager")]
        [Transactional]
        [Idempotency]
        [InvalidateCache("complaints")]
        public async Task<IActionResult> AssignComplaint(Guid id, [FromBody] AssignComplaintDto request)
        {
            var managerId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(managerId)) return Unauthorized();

            if (!await _lockService.AcquireLockAsync(id, managerId))
            {
                var holder = await _lockService.GetCurrentLockHolderAsync(id);
                return Conflict($"Complaint is currently locked by user {holder}");
            }

            try
            {
                await _complaintService.AssignComplaintAsync(id, request.EmployeeId, managerId);
                return NoContent();
            }
            finally
            {
                await _lockService.ReleaseLockAsync(id, managerId);
            }
        }

        [HttpPut("{id}/status")]
        [Authorize(Roles = "DepartmentManager,Employee")]
        [Transactional]
        [Idempotency]
        [InvalidateCache("complaints")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateComplaintStatusDto request)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!await _lockService.AcquireLockAsync(id, userId))
            {
                var holder = await _lockService.GetCurrentLockHolderAsync(id);
                return Conflict($"Complaint is currently locked by user {holder}");
            }

            try
            {
                var status = Enum.Parse<ComplaintStatus>(request.Status);
                await _complaintService.UpdateComplaintStatusAsync(id, status, userId);
                return NoContent();
            }
            finally
            {
                await _lockService.ReleaseLockAsync(id, userId);
            }
        }

        [HttpPost("{id}/attachments")]
        [Transactional]
        [InvalidateCache("complaints")]
        public async Task<IActionResult> UploadAttachment(Guid id, IFormFile file)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var relativePath = await _fileStorageService.SaveFileAsync(file.OpenReadStream(), file.FileName, $"complaints/{id}");

            await _complaintService.AddAttachmentAsync(id, relativePath, file.FileName, file.Length, file.ContentType, userId);

            // Use Helper
            return Ok(new { FileUrl = relativePath.ToAbsoluteUrl(Request) });
        }

        [HttpPost("{id}/notes")]
        [Transactional]
        [Idempotency]
        [InvalidateCache("complaints")]
        public async Task<IActionResult> AddNote(Guid id, [FromBody] AddNoteDto request)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (!await _lockService.AcquireLockAsync(id, userId))
            {
                var holder = await _lockService.GetCurrentLockHolderAsync(id);
                return Conflict($"Complaint is currently locked by user {holder}");
            }

            try
            {
                await _complaintService.AddNoteAsync(id, request.Note, userId);
                return Ok(new { Message = "Note added successfully" });
            }
            finally
            {
                await _lockService.ReleaseLockAsync(id, userId);
            }
        }

        [HttpGet("{id}/versions")]
        [Cached(60)]
        public async Task<IActionResult> GetVersions(Guid id)
        {
            var versions = await _complaintService.GetComplaintVersionsAsync(id);
            return Ok(versions);
        }

        [HttpPatch("{id}")]
        [Transactional]
        [Idempotency]
        [InvalidateCache("complaints")]
        public async Task<IActionResult> PatchComplaint(Guid id, [FromBody] PatchComplaintDto request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (userId == null || role == null) return Unauthorized();

            if (!await _lockService.AcquireLockAsync(id, userId))
            {
                var holder = await _lockService.GetCurrentLockHolderAsync(id);
                return Conflict($"Complaint is currently locked by user {holder}");
            }

            try
            {
                var updatedComplaint = await _complaintService.UpdateComplaintDetailsAsync(id, request, userId, role);
                PrepareComplaintUrl(updatedComplaint);
                return Ok(updatedComplaint);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            finally
            {
                await _lockService.ReleaseLockAsync(id, userId);
            }
        }

        [HttpPost("{id}/lock")]
        public async Task<IActionResult> AcquireLock(Guid id)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var success = await _lockService.AcquireLockAsync(id, userId);
            if (!success)
            {
                var holder = await _lockService.GetCurrentLockHolderAsync(id);
                return Conflict(new { Message = "Could not acquire lock", LockedBy = holder });
            }

            return Ok(new { Message = "Lock acquired" });
        }

        [HttpPost("{id}/unlock")]
        public async Task<IActionResult> ReleaseLock(Guid id)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _lockService.ReleaseLockAsync(id, userId);
            return Ok(new { Message = "Lock released" });
        }
    }
}