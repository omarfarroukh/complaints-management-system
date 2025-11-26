using CMS.Application.DTOs;

namespace CMS.Application.DTOs;

public class DashboardFilterDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class AdminDashboardDto
{
    // User Stats
    public int TotalUsers { get; set; }
    public int TotalCitizens { get; set; }
    public int TotalEmployees { get; set; }
    public int TotalManagers { get; set; }
    public Dictionary<string, int> UsersPerDepartment { get; set; } = new();

    // Security Stats
    public int BlacklistedUsers { get; set; }
    public int LoginAttempts { get; set; }
    public int SuccessfulLogins { get; set; }
    public int FailedLogins { get; set; }

    // Complaint Stats
    public int TotalComplaints { get; set; }
    public int SolvedComplaints { get; set; }
    public int PendingComplaints { get; set; }
    public Dictionary<string, int> ComplaintsPerDepartment { get; set; } = new();
    public Dictionary<string, int> ComplaintsByStatus { get; set; } = new();
}

public class ManagerDashboardDto
{
    // Employee Stats
    public int TotalEmployees { get; set; }

    // Complaint Stats
    public int TotalComplaints { get; set; }
    public int SolvedComplaints { get; set; }
    public int RejectedComplaints { get; set; }
    public int PendingComplaints { get; set; }
    public int InProgressComplaints { get; set; }

    // Performance
    public double AvgSolveTimeHours { get; set; }
    public List<EmployeePerformanceDto> TopPerformers { get; set; } = new();
}

public class EmployeePerformanceDto
{
    public string EmployeeName { get; set; } = string.Empty;
    public int SolvedCount { get; set; }
    public double AvgSolveTimeHours { get; set; }
}
