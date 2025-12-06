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

        // 1. User Stats
        var totalUsers = await _context.Users.CountAsync();
        var totalCitizens = await _context.Users.CountAsync(u => u.UserType == UserType.Citizen);
        var totalEmployees = await _context.Users.CountAsync(u => u.UserType == UserType.Employee);
        var totalManagers = await _context.Users.CountAsync(u => u.UserType == UserType.DepartmentManager);

        var usersPerDeptQuery = await _context.Users
            .Where(u => u.Department != null)
            .GroupBy(u => u.Department)
            .Select(g => new { Dept = g.Key, Count = g.Count() })
            .ToListAsync();

        var usersPerDept = usersPerDeptQuery.ToDictionary(k => k.Dept!.ToString()!, v => v.Count);

        // 2. Security Stats
        var blacklistedCount = await _context.IpBlacklist.CountAsync();

        var loginAttemptsQuery = _context.LoginAttempts.AsQueryable();
        if (filter.From.HasValue) loginAttemptsQuery = loginAttemptsQuery.Where(x => x.CreatedAt >= from);
        if (filter.To.HasValue) loginAttemptsQuery = loginAttemptsQuery.Where(x => x.CreatedAt <= to);

        var loginAttempts = await loginAttemptsQuery.CountAsync();
        var successfulLogins = await loginAttemptsQuery.CountAsync(x => x.Success);
        var failedLogins = await loginAttemptsQuery.CountAsync(x => !x.Success);

        // 3. Complaint Stats (Base Query)
        var complaintsQuery = _context.Complaints.AsQueryable();
        if (filter.From.HasValue) complaintsQuery = complaintsQuery.Where(x => x.CreatedOn >= from);
        if (filter.To.HasValue) complaintsQuery = complaintsQuery.Where(x => x.CreatedOn <= to);

        var totalComplaints = await complaintsQuery.CountAsync();
        var solvedComplaints = await complaintsQuery.CountAsync(x => x.Status == ComplaintStatus.Resolved);
        var pendingComplaints = await complaintsQuery.CountAsync(x => x.Status == ComplaintStatus.Pending);

        // -------------------------------------------------------------
        // FIX: Normalize Department Counts (Handle "0" vs "Electricity")
        // -------------------------------------------------------------
        var rawComplaintsPerDept = await complaintsQuery
            .Where(c => c.DepartmentId != null)
            .GroupBy(c => c.DepartmentId)
            .Select(g => new { DeptString = g.Key!, Count = g.Count() })
            .ToListAsync();

        var complaintsPerDept = new Dictionary<string, int>();

        foreach (var item in rawComplaintsPerDept)
        {
            string normalizedDeptName = item.DeptString;

            // Try to parse "0", "1" etc. into "Electricity", "Water"
            if (int.TryParse(item.DeptString, out int deptId))
            {
                if (Enum.IsDefined(typeof(Department), deptId))
                {
                    normalizedDeptName = ((Department)deptId).ToString();
                }
            }

            // Aggregate counts
            if (complaintsPerDept.ContainsKey(normalizedDeptName))
            {
                complaintsPerDept[normalizedDeptName] += item.Count;
            }
            else
            {
                complaintsPerDept[normalizedDeptName] = item.Count;
            }
        }
        // -------------------------------------------------------------

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

        // Prepare both formats ("Electricity" and "0") to query correctly
        var deptEnum = manager.Department.Value;
        string deptName = deptEnum.ToString();
        string deptInt = ((int)deptEnum).ToString();

        // 1. Employee Stats
        var totalEmployees = await _context.Users
            .CountAsync(u => u.UserType == UserType.Employee && u.Department == deptEnum);

        // 2. Complaint Stats (Filtered by Dept ID in both formats)
        var complaintsQuery = _context.Complaints
            .Where(c => c.DepartmentId == deptName || c.DepartmentId == deptInt);

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
                SolvedCount = g.Count()
            })
            .OrderByDescending(x => x.SolvedCount)
            .Take(5)
            .ToListAsync();

        var topPerformers = new List<EmployeePerformanceDto>();
        foreach (var p in topPerformersData)
        {
            if (string.IsNullOrEmpty(p.EmployeeId)) continue;

            var emp = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == p.EmployeeId);
            if (emp != null)
            {
                // Calculate avg time for this employee specifically
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