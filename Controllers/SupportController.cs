using System.Security.Claims;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SupportController(ApplicationDbContext db, SupportAccessService access, TimeProvider clock) : Controller
{
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Index()
    {
        if ((await HttpContext.AuthenticateAsync(SupportAccessService.Scheme)).Succeeded) return RedirectToAction(nameof(Requests));
        var application = await HttpContext.AuthenticateAsync(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
        if (application.Succeeded) {
            var id = application.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && !u.IsDisabled && u.EmailConfirmed);
            if (user is not null) { await SignInSupport(user); return RedirectToAction(nameof(Requests)); }
        }
        return View(new SupportStartInput());
    }

    private Task SignInSupport(ApplicationUser user)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim("support:stamp", user.SecurityStamp ?? "")], SupportAccessService.Scheme);
        return HttpContext.SignInAsync(SupportAccessService.Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false, ExpiresUtc = clock.GetUtcNow().AddMinutes(30) });
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("support-access")]
    public async Task<IActionResult> Index(SupportStartInput input)
    {
        if (!ModelState.IsValid) return View(input);
        await access.RequestAsync(input.Email);
        return View("Sent");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Verify(string? code)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View(model: code is { Length: 64 } && code.All(Uri.IsHexDigit) ? code : "");
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("support-access")]
    public async Task<IActionResult> Verify(string code, bool confirm)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var user = confirm ? await access.RedeemAsync(code ?? "") : null;
        if (user is null) { ModelState.AddModelError("", "This link has expired or has already been used. Request a new support email."); return View(model: ""); }
        await SignInSupport(user);
        return RedirectToAction(nameof(Requests));
    }

    [Authorize(AuthenticationSchemes = SupportAccessService.Scheme), HttpGet]
    public async Task<IActionResult> Requests(int page = 1)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = db.SupportTickets.AsNoTracking().Where(t => t.UserId == id);
        var total = await query.CountAsync(); page = Math.Clamp(page, 1, Math.Max(1,(int)Math.Ceiling(total/20d)));
        return View(new SupportListModel { Page = page, Total = total, Tickets = await query.OrderByDescending(t => t.UpdatedUtc).ThenByDescending(t => t.Id).Skip((page-1)*20).Take(20).ToListAsync() });
    }

    [Authorize(AuthenticationSchemes = SupportAccessService.Scheme), HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewData["CanAppeal"] = await db.Users.AnyAsync(u => u.Id == User.FindFirstValue(ClaimTypes.NameIdentifier) && u.IsDisabled);
        return View(new SupportMessageInput { Category = SupportCategory.SignIn });
    }

    [Authorize(AuthenticationSchemes = SupportAccessService.Scheme), HttpPost, EnableRateLimiting("support-message")]
    public async Task<IActionResult> Create(SupportMessageInput input)
    {
        ViewData["CanAppeal"] = await db.Users.AnyAsync(u => u.Id == User.FindFirstValue(ClaimTypes.NameIdentifier) && u.IsDisabled);
        if (!SupportCategory.All.Contains(input.Category)) ModelState.AddModelError("Category", "Choose a valid account support type.");
        if (input.Category == SupportCategory.Appeal && ViewData["CanAppeal"] as bool? != true) ModelState.AddModelError("Category", "Only a currently disabled account can submit a suspension appeal. Choose another account support type.");
        if (string.IsNullOrWhiteSpace(input.Subject)) ModelState.AddModelError("Subject", "Enter a subject of at least 5 characters.");
        if (!ModelState.IsValid) return View(input);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!; var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = (await db.Users.FromSqlInterpolated($"SELECT * FROM AspNetUsers WHERE Id={userId} FOR UPDATE").ToListAsync()).Single();
        if (user.SecurityStamp != User.FindFirstValue("support:stamp")) return Forbid();
        if (input.Category == SupportCategory.Appeal && !user.IsDisabled) {
            ViewData["CanAppeal"] = false;
            ModelState.AddModelError("Category", "This account is active now. Choose another account support type."); return View(input);
        }
        var open = await db.SupportTickets.FirstOrDefaultAsync(t => t.UserId == userId && t.Status != "Resolved");
        if (open is not null) { TempData["SupportNotice"] = "You already have an open request. Add your message to that conversation."; return RedirectToAction(nameof(Thread), new { id = open.Id }); }
        if (await db.SupportTickets.CountAsync(t => t.UserId == userId && t.CreatedUtc >= now.AddDays(-1)) >= 3) {
            ModelState.AddModelError("", "You can open up to three requests per day. Use an existing conversation or try again tomorrow."); return View(input);
        }
        var ticket = new SupportTicket { UserId = userId, Category = input.Category, Subject = input.Subject!.Trim(), CreatedUtc = now, UpdatedUtc = now, UserReadUtc = now };
        db.SupportTickets.Add(ticket); await db.SaveChangesAsync();
        db.SupportMessages.Add(new SupportMessage { TicketId = ticket.Id, AuthorId = userId, AuthorName = user.DisplayName, Body = input.Body.Trim(), CreatedUtc = now });
        await NotifyAdmins(ticket.Id, "New support request", "A user requested help with account access.");
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return RedirectToAction(nameof(Thread), new { id = ticket.Id });
    }

    private async Task NotifyAdmins(long id, string title, string message)
    {
        foreach (var admin in await db.Admins.Where(a => !a.IsReadOnly).Select(a => a.UserId).ToListAsync())
            db.UserNotifications.Add(new UserNotification { UserId = admin, EventKey = $"support:{id}:{Guid.NewGuid():N}", Title = title, Message = message, Link = $"/Admin/SupportThread/{id}", CreatedUtc = clock.GetUtcNow().UtcDateTime });
    }

    [Authorize(AuthenticationSchemes = SupportAccessService.Scheme), HttpGet]
    public async Task<IActionResult> Thread(long id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId);
        if (ticket is null) return NotFound();
        await db.SupportTickets.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.UserReadUtc, clock.GetUtcNow().UtcDateTime));
        return View(new SupportThreadModel { Ticket = ticket, Messages = await db.SupportMessages.AsNoTracking().Where(m => m.TicketId == id && !m.Internal).OrderBy(m => m.Id).ToListAsync() });
    }

    [Authorize(AuthenticationSchemes = SupportAccessService.Scheme), HttpPost, EnableRateLimiting("support-message")]
    public async Task<IActionResult> Reply(long id, SupportMessageInput input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!; var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var owner = (await db.Users.FromSqlInterpolated($"SELECT * FROM AspNetUsers WHERE Id={userId} FOR UPDATE").ToListAsync()).SingleOrDefault();
        if (owner is null || owner.SecurityStamp != User.FindFirstValue("support:stamp")) return Forbid(SupportAccessService.Scheme);
        var ticket = (await db.SupportTickets.FromSqlInterpolated($"SELECT * FROM SupportTickets WHERE Id={id} AND UserId={userId} FOR UPDATE").ToListAsync()).SingleOrDefault();
        if (ticket is null) return NotFound();
        if (ticket.Status == "Resolved") {
            var open = await db.SupportTickets.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && t.Id != id && t.Status != "Resolved");
            if (open is not null) {
                TempData["SupportNotice"] = "You already have an open request. Add your follow-up to that conversation.";
                return RedirectToAction(nameof(Thread), new { id = open.Id });
            }
        }
        if (input.Version != ticket.Version) return Conflict("This conversation changed. Reload before replying.");
        var lastPublicByAdmin = await db.SupportMessages.AsNoTracking().Where(m => m.TicketId == id && !m.Internal)
            .OrderByDescending(m => m.Id).Select(m => m.ByAdmin).FirstOrDefaultAsync();
        if (!ticket.CanUserReply(lastPublicByAdmin)) return Conflict("Please wait for an administrator's public reply before sending another message.");
        if (await db.SupportMessages.CountAsync(m => m.AuthorId == userId && !m.ByAdmin && m.CreatedUtc >= now.AddMinutes(-1)) >= 2)
            ModelState.AddModelError("Body", "Please wait a minute before sending another message.");
        if (!ModelState.IsValid) {
            return View("Thread", new SupportThreadModel { Ticket = ticket, Input = input, Messages = await db.SupportMessages.AsNoTracking().Where(m => m.TicketId == id && !m.Internal).OrderBy(m => m.Id).ToListAsync() });
        }
        var name = await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).SingleAsync();
        db.SupportMessages.Add(new SupportMessage { TicketId = id, AuthorId = userId, AuthorName = name, Body = input.Body.Trim(), CreatedUtc = now });
        ticket.Status = "Awaiting admin"; ticket.UpdatedUtc = now; ticket.UserReadUtc = now; ticket.AdminReadUtc = null; ticket.Version = Guid.NewGuid().ToString("N");
        await NotifyAdmins(id, "Support request updated", "A user added a message to their account support request.");
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return RedirectToAction(nameof(Thread), new { id });
    }

    [Authorize(AuthenticationSchemes = SupportAccessService.Scheme), HttpPost]
    public async Task<IActionResult> Exit() { await HttpContext.SignOutAsync(SupportAccessService.Scheme); return RedirectToAction("Index", "Home"); }
}
