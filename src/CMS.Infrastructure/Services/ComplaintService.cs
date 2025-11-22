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

        public async Task<ComplaintDto> CreateComplaintAsync(string title, string description, string departmentId, string citizenId)
        {
            var complaint = new Complaint
            {
                Title = title,
                Description = description,
                DepartmentId = departmentId,
                CitizenId = citizenId,
                Status = ComplaintStatus.Pending,
                Priority = ComplaintPriority.Low
            };

            _context.Complaints.Add(complaint);
            await _context.SaveChangesAsync();

            // Queue background job to notify department (non-blocking)
            BackgroundJob.Enqueue<INotificationJob>(job => 
                job.SendComplaintCreatedNotificationAsync(departmentId, title));

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

        public async Task<List<ComplaintDto>> GetComplaintsForUserAsync(string userId, string role, string? departmentId = null)
        {
            var query = _context.Complaints.AsQueryable();

            if (role == "Citizen")
            {
                query = query.Where(c => c.CitizenId == userId);
            }
            else if (role == "Employee")
            {
                query = query.Where(c => c.AssignedEmployeeId == userId);
            }
            else if (role == "Manager" && !string.IsNullOrEmpty(departmentId))
            {
                query = query.Where(c => c.DepartmentId == departmentId);
            }

            return await query
                .Include(c => c.Citizen)
                .Include(c => c.AssignedEmployee)
                .OrderByDescending(c => c.CreatedOn)
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

            LogChange(complaint, $"Status updated to {status}", userId, oldValues);
            await _context.SaveChangesAsync();

            // Queue background job to send notification (non-blocking)
            BackgroundJob.Enqueue<INotificationJob>(job =>
                job.SendComplaintStatusChangedNotificationAsync(complaint.CitizenId, complaint.Id, complaint.Title, status));
        }

        public async Task AddAttachmentAsync(Guid complaintId, string filePath, string fileName, string userId)
        {
            var attachment = new ComplaintAttachment
            {
                ComplaintId = complaintId,
                FilePath = filePath,
                FileName = fileName,
                UploadedByUserId = userId
            };

            _context.ComplaintAttachments.Add(attachment);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateComplaintDetailsAsync(Guid complaintId, string title, string description, string userId)
        {
            var complaint = await _context.Complaints.FindAsync(complaintId);
            if (complaint == null) throw new KeyNotFoundException("Complaint not found");

            var oldValues = JsonConvert.SerializeObject(complaint);

            complaint.Title = title;
            complaint.Description = description;

            LogChange(complaint, "Details updated", userId, oldValues);
            await _context.SaveChangesAsync();
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
                Attachments = complaint.Attachments?.Select(a => new ComplaintAttachmentDto
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    FilePath = a.FilePath,
                    UploadedOn = a.UploadedOn
                }).ToList() ?? new List<ComplaintAttachmentDto>()
            };
        }
    }
}
