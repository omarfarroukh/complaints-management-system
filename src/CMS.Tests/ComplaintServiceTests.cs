using CMS.Application.Interfaces;
using CMS.Domain.Common;
using CMS.Domain.Entities;
using CMS.Infrastructure.Hubs;
using CMS.Infrastructure.Persistence;
using CMS.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CMS.Tests
{
    public class ComplaintServiceTests
    {
        private readonly AppDbContext _context;
        private readonly Mock<IHubContext<NotificationHub>> _mockHubContext;
        private readonly Mock<IHubClients> _mockClients;
        private readonly Mock<IClientProxy> _mockClientProxy;
        private readonly Mock<ICurrentUserService> _mockCurrentUserService;
        private readonly ComplaintService _service;

        public ComplaintServiceTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _mockCurrentUserService = new Mock<ICurrentUserService>();
            _mockCurrentUserService.Setup(s => s.UserId).Returns("TestUser");

            _context = new AppDbContext(options, _mockCurrentUserService.Object);

            _mockHubContext = new Mock<IHubContext<NotificationHub>>();
            _mockClients = new Mock<IHubClients>();
            _mockClientProxy = new Mock<IClientProxy>();

            _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
            _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
            _mockClients.Setup(c => c.User(It.IsAny<string>())).Returns(_mockClientProxy.Object);

            _service = new ComplaintService(_context, _mockHubContext.Object);
        }

        [Fact]
        public async Task CreateComplaintAsync_ShouldCreateComplaint_AndNotifyDepartment()
        {
            // Arrange
            var title = "Test Complaint";
            var description = "Test Description";
            var departmentId = "Dept1";
            var citizenId = "User1";

            // Act
            var result = await _service.CreateComplaintAsync(title, description, departmentId, citizenId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(title, result.Title);
            Assert.Equal(ComplaintStatus.Pending, result.Status);
            Assert.Equal(departmentId, result.DepartmentId);

            var savedComplaint = await _context.Complaints.FindAsync(result.Id);
            Assert.NotNull(savedComplaint);

            // Verify notification
            _mockClients.Verify(c => c.Group(departmentId), Times.Once);
            _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveDepartmentMessage",
                It.Is<object[]>(o => o[0].ToString().Contains(title)), default), Times.Once);
        }

        [Fact]
        public async Task GetComplaintByIdAsync_ShouldReturnComplaint_WithRelations()
        {
            // Arrange
            var complaintId = Guid.NewGuid();
            var complaint = new Complaint
            {
                Id = complaintId,
                Title = "Existing Complaint",
                Description = "Desc",
                DepartmentId = "Dept1",
                CitizenId = "User1",
                Status = ComplaintStatus.Pending
            };
            _context.Complaints.Add(complaint);
            await _context.SaveChangesAsync();

            // Act
            var result = await _service.GetComplaintByIdAsync(complaintId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(complaintId, result.Id);
        }

        [Fact]
        public async Task AssignComplaintAsync_ShouldUpdateStatus_AndLogChange()
        {
            // Arrange
            var complaint = new Complaint
            {
                Title = "To Assign",
                Description = "Desc",
                DepartmentId = "Dept1",
                CitizenId = "User1",
                Status = ComplaintStatus.Pending
            };
            _context.Complaints.Add(complaint);
            await _context.SaveChangesAsync();

            var employeeId = "Emp1";
            var managerId = "Mgr1";

            // Act
            await _service.AssignComplaintAsync(complaint.Id, employeeId, managerId);

            // Assert
            var updatedComplaint = await _context.Complaints.FindAsync(complaint.Id);
            Assert.Equal(ComplaintStatus.Assigned, updatedComplaint.Status);
            Assert.Equal(employeeId, updatedComplaint.AssignedEmployeeId);

            var auditLog = await _context.ComplaintAuditLogs.FirstOrDefaultAsync(l => l.ComplaintId == complaint.Id);
            Assert.NotNull(auditLog);
            Assert.Equal("Assigned to employee", auditLog.ChangeSummary);
            Assert.Equal(managerId, auditLog.ChangedByUserId);
        }
    }
}
