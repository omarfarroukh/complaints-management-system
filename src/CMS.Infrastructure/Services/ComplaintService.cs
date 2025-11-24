using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Domain.Common;
using CMS.Domain.Entities;
using CMS.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace CMS.Infrastructure.Services
{
    public class ComplaintService : IComplaintService
    {
        private readonly AppDbContext _context;

        public ComplaintService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ComplaintDto> CreateComplaintAsync(CreateComplaintDto dto, string citizenId)
        {
            var complaint = new Complaint
            {
                Title = dto.Title,
                Description = dto.Description,
                DepartmentId = dto.DepartmentId,
                CitizenId = citizenId,
                Status = ComplaintStatus.Submitted, // Default to Submitted
                Priority =  ComplaintPriority.Low,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                Address = dto.Address,
            };

            _context.Complaints.Add(complaint);
            await _context.SaveChangesAsync();

            // Queue background job to notify department (non-blocking)
            BackgroundJob.Enqueue<INotificationJob>(job =>
                job.SendComplaintCreatedNotificationAsync(dto.DepartmentId, dto.Title));

            return MapToDto(complaint);
        }

        public async Task<ComplaintDto?> GetComplaintByIdAsync(Guid id)
        {
            var complaint = await _context.Complaints
                .Include(c => c.Attachments)
                .Include(c => c.AuditLogs)
                .Include(c => c.Citizen)
                .Include(c => c.AssignedEmployee)
                .FirstOrDefaultAsync(c => c.Id == id);

            return complaint != null ? MapToDto(complaint) : null;
        }

        public async Task<List<ComplaintDto>> GetComplaintsForUserAsync(string userId, string role, ComplaintFilterDto filter)
        {
            var query = _context.Complaints.AsQueryable();

            // Role-based base filtering
            if (role == "Citizen")
            {
                query = query.Where(c => c.CitizenId == userId);
            }
            else if (role == "Employee")
            {
                query = query.Where(c => c.AssignedEmployeeId == userId);
            }
            else if (role == "DepartmentManager" && !string.IsNullOrEmpty(filter.DepartmentId))
            {
                query = query.Where(c => c.DepartmentId == filter.DepartmentId);
            }

            // Apply Filters
            if (!string.IsNullOrEmpty(filter.Status) && Enum.TryParse<ComplaintStatus>(filter.Status, out var status))
            {
                query = query.Where(c => c.Status == status);
            }

            if (!string.IsNullOrEmpty(filter.Priority) && Enum.TryParse<ComplaintPriority>(filter.Priority, out var priority))
            {
                query = query.Where(c => c.Priority == priority);
            }

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var term = filter.SearchTerm.ToLower();
                query = query.Where(c => c.Title.ToLower().Contains(term) || c.Description.ToLower().Contains(term));
            }

            if (filter.FromDate.HasValue)
            {
                query = query.Where(c => c.CreatedOn >= filter.FromDate.Value);
            }

            if (filter.ToDate.HasValue)
            {
                query = query.Where(c => c.CreatedOn <= filter.ToDate.Value);
            }

            // Apply Sorting
            if (!string.IsNullOrEmpty(filter.SortBy))
            {
                switch (filter.SortBy.ToLower())
                {
                    case "priority":
                        query = filter.SortDescending ? query.OrderByDescending(c => c.Priority) : query.OrderBy(c => c.Priority);
                        break;
                    case "status":
                        query = filter.SortDescending ? query.OrderByDescending(c => c.Status) : query.OrderBy(c => c.Status);
                        break;
                    case "createdon":
                    default:
                        query = filter.SortDescending ? query.OrderByDescending(c => c.CreatedOn) : query.OrderBy(c => c.CreatedOn);
                        break;
                }
            }
            else
            {
                // Default sort for time-series data
                query = query.OrderByDescending(c => c.CreatedOn);
            }

            return await query
                .Include(c => c.Citizen)
                .Include(c => c.AssignedEmployee)
                .Include(c => c.Attachments)
                .Select(c => MapToDto(c))
                .ToListAsync();
        }

        public async Task AssignComplaintAsync(Guid complaintId, string employeeId, string managerId)
        {
            var complaint = await _context.Complaints.FindAsync(complaintId);
            if (complaint == null) throw new KeyNotFoundException("Complaint not found");

            var oldValues = JsonConvert.SerializeObject(complaint);

            complaint.AssignedEmployeeId = employeeId;
            complaint.Status = ComplaintStatus.Assigned;
            complaint.AssignedAt = DateTime.UtcNow;

            LogChange(complaint, "Assigned to employee", managerId, oldValues);
            await _context.SaveChangesAsync();

            // Queue background job to send notifications (non-blocking)
            BackgroundJob.Enqueue<INotificationJob>(job =>
                job.SendComplaintAssignedNotificationAsync(employeeId, complaint.Id, complaint.Title));
        }

        public async Task UpdateComplaintStatusAsync(Guid complaintId, ComplaintStatus status, string userId)
        {
            var complaint = await _context.Complaints.FindAsync(complaintId);
            if (complaint == null) throw new KeyNotFoundException("Complaint not found");

            var oldValues = JsonConvert.SerializeObject(complaint);

            complaint.Status = status;
            if (status == ComplaintStatus.Resolved)
            {
                complaint.ResolvedAt = DateTime.UtcNow;
            }

            LogChange(complaint, $"Status updated to {status}", userId, oldValues);
            await _context.SaveChangesAsync();

            // Queue background job to send notification (non-blocking)
            BackgroundJob.Enqueue<INotificationJob>(job =>
                job.SendComplaintStatusChangedNotificationAsync(complaint.CitizenId, complaint.Id, complaint.Title, status));
        }

        public async Task AddAttachmentAsync(Guid complaintId, string filePath, string fileName, long fileSize, string mimeType, string userId)
        {
            var attachment = new ComplaintAttachment
            {
                ComplaintId = complaintId,
                FilePath = filePath,
                FileName = fileName,
                FileSize = fileSize,
                MimeType = mimeType,
                UploadedByUserId = userId,
                IsScanned = false
            };

            _context.ComplaintAttachments.Add(attachment);
            await _context.SaveChangesAsync();

            // Notification
            var complaint = await _context.Complaints.FindAsync(complaintId);
            if (complaint != null)
            {
                BackgroundJob.Enqueue<INotificationJob>(job =>
                   job.SendComplaintAttachmentUploadedNotificationAsync(complaint.Id, fileName));
            }
        }

        public async Task AddNoteAsync(Guid complaintId, string note, string userId)
        {
            var complaint = await _context.Complaints.FindAsync(complaintId);
            if (complaint == null) throw new KeyNotFoundException("Complaint not found");

            var oldValues = JsonConvert.SerializeObject(complaint);

            LogChange(complaint, $"Note added: {note}", userId, oldValues);
            await _context.SaveChangesAsync();

            // Notification
            BackgroundJob.Enqueue<INotificationJob>(job =>
               job.SendComplaintNoteAddedNotificationAsync(complaint.Id, note));
        }

        public async Task<List<ComplaintAuditLogDto>> GetComplaintVersionsAsync(Guid complaintId)
        {
            return await _context.ComplaintAuditLogs
                .Where(l => l.ComplaintId == complaintId)
                .OrderByDescending(l => l.Timestamp)
                .Select(l => new ComplaintAuditLogDto
                {
                    Id = l.Id,
                    ChangeSummary = l.ChangeSummary,
                    ChangedByUserId = l.ChangedByUserId,
                    Timestamp = l.Timestamp,
                    OldValues = l.OldValues,
                    NewValues = l.NewValues
                })
                .ToListAsync();
        }

        public async Task<ComplaintDto> UpdateComplaintDetailsAsync(Guid complaintId, PatchComplaintDto dto, string userId, string role)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var complaint = await _context.Complaints.FindAsync(complaintId);
                if (complaint == null) throw new KeyNotFoundException("Complaint not found");

                var oldValues = JsonConvert.SerializeObject(complaint);
                bool isChanged = false;

                // Role-based restrictions
                if (role == "Citizen")
                {
                    if (complaint.Status != ComplaintStatus.Draft && complaint.Status != ComplaintStatus.Submitted)
                        throw new InvalidOperationException("Citizens can only edit complaints in Draft or Submitted status.");

                    if (dto.Title != null) { complaint.Title = dto.Title; isChanged = true; }
                    if (dto.Description != null) { complaint.Description = dto.Description; isChanged = true; }
                    if (dto.Latitude != null) { complaint.Latitude = dto.Latitude; isChanged = true; }
                    if (dto.Longitude != null) { complaint.Longitude = dto.Longitude; isChanged = true; }
                    if (dto.Address != null) { complaint.Address = dto.Address; isChanged = true; }
                    if (dto.Metadata != null) { complaint.Metadata = dto.Metadata; isChanged = true; }
                }
                else if (role == "DepartmentManager" || role == "Employee")
                {
                    if (dto.Priority != null && Enum.TryParse<ComplaintPriority>(dto.Priority, out var p))
                    {
                        complaint.Priority = p;
                        isChanged = true;
                    }
                    if (role == "DepartmentManager" && dto.DepartmentId != null)
                    {
                        complaint.DepartmentId = dto.DepartmentId;
                        isChanged = true;
                    }
                    // Managers/Employees might update metadata too
                    if (dto.Metadata != null) { complaint.Metadata = dto.Metadata; isChanged = true; }
                }

                if (isChanged)
                {
                    complaint.LastModifiedOn = DateTime.UtcNow;
                    LogChange(complaint, "Details updated via PATCH", userId, oldValues);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                return MapToDto(complaint);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private void LogChange(Complaint complaint, string summary, string userId, string oldValues)
        {
            var newValues = JsonConvert.SerializeObject(complaint);
            var log = new ComplaintAuditLog
            {
                ComplaintId = complaint.Id,
                ChangeSummary = summary,
                ChangedByUserId = userId,
                OldValues = oldValues,
                NewValues = newValues
            };
            _context.ComplaintAuditLogs.Add(log);
        }

        private static ComplaintDto MapToDto(Complaint complaint)
        {
            return new ComplaintDto
            {
                Id = complaint.Id,
                Title = complaint.Title,
                Description = complaint.Description,
                Status = complaint.Status.ToString(),
                Priority = complaint.Priority.ToString(),
                DepartmentId = complaint.DepartmentId,
                CitizenId = complaint.CitizenId,
                CitizenName = complaint.Citizen?.UserName,
                AssignedEmployeeId = complaint.AssignedEmployeeId,
                AssignedEmployeeName = complaint.AssignedEmployee?.UserName,
                CreatedOn = complaint.CreatedOn,
                LastModifiedOn = complaint.LastModifiedOn,
                AssignedAt = complaint.AssignedAt,
                ResolvedAt = complaint.ResolvedAt,
                Latitude = complaint.Latitude,
                Longitude = complaint.Longitude,
                Address = complaint.Address,
                Attachments = complaint.Attachments?.Select(a => new ComplaintAttachmentDto
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    FilePath = a.FilePath,
                    UploadedOn = a.UploadedOn,
                    FileSize = a.FileSize,
                    MimeType = a.MimeType,
                    IsScanned = a.IsScanned,
                    ScanResult = a.ScanResult
                }).ToList() ?? new List<ComplaintAttachmentDto>()
            };
        }
    }
}
