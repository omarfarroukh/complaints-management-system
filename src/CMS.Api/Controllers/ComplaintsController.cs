using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ComplaintsController : ControllerBase
    {
        private readonly IComplaintService _complaintService;
        private readonly IComplaintLockService _lockService;
        private readonly ICurrentUserService _currentUserService;

        public ComplaintsController(
            IComplaintService complaintService,
            IComplaintLockService lockService,
            ICurrentUserService currentUserService)
        {
            _complaintService = complaintService;
            _lockService = lockService;
            _currentUserService = currentUserService;
        }

        [HttpPost]
        [Authorize(Roles = "Citizen")]
        public async Task<IActionResult> CreateComplaint([FromBody] CreateComplaintDto request)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var complaint = await _complaintService.CreateComplaintAsync(
                request.Title,
                request.Description,
                request.DepartmentId,
                userId);

            return CreatedAtAction(nameof(GetComplaintById), new { id = complaint.Id }, complaint);
        }

        [HttpGet]
        public async Task<IActionResult> GetComplaints([FromQuery] string? departmentId = null)
        {
            var userId = _currentUserService.UserId;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role)) return Unauthorized();

            var complaints = await _complaintService.GetComplaintsForUserAsync(userId, role, departmentId);
            return Ok(complaints);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetComplaintById(Guid id)
        {
            var complaint = await _complaintService.GetComplaintByIdAsync(id);
            if (complaint == null) return NotFound();

            // Check access rights (simplified)
            var userId = _currentUserService.UserId;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (role == "Citizen" && complaint.CitizenId != userId) return Forbid();
            // Employees/Managers can view (add more strict checks if needed)

            return Ok(complaint);
        }

        [HttpPut("{id}/assign")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> AssignComplaint(Guid id, [FromBody] AssignComplaintDto request)
        {
            var managerId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(managerId)) return Unauthorized();

            // Check Lock
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
        [Authorize(Roles = "Manager,Employee")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateComplaintStatusDto request)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Check Lock
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
        public async Task<IActionResult> UploadAttachment(Guid id, IFormFile file)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            // Ensure directory exists
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "complaints", id.ToString());
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Relative path for storage
            var relativePath = $"/uploads/complaints/{id}/{fileName}";
            await _complaintService.AddAttachmentAsync(id, relativePath, file.FileName, userId);

            return Ok(new { FilePath = relativePath });
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
