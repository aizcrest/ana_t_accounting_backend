using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

sealed class LedgerUser:IdentityUser
{
 public bool Enabled{get;set;}=true;
 public bool PlatformAdmin{get;set;}
 public string? LastTotpHash{get;set;}
 public DateTimeOffset? LastTotpUntil{get;set;}
}
sealed class SchoolRegistration
{
 public Guid Id{get;set;}=Guid.NewGuid();public string Code{get;set;}="";public string Name{get;set;}="";
 public bool Enabled{get;set;}=true;public string ProvisioningStatus{get;set;}="Pending";
 public string DatabaseName{get;set;}="";public string ProtectedConnection{get;set;}="";
}
sealed class SchoolMembership
{
 public Guid Id{get;set;}=Guid.NewGuid();public string UserId{get;set;}="";public Guid SchoolId{get;set;}
 public string Role{get;set;}="Operator";public bool Enabled{get;set;}=true;
}
sealed class LedgerSession
{
 public Guid Id{get;set;}=Guid.NewGuid();public string UserId{get;set;}="";public string SecurityStamp{get;set;}="";
 public DateTimeOffset ExpiresAt{get;set;}=DateTimeOffset.UtcNow.AddHours(8);public DateTimeOffset LastSeen{get;set;}=DateTimeOffset.UtcNow;
 public DateTimeOffset? MfaAt{get;set;}public bool MfaVerified{get;set;}public bool Revoked{get;set;}public string? DeviceId{get;set;}public Guid? SupportGrantId{get;set;}
}
sealed class ApprovedDevice
{
 public string Id{get;set;}="";public string Name{get;set;}="";public Guid? SchoolId{get;set;}public bool Enabled{get;set;}=true;
}
sealed class SupportGrant
{
 public Guid Id{get;set;}=Guid.NewGuid();public string AdminUserId{get;set;}="";public Guid SchoolId{get;set;}
 public string Reason{get;set;}="";public DateTimeOffset ExpiresAt{get;set;}=DateTimeOffset.UtcNow.AddMinutes(15);public bool Revoked{get;set;}
}
sealed class PlatformAudit
{
 public Guid Id{get;set;}=Guid.NewGuid();public string Actor{get;set;}="";public Guid? SchoolId{get;set;}
 public string Action{get;set;}="";public string Outcome{get;set;}="";public DateTimeOffset At{get;set;}=DateTimeOffset.UtcNow;
}
sealed class ControlDb(DbContextOptions<ControlDb> options):IdentityDbContext<LedgerUser>(options)
{
 public DbSet<SchoolRegistration> Schools=>Set<SchoolRegistration>();public DbSet<SchoolMembership> Memberships=>Set<SchoolMembership>();
 public DbSet<LedgerSession> Sessions=>Set<LedgerSession>();public DbSet<ApprovedDevice> Devices=>Set<ApprovedDevice>();
 public DbSet<SupportGrant> SupportGrants=>Set<SupportGrant>();public DbSet<PlatformAudit> PlatformAudit=>Set<PlatformAudit>();
 protected override void OnModelCreating(ModelBuilder b){base.OnModelCreating(b);
  b.Entity<SchoolRegistration>().HasIndex(s=>s.Code).IsUnique();
  b.Entity<SchoolMembership>().HasIndex(m=>m.UserId).IsUnique().HasFilter("\"Enabled\" = TRUE");
  b.Entity<SchoolMembership>().HasIndex(m=>m.SchoolId).IsUnique().HasFilter("\"Enabled\" = TRUE AND \"Role\" = 'Operator'");
  b.Entity<SchoolMembership>().HasOne<LedgerUser>().WithMany().HasForeignKey(m=>m.UserId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<SchoolMembership>().HasOne<SchoolRegistration>().WithMany().HasForeignKey(m=>m.SchoolId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<LedgerSession>().HasOne<LedgerUser>().WithMany().HasForeignKey(s=>s.UserId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<ApprovedDevice>().HasOne<SchoolRegistration>().WithMany().HasForeignKey(d=>d.SchoolId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<SupportGrant>().HasOne<SchoolRegistration>().WithMany().HasForeignKey(d=>d.SchoolId).OnDelete(DeleteBehavior.Restrict);
 }
}
