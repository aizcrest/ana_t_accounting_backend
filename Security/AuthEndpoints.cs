using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

record LoginRequest(string Email,string Password);
record InvitationRequest(string UserId,string Token,string Password);
record CodeRequest(string Code);
record InviteUserRequest(string Email,Guid? SchoolId,string Role);
record StatusRequest(bool Enabled);
record SchoolRequest(string Name,string Code);
record SupportRequest(Guid SchoolId,string Reason,int Minutes=15);
record SupportContextRequest(Guid? GrantId);
record DeviceRequest(string Id,string Name,Guid? SchoolId,bool Enabled);
static class AuthEndpoints
{
 static object UserView(LedgerUser user,LedgerSession session,SchoolRegistration? school,string role)=>new{email=user.Email,platformAdmin=user.PlatformAdmin,mfaVerified=session.MfaVerified,needsEnrollment=!user.TwoFactorEnabled,school=school is null?null:new{id=school.Id,name=school.Name,code=school.Code},role,readOnly=role is "Viewer" or "SupportViewer",supportGrantId=session.SupportGrantId};
 static async Task Audit(ControlDb db,string actor,string action,string outcome,Guid? school=null){db.PlatformAudit.Add(new(){Actor=actor,Action=action,Outcome=outcome,SchoolId=school});await db.SaveChangesAsync();}
 static void Check(IdentityResult result){if(!result.Succeeded)throw new ArgumentException(string.Join(" ",result.Errors.Select(e=>e.Description)));}
 public static void Map(WebApplication app){
  app.MapGet("/api/auth/csrf",(HttpContext h,IAntiforgery antiforgery)=>Results.Ok(new{token=antiforgery.GetAndStoreTokens(h).RequestToken}));
  app.MapPost("/api/auth/login",async(LoginRequest input,HttpContext h,ControlDb db,UserManager<LedgerUser> users)=>{
   if(string.IsNullOrWhiteSpace(input.Email)||input.Email.Length>256||string.IsNullOrEmpty(input.Password)||input.Password.Length>1024)return Results.Json(new{error="Sign-in failed. Check your credentials or contact the administrator."},statusCode:401);
   var user=await users.FindByEmailAsync(input.Email.Trim());
   if(user is null||!user.Enabled||await users.IsLockedOutAsync(user)||!await users.CheckPasswordAsync(user,input.Password)){
    if(user is not null&&user.Enabled&&!await users.IsLockedOutAsync(user))await users.AccessFailedAsync(user);
    await Audit(db,user?.Id??"anonymous","login","Failed");return Results.Json(new{error="Sign-in failed. Check your credentials or contact the administrator."},statusCode:401);
   }
   if(!user.PlatformAdmin&&!await db.Memberships.AnyAsync(m=>m.UserId==user.Id&&m.Enabled&&db.Schools.Any(s=>s.Id==m.SchoolId&&s.Enabled&&s.ProvisioningStatus=="Ready")))return Results.Json(new{error="No approved active school assignment."},statusCode:403);
   await users.ResetAccessFailedCountAsync(user);var device=h.Items["VerifiedDevice"] as ApprovedDevice;
   if(SecuritySetup.RequiresDevice(h.RequestServices.GetRequiredService<IConfiguration>())){
    var membership=user.PlatformAdmin?null:await db.Memberships.AsNoTracking().SingleOrDefaultAsync(m=>m.UserId==user.Id&&m.Enabled);
    if(device is null||(user.PlatformAdmin?device.SchoolId is not null:device.SchoolId!=membership?.SchoolId))return Results.Json(new{error="This workstation is not approved for this account's school."},statusCode:403);
   }
   var session=new LedgerSession{UserId=user.Id,SecurityStamp=user.SecurityStamp!,DeviceId=device?.Id};db.Sessions.Add(session);await db.SaveChangesAsync();
   var identity=new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.Id),new Claim("sid",session.Id.ToString())},CookieAuthenticationDefaults.AuthenticationScheme);
   await h.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(identity),new AuthenticationProperties{IsPersistent=false,ExpiresUtc=session.ExpiresAt});await Audit(db,user.Id,"login","PendingMfa");
   return Results.Ok(new{needsEnrollment=!user.TwoFactorEnabled,needsMfa=true});
  }).RequireRateLimiting("login");
  app.MapPost("/api/auth/accept-invitation",async(InvitationRequest input,ControlDb db,UserManager<LedgerUser> users)=>{
   var user=await users.FindByIdAsync(input.UserId);if(user is null||!user.Enabled||user.PasswordHash is not null)return Results.BadRequest(new{error="Invitation is unavailable."});
   Check(await users.ResetPasswordAsync(user,input.Token,input.Password));await Audit(db,user.Id,"invitation.accept","Success");return Results.Ok(new{message="Password set. Sign in and enroll MFA."});
  }).RequireRateLimiting("login");
  app.MapGet("/api/auth/me",async(HttpContext h,ControlDb db)=>{
   var user=(LedgerUser)h.Items["User"]!;var session=(LedgerSession)h.Items["Session"]!;SchoolRegistration? school=null;string role="PlatformAdmin";
   if(!user.PlatformAdmin){var m=await db.Memberships.AsNoTracking().SingleOrDefaultAsync(m=>m.UserId==user.Id&&m.Enabled);if(m!=null){school=await db.Schools.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==m.SchoolId&&s.Enabled);role=m.Role;}}
   else if(session.SupportGrantId!=null){var grant=await db.SupportGrants.AsNoTracking().SingleOrDefaultAsync(g=>g.Id==session.SupportGrantId&&!g.Revoked&&g.ExpiresAt>DateTimeOffset.UtcNow);if(grant!=null){school=await db.Schools.AsNoTracking().SingleAsync(s=>s.Id==grant.SchoolId);role="SupportViewer";}}
   return Results.Ok(UserView(user,session,school,role));
  });
  app.MapPost("/api/auth/logout",async(HttpContext h,ControlDb db)=>{var session=(LedgerSession)h.Items["Session"]!;session.Revoked=true;await Audit(db,session.UserId,"logout","Success");await h.SignOutAsync();return Results.NoContent();});
  app.MapPost("/api/auth/mfa/setup",async(HttpContext h,ControlDb db,UserManager<LedgerUser> users)=>{var user=(LedgerUser)h.Items["User"]!;if(user.TwoFactorEnabled)return Results.BadRequest(new{error="MFA is already configured."});var key=await users.GetAuthenticatorKeyAsync(user);if(key is null){Check(await users.ResetAuthenticatorKeyAsync(user));key=await users.GetAuthenticatorKeyAsync(user);}((LedgerSession)h.Items["Session"]!).SecurityStamp=user.SecurityStamp!;await db.SaveChangesAsync();return Results.Ok(new{key,uri=$"otpauth://totp/LedgerPH:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer=LedgerPH&digits=6"});});
  app.MapPost("/api/auth/mfa/verify",async(CodeRequest input,HttpContext h,ControlDb db,UserManager<LedgerUser> users)=>{
   var user=(LedgerUser)h.Items["User"]!;var session=(LedgerSession)h.Items["Session"]!;
   if(await users.IsLockedOutAsync(user))return Results.Json(new{error="MFA verification is temporarily locked."},statusCode:401);
   var code=(input.Code??"").Replace(" ","").Trim();var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
   var valid=await users.VerifyTwoFactorTokenAsync(user,TokenOptions.DefaultAuthenticatorProvider,code);
   if(valid&&user.LastTotpHash==hash&&user.LastTotpUntil>DateTimeOffset.UtcNow)valid=false;
   if(!valid){await users.AccessFailedAsync(user);await Audit(db,user.Id,"mfa.verify","Failed");return Results.Json(new{error="Invalid or already used MFA code. Wait for the next authenticator code."},statusCode:401);}
   var enrolled=!user.TwoFactorEnabled;if(enrolled)Check(await users.SetTwoFactorEnabledAsync(user,true));
   user.LastTotpHash=hash;user.LastTotpUntil=DateTimeOffset.UtcNow.AddMinutes(5);Check(await users.UpdateAsync(user));await users.ResetAccessFailedCountAsync(user);
   session.MfaVerified=true;session.MfaAt=DateTimeOffset.UtcNow;session.SecurityStamp=user.SecurityStamp!;session.LastSeen=DateTimeOffset.UtcNow;
   var recoveryCodes=enrolled?await users.GenerateNewTwoFactorRecoveryCodesAsync(user,10):null;session.SecurityStamp=user.SecurityStamp!;await Audit(db,user.Id,"mfa.verify","Success");return Results.Ok(new{verified=true,recoveryCodes});
  }).RequireRateLimiting("login");
  app.MapPost("/api/auth/mfa/recovery",async(CodeRequest input,HttpContext h,ControlDb db,UserManager<LedgerUser> users)=>{
   var user=(LedgerUser)h.Items["User"]!;var session=(LedgerSession)h.Items["Session"]!;if(!user.TwoFactorEnabled||await users.IsLockedOutAsync(user))return Results.Unauthorized();
   var result=await users.RedeemTwoFactorRecoveryCodeAsync(user,(input.Code??"").Trim());if(!result.Succeeded){await users.AccessFailedAsync(user);await Audit(db,user.Id,"mfa.recovery","Failed");return Results.Json(new{error="Invalid recovery code."},statusCode:401);}
   session.MfaVerified=true;session.SecurityStamp=user.SecurityStamp!;session.MfaAt=null;await Audit(db,user.Id,"mfa.recovery","Success");return Results.Ok(new{verified=true});
  }).RequireRateLimiting("login");
  app.MapPost("/api/auth/support-context",async(SupportContextRequest input,HttpContext h,ControlDb db)=>{
   var user=(LedgerUser)h.Items["User"]!;var session=(LedgerSession)h.Items["Session"]!;if(!user.PlatformAdmin||!session.MfaVerified)return Results.Forbid();
   if(input.GrantId!=null&&!await db.SupportGrants.AnyAsync(g=>g.Id==input.GrantId&&g.AdminUserId==user.Id&&!g.Revoked&&g.ExpiresAt>DateTimeOffset.UtcNow))return Results.Forbid();
   session.SupportGrantId=input.GrantId;await Audit(db,user.Id,"support.context",input.GrantId?.ToString()??"Cleared");return Results.Ok();
  });
  app.MapGet("/api/admin/overview",async(ControlDb db)=>Results.Ok(new{
   schools=await db.Schools.Select(s=>new{s.Id,s.Name,s.Code,s.Enabled,s.ProvisioningStatus}).ToListAsync(),
   users=await db.Users.Select(u=>new{u.Id,u.Email,u.Enabled,u.PlatformAdmin,u.TwoFactorEnabled}).ToListAsync(),
   memberships=await db.Memberships.AsNoTracking().ToListAsync(),devices=await db.Devices.AsNoTracking().ToListAsync(),
   grants=await db.SupportGrants.AsNoTracking().ToListAsync(),audit=await db.PlatformAudit.OrderByDescending(a=>a.At).Take(100).AsNoTracking().ToListAsync()}));
  app.MapPost("/api/admin/schools",async(SchoolRequest input,HttpContext h,ControlDb db)=>{
   if(string.IsNullOrWhiteSpace(input.Name)||string.IsNullOrWhiteSpace(input.Code)||input.Name.Length>200||input.Code.Length>80)throw new ArgumentException("Enter a registered school name and code.");
   var school=new SchoolRegistration{Name=input.Name.Trim(),Code=input.Code.Trim()};db.Schools.Add(school);await Audit(db,((LedgerUser)h.Items["User"]!).Id,"school.register","Pending",school.Id);return Results.Ok(new{school.Id,school.Name,school.Code,school.ProvisioningStatus});
  });
  app.MapPut("/api/admin/schools/{id:guid}/status",async(Guid id,StatusRequest input,HttpContext h,ControlDb db)=>{var school=await db.Schools.SingleOrDefaultAsync(s=>s.Id==id);if(school is null)return Results.NotFound();school.Enabled=input.Enabled;await Audit(db,((LedgerUser)h.Items["User"]!).Id,"school.status",input.Enabled?"Enabled":"Disabled",id);return Results.Ok();});
  app.MapPost("/api/admin/users",async(InviteUserRequest input,HttpContext h,ControlDb db,UserManager<LedgerUser> users)=>{
   if(!new[]{"Operator","Viewer"}.Contains(input.Role)||input.SchoolId==null||!await db.Schools.AnyAsync(s=>s.Id==input.SchoolId&&s.Enabled&&s.ProvisioningStatus=="Ready"))throw new ArgumentException("Select a ready school and Operator or Viewer role.");
   if(input.Role=="Operator"&&await db.Memberships.AnyAsync(m=>m.SchoolId==input.SchoolId&&m.Enabled&&m.Role=="Operator"))throw new ArgumentException("Disable the school's previous operator membership first.");
   var user=new LedgerUser{UserName=input.Email.Trim(),Email=input.Email.Trim()};Check(await users.CreateAsync(user));db.Memberships.Add(new(){UserId=user.Id,SchoolId=input.SchoolId.Value,Role=input.Role});var token=await users.GeneratePasswordResetTokenAsync(user);await Audit(db,((LedgerUser)h.Items["User"]!).Id,"user.invite:"+user.Id,"Success",input.SchoolId);return Results.Ok(new{userId=user.Id,token,message="Share this one-time invitation privately. No email was sent."});
  });
  app.MapPut("/api/admin/users/{id}/status",async(string id,StatusRequest input,HttpContext h,ControlDb db,UserManager<LedgerUser> users)=>{
   var actor=(LedgerUser)h.Items["User"]!;if(actor.Id==id)throw new ArgumentException("Use another administrator to change this account's status.");var user=await users.FindByIdAsync(id);if(user is null)return Results.NotFound();if(input.Enabled){var memberships=await db.Memberships.Where(m=>m.UserId==id).ToListAsync();foreach(var membership in memberships.Where(m=>m.Role=="Operator"))if(await db.Memberships.AnyAsync(m=>m.UserId!=id&&m.SchoolId==membership.SchoolId&&m.Enabled&&m.Role=="Operator"))throw new ArgumentException("This school already has another active operator. Change that assignment first.");}
   user.Enabled=input.Enabled;Check(await users.UpdateSecurityStampAsync(user));Check(await users.UpdateAsync(user));
   foreach(var session in await db.Sessions.Where(s=>s.UserId==id&&!s.Revoked).ToListAsync())session.Revoked=true;
   foreach(var member in await db.Memberships.Where(m=>m.UserId==id).ToListAsync())member.Enabled=input.Enabled;
   await Audit(db,actor.Id,"user.status:"+user.Id,input.Enabled?"Enabled":"Disabled");return Results.Ok();
  });
  app.MapPut("/api/admin/memberships/{id:guid}/role",async(Guid id,string role,HttpContext h,ControlDb db)=>{if(!new[]{"Operator","Viewer"}.Contains(role))throw new ArgumentException("Invalid role.");var m=await db.Memberships.SingleOrDefaultAsync(m=>m.Id==id);if(m is null)return Results.NotFound();if(role=="Operator"&&await db.Memberships.AnyAsync(x=>x.Id!=id&&x.SchoolId==m.SchoolId&&x.Enabled&&x.Role=="Operator"))throw new ArgumentException("School already has an active operator.");m.Role=role;await Audit(db,((LedgerUser)h.Items["User"]!).Id,"membership.role:"+m.Id,role,m.SchoolId);return Results.Ok();});
  app.MapPost("/api/admin/support-grants",async(SupportRequest input,HttpContext h,ControlDb db)=>{
   var session=(LedgerSession)h.Items["Session"]!;if(session.MfaAt is null||session.MfaAt.Value.AddMinutes(10)<DateTimeOffset.UtcNow)return Results.Json(new{error="Verify a fresh authenticator code before requesting support access."},statusCode:403);
   if(string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Length>500||input.Minutes<1||input.Minutes>30||!await db.Schools.AnyAsync(s=>s.Id==input.SchoolId&&s.Enabled&&s.ProvisioningStatus=="Ready"))throw new ArgumentException("Select an active school, provide a reason and use a duration of 1–30 minutes.");
   var grant=new SupportGrant{AdminUserId=session.UserId,SchoolId=input.SchoolId,Reason=input.Reason.Trim(),ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(input.Minutes)};db.SupportGrants.Add(grant);await Audit(db,session.UserId,"support.grant:"+grant.Id,"Read-only",input.SchoolId);return Results.Ok(grant);
  });
  app.MapDelete("/api/admin/support-grants/{id:guid}",async(Guid id,HttpContext h,ControlDb db)=>{var grant=await db.SupportGrants.SingleOrDefaultAsync(g=>g.Id==id);if(grant is null)return Results.NotFound();grant.Revoked=true;await Audit(db,((LedgerUser)h.Items["User"]!).Id,"support.revoke:"+grant.Id,"Success",grant.SchoolId);return Results.NoContent();});
  app.MapPost("/api/admin/devices",async(DeviceRequest input,HttpContext h,ControlDb db)=>{
   var fingerprint=input.Id.Replace(":","").ToUpperInvariant();if(fingerprint.Length!=64||fingerprint.Any(c=>!Uri.IsHexDigit(c))||string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>200||input.SchoolId.HasValue&&!await db.Schools.AnyAsync(s=>s.Id==input.SchoolId))throw new ArgumentException("Enter a SHA-256 certificate fingerprint, device name and valid school scope (or administrator workstation).");
   var device=await db.Devices.SingleOrDefaultAsync(d=>d.Id==fingerprint);if(device is null){device=new(){Id=fingerprint};db.Devices.Add(device);}var scopeChanged=device.SchoolId!=input.SchoolId;device.Name=input.Name;device.SchoolId=input.SchoolId;device.Enabled=input.Enabled;
   if(!input.Enabled||scopeChanged)foreach(var session in await db.Sessions.Where(s=>s.DeviceId==fingerprint&&!s.Revoked).ToListAsync())session.Revoked=true;
   await Audit(db,((LedgerUser)h.Items["User"]!).Id,"device.approve:"+fingerprint,input.Enabled?"Enabled":"Revoked",input.SchoolId);return Results.Ok(device);
  });
 }
}
