using CMS.Application.DTOs;

namespace CMS.Application.Interfaces;

public interface IDashboardService
{
    Task<AdminDashboardDto> GetAdminStatsAsync(DashboardFilterDto filter);
    Task<ManagerDashboardDto> GetManagerStatsAsync(string managerId, DashboardFilterDto filter);
}
