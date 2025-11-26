using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Domain.Common;
using CMS.Domain.Entities;
using CMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMS.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AdminDashboardDto> GetAdminStatsAsync(DashboardFilterDto filter)
    {
        var from = filter.From ?? DateTime.MinValue;
        var to = filter.To ?? DateTime.MaxValue;

        // 1. User Stats (Snapshot - usually not filtered by date unless "Created Between")
        // For "Live" dashboard, we usually want TOTAL counts. 
        // If filtering is applied, we can interpret it as "Users Created Between X and Y"
        // But typically dashboards show "Current Total" and "Activity Between X and Y".
        // Let's assume Counts are TOTAL, and Activity is FILTERED.

        var totalUsers = await _context.Users.CountAsync();
        var totalCitizens = await _context.Users.CountAsync(u => u.UserType == UserType.Citizen);
        var totalEmployees = await _context.Users.CountAsync(u => u.UserType == UserType.Employee);
        var totalManagers = await _context.Users.CountAsync(u => u.UserType == UserType.DepartmentManager);

        var usersPerDept = await _context.Users
            .Where(u => u.Department != null)
            .GroupBy(u => u.Department)
            .Select(g => new { Dept = g.Key!.ToString()!, Count = g.Count() }) // Notice the second '!'
            .ToDictionaryAsync(x => x.Dept, x => x.Count);

        // 2. Security Stats (Filtered by Date)
        var blacklistedCount = await _context.IpBlacklist.CountAsync(); // Current blacklist size

        var loginAttemptsQuery = _context.LoginAttempts.AsQueryable();
        if (filter.From.HasValue) loginAttemptsQuery = loginAttemptsQuery.Where(x => x.CreatedAt >= from);
        if (filter.To.HasValue) loginAttemptsQuery = loginAttemptsQuery.Where(x => x.CreatedAt <= to);

        var loginAttempts = await loginAttemptsQuery.CountAsync();
        var successfulLogins = await loginAttemptsQuery.CountAsync(x => x.Success);
        var failedLogins = await loginAttemptsQuery.CountAsync(x => !x.Success);

        // 3. Complaint Stats (Filtered by Date)
        var complaintsQuery = _context.Complaints.AsQueryable();
        if (filter.From.HasValue) complaintsQuery = complaintsQuery.Where(x => x.CreatedOn >= from);
        if (filter.To.HasValue) complaintsQuery = complaintsQuery.Where(x => x.CreatedOn <= to);

        var totalComplaints = await complaintsQuery.CountAsync();
        var solvedComplaints = await complaintsQuery.CountAsync(x => x.Status == ComplaintStatus.Resolved);
        var pendingComplaints = await complaintsQuery.CountAsync(x => x.Status == ComplaintStatus.Pending);

        var complaintsPerDept = await complaintsQuery
            .Where(c => c.DepartmentId != null) // Filter nulls if DepartmentId is nullable
            .GroupBy(c => c.DepartmentId)
            .Select(g => new { Dept = g.Key!, Count = g.Count() }) // Null-forgiving
            .ToDictionaryAsync(x => x.Dept, x => x.Count);

        var complaintsByStatus = await complaintsQuery
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status.ToString(), x => x.Count);

        return new AdminDashboardDto
        {
            TotalUsers = totalUsers,
            TotalCitizens = totalCitizens,
            TotalEmployees = totalEmployees,
            TotalManagers = totalManagers,
            UsersPerDepartment = usersPerDept, 
            BlacklistedUsers = blacklistedCount,
            LoginAttempts = loginAttempts,
            SuccessfulLogins = successfulLogins,
            FailedLogins = failedLogins,
            TotalComplaints = totalComplaints,
            SolvedComplaints = solvedComplaints,
            PendingComplaints = pendingComplaints,
            ComplaintsPerDepartment = complaintsPerDept,
            ComplaintsByStatus = complaintsByStatus
        };
    }

    public async Task<ManagerDashboardDto> GetManagerStatsAsync(string managerId, DashboardFilterDto filter)
    {
        var from = filter.From ?? DateTime.MinValue;
        var to = filter.To ?? DateTime.MaxValue;

        // Get Manager's Department
        var manager = await _context.Users.FindAsync(managerId);
        if (manager == null || manager.Department == null)
            throw new Exception("Manager or Department not found");

        var departmentId = manager.Department.ToString();

        // 1. Employee Stats
        var totalEmployees = await _context.Users
            .CountAsync(u => u.UserType == UserType.Employee && u.Department == manager.Department);

        // 2. Complaint Stats (Filtered)
        var complaintsQuery = _context.Complaints
            .Where(c => c.DepartmentId == departmentId);

        if (filter.From.HasValue) complaintsQuery = complaintsQuery.Where(x => x.CreatedOn >= from);
        if (filter.To.HasValue) complaintsQuery = complaintsQuery.Where(x => x.CreatedOn <= to);

        var totalComplaints = await complaintsQuery.CountAsync();
        var solvedComplaints = await complaintsQuery.CountAsync(x => x.Status == ComplaintStatus.Resolved);
        var rejectedComplaints = await complaintsQuery.CountAsync(x => x.Status == ComplaintStatus.Rejected);
        var pendingComplaints = await complaintsQuery.CountAsync(x => x.Status == ComplaintStatus.Pending);
        var inProgressComplaints = await complaintsQuery.CountAsync(x => x.Status == ComplaintStatus.InProgress);

        // 3. Performance Metrics
        // Avg Solve Time (Hours)
        var resolvedComplaints = await complaintsQuery
            .Where(c => c.Status == ComplaintStatus.Resolved && c.ResolvedAt.HasValue)
            .Select(c => new { Created = c.CreatedOn, Resolved = c.ResolvedAt!.Value })
            .ToListAsync();

        double avgSolveTime = 0;
        if (resolvedComplaints.Any())
        {
            avgSolveTime = resolvedComplaints.Average(c => (c.Resolved - c.Created).TotalHours);
        }

        // Top Performers
        // Group by AssignedEmployeeId, Count Resolved, Avg Time
        var topPerformersData = await complaintsQuery
            .Where(c => c.Status == ComplaintStatus.Resolved && c.AssignedEmployeeId != null)
            .GroupBy(c => c.AssignedEmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                SolvedCount = g.Count(),
                // EF Core can't translate complex time math easily in GroupBy sometimes, 
                // but let's try simple diff if supported, or fetch and process.
                // For simplicity/reliability in this snippet, we might need to fetch stats or use a simpler metric.
                // Let's stick to SolvedCount for DB query, and we can enrich names later.
            })
            .OrderByDescending(x => x.SolvedCount)
            .Take(5)
            .ToListAsync();

        var topPerformers = new List<EmployeePerformanceDto>();
        foreach (var p in topPerformersData)
        {
            var emp = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == p.EmployeeId);
            if (emp != null)
            {
                // Calculate avg time for this employee specifically
                // This is an N+1 query risk but for top 5 it's negligible.
                var empResolved = await complaintsQuery
                    .Where(c => c.AssignedEmployeeId == p.EmployeeId && c.Status == ComplaintStatus.Resolved && c.ResolvedAt.HasValue)
                    .Select(c => (c.ResolvedAt!.Value - c.CreatedOn).TotalHours)
                    .ToListAsync();

                double empAvgTime = empResolved.Any() ? empResolved.Average() : 0;

                topPerformers.Add(new EmployeePerformanceDto
                {
                    EmployeeName = (emp.Profile != null ? $"{emp.Profile.FirstName} {emp.Profile.LastName}" : emp.Email) ?? "Unknown",
                    SolvedCount = p.SolvedCount,
                    AvgSolveTimeHours = empAvgTime
                });
            }
        }

        return new ManagerDashboardDto
        {
            TotalEmployees = totalEmployees,
            TotalComplaints = totalComplaints,
            SolvedComplaints = solvedComplaints,
            RejectedComplaints = rejectedComplaints,
            PendingComplaints = pendingComplaints,
            InProgressComplaints = inProgressComplaints,
            AvgSolveTimeHours = avgSolveTime,
            TopPerformers = topPerformers
        };
    }
}
