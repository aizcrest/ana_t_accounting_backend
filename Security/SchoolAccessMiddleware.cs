using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

sealed class SchoolAccessMiddleware(RequestDelegate next)
{
 static bool Unsafe(HttpRequest r)=>!new[]{"GET","HEAD","OPTIONS"}.Contains(r.Method);
 static async Task Error(HttpContext h,int status,string message){
  var actor=h.User.FindFirstValue(ClaimTypes.NameIdentifier);if(actor is not null){try{var db=h.RequestServices.GetRequiredService<ControlDb>();db.PlatformAudit.Add(new(){Actor=actor,Action=h.Request.Method+" "+h.Request.Path,Outcome="Denied "+status});await db.SaveChangesAsync();}catch{h.RequestServices.GetRequiredService<ILogger<SchoolAccessMiddleware>>().LogWarning("Could not append denied-access audit event {Trace}",h.TraceIdentifier);}}
  h.Response.StatusCode=status;await h.Response.WriteAsJsonAsync(new{error=message});
 }
 public async Task InvokeAsync(HttpContext h,ControlDb control,IConfiguration config,IAntiforgery csrf){
  if(!h.Request.Path.StartsWithSegments("/api")){await next(h);return;}
  if(SecuritySetup.IsLocalMode(config)){
   bool Loopback(System.Net.IPAddress? address)=>address is not null&&System.Net.IPAddress.IsLoopback(address.IsIPv4MappedToIPv6?address.MapToIPv4():address);
   if(!Loopback(h.Connection.RemoteIpAddress)||!Loopback(h.Connection.LocalIpAddress)){await Error(h,403,"Local development/test mode accepts loopback connections only.");return;}
  }
  h.Response.Headers.CacheControl="no-cache, no-store";h.Response.Headers.Pragma="no-cache";
  if(h.Request.Path=="/api/health"){await next(h);return;}
  if(SecuritySetup.IsLegacy(config)){await next(h);return;}
  if(SecuritySetup.RequiresDevice(config)){
   if(!h.Request.IsHttps){await Error(h,403,"HTTPS is required.");return;}
   var certificate=await h.Connection.GetClientCertificateAsync();
   if(certificate is null||!SecuritySetup.ValidateCertificate(certificate,config["Ledger:ClientCaFile"]!)){await Error(h,403,"Approved workstation certificate required.");return;}
   var thumbprint=certificate.GetCertHashString(HashAlgorithmName.SHA256);var device=await control.Devices.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==thumbprint&&d.Enabled);
   if(device is null){await Error(h,403,"This workstation is not approved.");return;}h.Items["VerifiedDevice"]=device;
  }
  if(Unsafe(h.Request)){
   if(h.Request.Headers.TryGetValue("Origin",out var origin)&&origin.ToString()!=$"{h.Request.Scheme}://{h.Request.Host}"){await Error(h,400,"Invalid request origin.");return;}
   try{await csrf.ValidateRequestAsync(h);}catch(AntiforgeryValidationException){await Error(h,400,"Refresh the page and retry (CSRF validation).");return;}
  }
  var path=h.Request.Path.Value!;
  if(path is "/api/auth/csrf" or "/api/auth/login" or "/api/auth/accept-invitation"){await next(h);return;}
  if(!Guid.TryParse(h.User.FindFirstValue("sid"),out var sid)){await Error(h,401,"Sign in to continue.");return;}
  var session=await control.Sessions.SingleOrDefaultAsync(s=>s.Id==sid);var now=DateTimeOffset.UtcNow;
  var user=session is null?null:await control.Users.SingleOrDefaultAsync(u=>u.Id==session.UserId);
  if(session is null||session.Revoked||session.ExpiresAt<=now||session.LastSeen.AddMinutes(session.MfaVerified?30:10)<=now||user is null||!user.Enabled||user.SecurityStamp!=session.SecurityStamp||h.User.FindFirstValue(ClaimTypes.NameIdentifier)!=user.Id){await Error(h,401,"Your session has expired or been revoked.");return;}
  var verified=h.Items["VerifiedDevice"] as ApprovedDevice;
  if(SecuritySetup.RequiresDevice(config)&&session.DeviceId!=verified?.Id){await Error(h,403,"Session does not belong to this workstation.");return;}
  if(SecuritySetup.RequiresDevice(config)&&user.PlatformAdmin&&verified?.SchoolId is not null){await Error(h,403,"Administrator access requires an administrator workstation.");return;}
  if(path!="/api/auth/me"){session.LastSeen=now;await control.SaveChangesAsync();}h.Items["Session"]=session;h.Items["User"]=user;
  if(path.StartsWith("/api/auth/",StringComparison.Ordinal)){await next(h);return;}
  if(!session.MfaVerified||!user.TwoFactorEnabled){await Error(h,403,"Complete MFA enrollment or verification first.");return;}
  if(path.StartsWith("/api/admin/",StringComparison.Ordinal)){
   if(!user.PlatformAdmin){await Error(h,403,"Administrator access required.");return;}if(Unsafe(h.Request)&&(session.MfaAt is null||session.MfaAt.Value.AddMinutes(10)<now)){await Error(h,403,"Verify a fresh authenticator code in Support access before making administrator changes.");return;}await next(h);return;
  }
  SchoolRegistration? school;string role;
  if(user.PlatformAdmin){var grant=await control.SupportGrants.AsNoTracking().SingleOrDefaultAsync(g=>g.Id==session.SupportGrantId&&g.AdminUserId==user.Id&&!g.Revoked&&g.ExpiresAt>now);if(grant is null){await Error(h,403,"Select an active, audited support grant first.");return;}school=await control.Schools.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==grant.SchoolId);role="SupportViewer";}
  else{var member=await control.Memberships.AsNoTracking().SingleOrDefaultAsync(m=>m.UserId==user.Id&&m.Enabled);if(member is null){await Error(h,403,"No active school assignment.");return;}school=await control.Schools.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==member.SchoolId);role=member.Role;}
  if(school is null||!school.Enabled||school.ProvisioningStatus!="Ready"){await Error(h,403,"School access is unavailable.");return;}
  if(SecuritySetup.RequiresDevice(config)&&verified?.SchoolId!=school.Id&&!(user.PlatformAdmin&&verified?.SchoolId is null)){await Error(h,403,"This workstation is not approved for your school.");return;}
  if(h.Request.Headers.ContainsKey("X-School-Id")||(h.Request.Query.TryGetValue("schoolId",out var target)&&target.ToString()!=school.Id.ToString())){await Error(h,403,"School context is assigned by the server.");return;}
  if(Unsafe(h.Request)&&(role!="Operator"||path=="/api/access")){await Error(h,403,"Your role does not allow this operation.");return;}
  h.Items["SchoolRegistration"]=school;h.Items["SchoolContext"]=new SchoolContext(school.Id,school.Code,school.Name,user.Id,role,verified?.Id,sid);
  // All response content is generated from this scoped workspace. Audit reads before sending it.
  if(!Unsafe(h.Request))h.RequestServices.GetRequiredService<SchoolWorkspace>().AuditRead();
  await next(h);
 }
}
