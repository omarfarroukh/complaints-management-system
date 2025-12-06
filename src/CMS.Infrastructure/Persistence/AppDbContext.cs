using CMS.Application.Interfaces;
using CMS.Domain.Common;
using CMS.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CMS.Infrastructure.Persistence
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        private readonly ICurrentUserService _currentUserService;

        public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserService currentUserService) : base(options)
        {
            _currentUserService = currentUserService;
        }

        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<UserDevice> UserDevices { get; set; } // Added this
        public DbSet<IpBlacklist> IpBlacklist { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<AccountActivationToken> AccountActivationTokens { get; set; }
        public DbSet<LoginAttempt> LoginAttempts { get; set; }
        public DbSet<Complaint> Complaints { get; set; }
        public DbSet<ComplaintAttachment> ComplaintAttachments { get; set; }
        public DbSet<ComplaintAuditLog> ComplaintAuditLogs { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedBy = _currentUserService.UserId;
                        entry.Entity.CreatedOn = DateTime.UtcNow;
                        break;
                    case EntityState.Modified:
                        entry.Entity.LastModifiedBy = _currentUserService.UserId;
                        entry.Entity.LastModifiedOn = DateTime.UtcNow;
                        break;
                }
            }
            return await base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // =========================================================================
            // 1. Identity & System Configuration
            // =========================================================================

            // Rename Identity Tables (Cleaner DB Schema)
            builder.Entity<ApplicationUser>().ToTable("Users");
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityRole>().ToTable("Roles");
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().ToTable("UserRoles");
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<string>>().ToTable("UserClaims");
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>().ToTable("UserLogins");
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>>().ToTable("RoleClaims");
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>().ToTable("UserTokens");

            // Enum Conversions (Store Enums as Strings for readability)
            builder.Entity<ApplicationUser>().Property(u => u.UserType).HasConversion<string>();
            builder.Entity<ApplicationUser>().Property(u => u.Department).HasConversion<string>();

            // =========================================================================
            // 2. Security Entities
            // =========================================================================

            // Refresh Tokens
            builder.Entity<RefreshToken>(entity =>
            {
                entity.HasIndex(r => r.Token);
                // Foreign Key
                entity.HasOne(r => r.User)
                      .WithMany(u => u.RefreshTokens)
                      .HasForeignKey(r => r.UserId);
            });

            // Login Attempts
            builder.Entity<LoginAttempt>(entity =>
            {
                entity.HasIndex(l => l.IpAddress);
                entity.HasIndex(l => l.Email);
                entity.HasIndex(l => l.CreatedAt);
            });

            // IP Blacklist
            builder.Entity<IpBlacklist>(entity =>
            {
                entity.HasIndex(i => i.IpAddress);
                entity.HasIndex(i => i.CreatedAt);
            });

            // =========================================================================
            // 3. User & Profile
            // =========================================================================

            // User Profile (1-to-1)
            builder.Entity<ApplicationUser>()
                .HasOne(u => u.Profile)
                .WithOne(p => p.User)
                .HasForeignKey<UserProfile>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade); // Deleting user deletes profile

            // Account Activation
            builder.Entity<AccountActivationToken>(entity =>
            {
                entity.HasIndex(t => t.Token);
                entity.HasIndex(t => t.UserId);
            });

            // =========================================================================
            // 4. Notifications
            // =========================================================================

            builder.Entity<Notification>(entity =>
            {
                entity.HasIndex(n => n.UserId);
                entity.HasIndex(n => n.IsRead);
                entity.HasIndex(n => n.CreatedOn);
                entity.Property(n => n.Type).HasConversion<string>(); // Store as String
            });

            // =========================================================================
            // 5. Complaints
            // =========================================================================

            builder.Entity<Complaint>(entity =>
            {
                // Indexes for Filtering
                entity.HasIndex(c => c.Status);
                entity.HasIndex(c => c.Priority);
                entity.HasIndex(c => c.CreatedOn);
                entity.HasIndex(c => c.DepartmentId);
                entity.HasIndex(c => c.CitizenId);
                entity.HasIndex(c => c.AssignedEmployeeId);

                // Decimal Precision (Latitude/Longitude)
                entity.Property(c => c.Latitude).HasPrecision(10, 7);
                entity.Property(c => c.Longitude).HasPrecision(10, 7);

                // Enum Conversions
                entity.Property(c => c.Status).HasConversion<string>();
                entity.Property(c => c.Priority).HasConversion<string>();

                // Relationships
                // Prevent deleting a User from deleting the Complaint history (Restrict)
                entity.HasOne(c => c.Citizen)
                      .WithMany()
                      .HasForeignKey(c => c.CitizenId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(c => c.AssignedEmployee)
                      .WithMany()
                      .HasForeignKey(c => c.AssignedEmployeeId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ComplaintAttachment>(entity =>
            {
                entity.HasIndex(a => a.ComplaintId);
            });

            builder.Entity<ComplaintAuditLog>(entity =>
            {
                entity.HasIndex(l => l.ComplaintId);
                entity.HasIndex(l => l.Timestamp);

                entity.HasOne(log => log.Complaint)
                      .WithMany(complaint => complaint.AuditLogs)
                      .HasForeignKey(log => log.ComplaintId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}