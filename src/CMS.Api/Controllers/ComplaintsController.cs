using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Domain.Common;
using CMS.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using StackExchange.Redis;
using CMS.Api.Helpers;
using CMS.Application.Wrappers;
using Hangfire;

namespace CMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [ValidationFilter]
    [NotifyDashboard("All")]
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

        // Helper to convert relative file paths to absolute URLs
        private void PrepareComplaintUrl(ComplaintDto complaint)
        {
            if (complaint.Attachments == null) return;

            foreach (var attachment in complaint.Attachments)
            {
                attachment.FilePath = attachment.FilePath.ToAbsoluteUrl(Request) ?? string.Empty;
            }
        }

        /// <summary>
        /// Create a new complaint
        /// </summary>
        /// <remarks>
        /// **Authorization:** Citizen role required  
        /// **Idempotent Operation:** Requires X-Idempotency-Key header (UUID format)
        /// 
        /// Creates a new complaint with initial status "Pending". 
        /// 
        /// **Idempotency Behavior:**
        /// - Duplicate requests with the same idempotency key return the cached response (409 Conflict)
        /// - Cache expires after 1440 minutes (24 hours)
        /// - Use a new UUID for each unique complaint submission
        /// 
        /// **Location Information:**
        /// - Address, Latitude, and Longitude are optional but recommended
        /// - Geolocation helps route complaints to the correct department
        /// 
        /// **Next Steps:** Complaint will be reviewed and assigned to an employee by a department manager
        /// </remarks>
        /// <param name="request">Complaint details including title, description, department, and location</param>
        /// <returns>Created complaint with unique ID and initial status</returns>
        /// <response code="201">Complaint created successfully, returns complaint details</response>
        /// <response code="400">Invalid input or validation errors</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="403">Not authorized (requires Citizen role)</response>
        /// <response code="409">Duplicate idempotency key - returns previously created complaint</response>
        [HttpPost]
        [Authorize(Roles = "Citizen")]
        [Transactional]
        [Idempotency]
        [InvalidateCache("complaints", InvalidateOwners = true)]
        [ProducesResponseType(typeof(ComplaintDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ComplaintDto), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CreateComplaint([FromBody] CreateComplaintDto request)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var complaint = await _complaintService.CreateComplaintAsync(request, userId);

            HttpContext.Items["CreatedComplaint"] = complaint;
            PrepareComplaintUrl(complaint);

            // Returns CreatedAtAction (ObjectResult) which enables Reflection in the Attribute
            var response = new ApiResponse<ComplaintDto>(complaint, "Complaint created successfully");

            // Return 201 Created
            return CreatedAtAction(nameof(GetComplaintById), new { id = complaint.Id }, complaint);
        }

        /// <summary>
        /// Get complaints with optional filtering
        /// </summary>
        /// <remarks>
        /// Returns complaints based on the authenticated user's role:
        /// - **Citizen:** Only their own complaints
        /// - **Employee:** Complaints assigned to them
        /// - **DepartmentManager:** All complaints in their department
        /// - **Admin:** All complaints in the system
        /// 
        /// **Filtering Options:**
        /// - Status (Pending, InProgress, Resolved, etc.)
        /// - Priority (Low, Medium, High, Critical)
        /// - Date range
        /// - Department
        /// 
        /// **Caching:** Results cached for 60 seconds
        /// </remarks>
        /// <param name="filter">Optional filters for status, priority, date range, etc.</param>
        /// <returns>List of complaints accessible to the current user</returns>
        /// <response code="200">Complaints retrieved successfully</response>
        /// <response code="401">Not authenticated</response>
        [HttpGet]
        [Cached(60, "complaints")]
        [ProducesResponseType(typeof(PagedResult<ComplaintDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetComplaints([FromQuery] ComplaintFilterDto filter)
        {
            var userId = _currentUserService.UserId;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role)) return Unauthorized();

            // ✅ Enforce safe page size (never allow > 50)
            filter.Take = Math.Min(filter.Take, 50);

            var result = await _complaintService.GetComplaintsForUserAsync(userId, role, filter);
            
            // ✅ Prepare URLs for the current page only
            result.Items.ForEach(PrepareComplaintUrl);

            return Ok(result);
        }

        /// <summary>
        /// Get a specific complaint by ID
        /// </summary>
        /// <remarks>
        /// Returns detailed information about a single complaint including:
        /// - Complaint details (title, description, status, priority)
        /// - Citizen and assigned employee information
        /// - Location data
        /// - All file attachments
        /// - Timestamps (created, assigned, resolved)
        /// 
        /// **Access Control:**
        /// - Citizens can only view their own complaints
        /// - Employees/Managers/Admins can view complaints they have access to
        /// 
        /// **Caching:** Results cached for 60 seconds
        /// </remarks>
        /// <param name="id">Complaint unique identifier</param>
        /// <returns>Complaint details with attachments</returns>
        /// <response code="200">Complaint retrieved successfully</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="403">Not authorized to view this complaint</response>
        /// <response code="404">Complaint not found</response>
        [HttpGet("{id}")]
        [Cached(60, "complaints")]
        [ProducesResponseType(typeof(ComplaintDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
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

        /// <summary>
        /// Assign a complaint to an employee
        /// </summary>
        /// <remarks>
        /// **Authorization:** DepartmentManager role required  
        /// **Idempotent Operation:** Requires X-Idempotency-Key header (UUID format)
        /// 
        /// Assigns a complaint to a specific employee within the department.
        /// 
        /// **Idempotency Behavior:**
        /// - Duplicate requests with the same idempotency key are ignored (409 Conflict)
        /// - Use a new UUID for each unique assignment operation
        /// 
        /// **Optimistic Locking:**
        /// - Acquires lock before assignment to prevent concurrent modifications
        /// - Returns 409 Conflict if complaint is locked by another user
        /// - Lock is automatically released after operation completes
        /// 
        /// **Audit Trail:** Assignment is logged in complaint history
        /// </remarks>
        /// <param name="id">Complaint unique identifier</param>
        /// <param name="request">Employee ID to assign the complaint to</param>
        /// <returns>No content on success</returns>
        /// <response code="204">Complaint assigned successfully</response>
        /// <response code="400">Invalid employee ID or validation errors</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="403">Not authorized (requires DepartmentManager role)</response>
        /// <response code="404">Complaint not found</response>
        /// <response code="409">Complaint locked by another user or duplicate idempotency key</response>
        [HttpPut("{id}/assign")]
        [Authorize(Roles = "DepartmentManager")]
        [Transactional]
        [Idempotency]
        [InvalidateCache("complaints", InvalidateOwners = true)]
        [ProducesResponseType(typeof(ComplaintDto), StatusCodes.Status200OK)] // Changed from 204
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
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

                var updatedComplaint = await _complaintService.GetComplaintByIdAsync(id);
                if (updatedComplaint == null) return NotFound(); // Safety check

                return Ok(updatedComplaint);
            }
            finally
            {
                await _lockService.ReleaseLockAsync(id, managerId);
            }
        }

        /// <summary>
        /// Update complaint status
        /// </summary>
        /// <remarks>
        /// **Authorization:** DepartmentManager or Employee role required  
        /// **Idempotent Operation:** Requires X-Idempotency-Key header (UUID format)
        /// 
        /// Updates the status of a complaint (e.g., from Pending to InProgress, or InProgress to Resolved).
        /// 
        /// **Valid Status Values:**
        /// - Pending - Initial status when created
        /// - InProgress - Employee is actively working on it
        /// - Resolved - Issue has been resolved
        /// - Closed - Complaint is closed (no further action)
        /// - Rejected - Complaint was rejected
        /// 
        /// **Idempotency Behavior:**
        /// - Duplicate status updates with the same idempotency key are ignored
        /// - Use a new UUID for each distinct status change
        /// 
        /// **Optimistic Locking:**
        /// - Acquires lock to prevent concurrent modifications
        /// - Returns 409 if locked by another user
        /// 
        /// **Audit Trail:** Status changes are logged with timestamp and user
        /// </remarks>
        /// <param name="id">Complaint unique identifier</param>
        /// <param name="request">New status value</param>
        /// <returns>No content on success</returns>
        /// <response code="204">Status updated successfully</response>
        /// <response code="400">Invalid status value</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="403">Not authorized (requires DepartmentManager or Employee role)</response>
        /// <response code="404">Complaint not found</response>
        /// <response code="409">Complaint locked by another user or duplicate idempotency key</response>
        [HttpPut("{id}/status")]
        [Authorize(Roles = "DepartmentManager,Employee")]
        [Transactional]
        [Idempotency]
        [InvalidateCache("complaints", InvalidateOwners = true)]
        [ProducesResponseType(typeof(ComplaintDto), StatusCodes.Status200OK)] // Changed from 204
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
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

                // FIX CS0815: Call service, then fetch updated object manually
                await _complaintService.UpdateComplaintStatusAsync(id, status, userId);

                var updatedComplaint = await _complaintService.GetComplaintByIdAsync(id);
                if (updatedComplaint == null) return NotFound();

                return Ok(updatedComplaint);
            }
            finally
            {
                await _lockService.ReleaseLockAsync(id, userId);
            }
        }

        /// <summary>
        /// Upload a file attachment to a complaint
        /// </summary>
        /// <remarks>
        /// Adds a file attachment (image, PDF, document) to an existing complaint.
        /// 
        /// **Supported File Types:**
        /// - Images: JPG, PNG, GIF
        /// - Documents: PDF, DOC, DOCX
        /// - Spreadsheets: XLS, XLSX
        /// 
        /// **File Size Limit:** 10MB per file
        /// 
        /// **Use Cases:**
        /// - Citizens can add photos of the issue
        /// - Employees can attach resolution documentation
        /// - Managers can add approval documents
        /// 
        /// **File Storage:** Files are scanned for malware before being permanently stored
        /// </remarks>
        /// <param name="id">Complaint unique identifier</param>
        /// <param name="file">File to upload (multipart/form-data)</param>
        /// <returns>URL of the uploaded file</returns>
        /// <response code="200">File uploaded successfully, returns file URL</response>
        /// <response code="400">No file provided or file validation errors (size, type)</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="404">Complaint not found</response>
        [HttpPost("{id}/attachments")]
        [Transactional]
        [InvalidateCache("complaints", InvalidateOwners = true)]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UploadAttachment(Guid id, IFormFile file)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            // 1. Save File to Disk
            var relativePath = await _fileStorageService.SaveFileAsync(
                file.OpenReadStream(),
                file.FileName,
                $"complaints/{id}");

            // 2. Save DB Record (IsScanned = false)
            var attachmentId = await _complaintService.AddAttachmentAsync(
                id, relativePath, file.FileName, file.Length, file.ContentType, userId);

            // 3. Enqueue Job (The Fire-and-Forget Magic)
            // We use the Interface here to keep API clean
            BackgroundJob.Schedule<IAttachmentScanningJob>(
                job => job.ExecuteAsync(attachmentId),
                TimeSpan.FromSeconds(5)
            );
            // 4. Return
            var updatedComplaint = await _complaintService.GetComplaintByIdAsync(id);
            if (updatedComplaint != null)
            {
                PrepareComplaintUrl(updatedComplaint);
                return Ok(updatedComplaint);
            }
            return NotFound(new ApiResponse<object>("Complaint not found"));
        }

        /// <summary>
        /// Add a note/comment to a complaint
        /// </summary>
        /// <remarks>
        /// **Idempotent Operation:** Requires X-Idempotency-Key header (UUID format)
        /// 
        /// Adds an internal note or comment to the complaint for communication between staff.
        /// 
        /// **Use Cases:**
        /// - Employees document investigation steps
        /// - Managers provide guidance or instructions
        /// - Staff communicate about resolution progress
        /// 
        /// **Idempotency Behavior:**
        /// - Duplicate notes with the same idempotency key are ignored (409 Conflict)
        /// - Use a new UUID for each distinct note
        /// 
        /// **Optimistic Locking:**
        /// - Acquires lock before adding note
        /// - Prevents concurrent modifications
        /// 
        /// **Audit Trail:** Notes are logged with author ID and timestamp
        /// 
        /// **Visibility:** Notes are visible to all staff (Employees, Managers, Admin) but not to Citizens
        /// </remarks>
        /// <param name="id">Complaint unique identifier</param>
        /// <param name="request">Note text content</param>
        /// <returns>Success confirmation message</returns>
        /// <response code="200">Note added successfully</response>
        /// <response code="400">Invalid or empty note content</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="404">Complaint not found</response>
        /// <response code="409">Complaint locked by another user or duplicate idempotency key</response>
        [HttpPost("{id}/notes")]
        [Transactional]
        [Idempotency]
        // Notes don't change the main list view usually, but we invalidate the specific ID tag
        [InvalidateCache("complaints")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
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
                return Ok(new { message = "Note added successfully" });
            }
            finally
            {
                await _lockService.ReleaseLockAsync(id, userId);
            }
        }

        /// <summary>
        /// Get complaint audit history/versions
        /// </summary>
        /// <remarks>
        /// Returns the complete audit trail of all changes made to a complaint.
        /// 
        /// **Audit Log Includes:**
        /// - Timestamp of each change
        /// - User who made the change
        /// - Summary of what was changed
        /// - Old and new values (when applicable)
        /// - Change type (status update, assignment, note, etc.)
        /// 
        /// **Use Cases:**
        /// - Track complaint lifecycle
        /// - Investigate processing delays
        /// - Compliance and accountability
        /// - Performance analysis
        /// 
        /// **Caching:** Results cached for 60 seconds
        /// </remarks>
        /// <param name="id">Complaint unique identifier</param>
        /// <returns>List of audit log entries ordered by timestamp (newest first)</returns>
        /// <response code="200">Audit history retrieved successfully</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="404">Complaint not found</response>
        [HttpGet("{id}/versions")]
        [Cached(60, "complaints")] // Uses Tag + ID automatically
        [ProducesResponseType(typeof(List<ComplaintAuditLogDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetVersions(Guid id)
        {
            var versions = await _complaintService.GetComplaintVersionsAsync(id);
            return Ok(versions);
        }

        /// <summary>
        /// Partially update complaint details
        /// </summary>
        /// <remarks>
        /// **Idempotent Operation:** Requires X-Idempotency-Key header (UUID format)
        /// 
        /// Allows partial updates to complaint fields without replacing the entire complaint.
        /// 
        /// **Updatable Fields:**
        /// - Priority (Low, Medium, High, Critical)
        /// - Address
        /// - Latitude/Longitude
        /// - Department assignment
        /// 
        /// **Role-Based Updates:**
        /// - Citizens can only update their own pending complaints
        /// - Employees/Managers can update assigned complaints
        /// - Admins can update any complaint
        /// 
        /// **Idempotency Behavior:**
        /// - Duplicate updates with the same idempotency key return the previously updated complaint
        /// - Use a new UUID for each distinct update operation
        /// 
        /// **Optimistic Locking:**
        /// - Acquires lock to prevent concurrent modifications
        /// - Returns 409 if locked by another user
        /// 
        /// **Audit Trail:** All field changes are logged in audit history
        /// </remarks>
        /// <param name="id">Complaint unique identifier</param>
        /// <param name="request">Fields to update (only provided fields will be updated)</param>
        /// <returns>Updated complaint with all fields</returns>
        /// <response code="200">Complaint updated successfully</response>
        /// <response code="400">Invalid field values or business rule violations</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="403">Not authorized to update this complaint</response>
        /// <response code="404">Complaint not found</response>
        /// <response code="409">Complaint locked by another user or duplicate idempotency key</response>
        [HttpPatch("{id}")]
        [Transactional]
        [Idempotency]
        [InvalidateCache("complaints", InvalidateOwners = true)]
        [ProducesResponseType(typeof(ComplaintDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
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
                var updatedComplaint = await _complaintService.UpdateComplaintDetailsAsync(
                    id, request, userId, role);
                PrepareComplaintUrl(updatedComplaint);
                return Ok(updatedComplaint);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ApiResponse<string>(ex.Message));
            }
            catch (KeyNotFoundException)
            {
                return NotFound("Complaint not found");
            }
            finally
            {
                await _lockService.ReleaseLockAsync(id, userId);
            }
        }

        /// <summary>
        /// Acquire an editing lock on a complaint
        /// </summary>
        /// <remarks>
        /// Manually acquires an exclusive lock on a complaint to prevent concurrent modifications.
        /// 
        /// **Lock Mechanism:**
        /// - Only one user can hold a lock on a complaint at a time
        /// - Lock expires after 5 minutes of inactivity
        /// - User must release lock explicitly or wait for expiration
        /// 
        /// **Use Cases:**
        /// - Before starting to edit a complaint in the UI
        /// - Prevent concurrent edits from multiple users
        /// - Ensure data consistency
        /// 
        /// **Best Practice:** Call /unlock when done editing to release the lock immediately
        /// </remarks>
        /// <param name="id">Complaint unique identifier</param>
        /// <returns>Success message if lock acquired</returns>
        /// <response code="200">Lock acquired successfully</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="409">Complaint already locked by another user</response>
        [HttpPost("{id}/lock")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AcquireLock(Guid id)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var success = await _lockService.AcquireLockAsync(id, userId);
            if (!success)
            {
                var holder = await _lockService.GetCurrentLockHolderAsync(id);
                return Conflict(new ApiResponse<string>($"Could not acquire lock. Locked by: {holder}"));
            }

            return Ok(new { message = "Lock acquired" });
        }

        /// <summary>
        /// Release an editing lock on a complaint
        /// </summary>
        /// <remarks>
        /// Releases a previously acquired lock to allow other users to edit the complaint.
        /// 
        /// **When to Call:**
        /// - After finishing edits and saving changes
        /// - When canceling an edit operation
        /// - When navigating away from the edit page
        /// 
        /// **Lock Auto-Release:** Locks automatically expire after 5 minutes if not manually released
        /// 
        /// **Best Practice:** Always release locks explicitly to improve responsiveness for other users
        /// </remarks>
        /// <param name="id">Complaint unique identifier</param>
        /// <returns>Success confirmation message</returns>
        /// <response code="200">Lock released successfully</response>
        /// <response code="401">Not authenticated</response>
        [HttpPost("{id}/unlock")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ReleaseLock(Guid id)
        {
            var userId = _currentUserService.UserId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _lockService.ReleaseLockAsync(id, userId);
            return Ok(new { message = "Lock released" });
        }
    }
}