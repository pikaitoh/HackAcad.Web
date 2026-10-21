using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

public partial class AdminController
{
    [HttpGet]
    public async Task<IActionResult> SupportRequests(string? status, string? search, int page = 1, string? category = null)
    {
        if (status is not (null or "" or "Awaiting admin" or "Awaiting user" or "Resolved")) return BadRequest();
        if (!string.IsNullOrEmpty(category) && !SupportCategory.All.Contains(category) && category != "Account support") return BadRequest();
        var query = db.SupportTickets.AsNoTracking();
        if (!string.IsNullOrEmpty(category)) query = query.Where(t => t.Category == category);
        if (!string.IsNullOrEmpty(status)) query = query.Where(t => t.Status == status);
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(t => t.Subject.Contains(search) || db.Users.Any(u => u.Id == t.UserId && (u.DisplayName.Contains(search) || u.Email!.Contains(search))));
        var total = await query.CountAsync(); page = Math.Clamp(page, 1, Math.Max(1,(int)Math.Ceiling(total/20d)));
        var tickets = await query.OrderByDescending(t => t.UpdatedUtc).ThenByDescending(t => t.Id).Skip((page-1)*20).Take(20).ToListAsync();
        var ids = tickets.Select(t => t.UserId).ToList();
        return View(new SupportListModel { Status = status, Search = search, Category = category,
            DisabledUsers = (await db.Users.Where(u => ids.Contains(u.Id) && u.IsDisabled).Select(u => u.Id).ToListAsync()).ToHashSet(), Page = page, Total = total, Tickets = tickets,
            Users = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName) });
    }

    [HttpGet]
    public async Task<IActionResult> SupportThread(long id)
    {
        var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();
        await db.SupportTickets.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.AdminReadUtc, DateTime.UtcNow));
        return View(await AdminThreadData(ticket));
    }

    private async Task<SupportThreadModel> AdminThreadData(SupportTicket ticket, SupportMessageInput? input = null)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ticket.UserId);
        return new SupportThreadModel { Ticket = ticket, DisplayName = user.DisplayName, Email = user.Email ?? "", Disabled = user.IsDisabled, Input = input ?? new(),
            Messages = await db.SupportMessages.AsNoTracking().Where(m => m.TicketId == ticket.Id).OrderBy(m => m.Id).ToListAsync(),
            AccessChanges = await db.AdminActivities.AsNoTracking().Where(a => a.TargetType == "User" && a.TargetId == user.Id && (a.Action == "Disabled account" || a.Action == "Reactivated account")).OrderByDescending(a => a.Id).Take(10).ToListAsync() };
    }

    [HttpPost, EnableRateLimiting("support-message")]
    public async Task<IActionResult> SupportReply(long id, SupportMessageInput input, [FromServices] EmailVerification email)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var ticket = (await db.SupportTickets.FromSqlInterpolated($"SELECT * FROM `support_tickets` WHERE Id={id} FOR UPDATE").ToListAsync()).SingleOrDefault();
        if (ticket is null) return NotFound();
        if (input.Version != ticket.Version) return Conflict("This conversation changed. Reload before replying.");
        if (input.Status is not ("Awaiting admin" or "Awaiting user" or "Resolved")) ModelState.AddModelError("Status", "Choose a valid request status.");
        if (!ModelState.IsValid) return View("SupportThread", await AdminThreadData(ticket, input));
        var actorId = users.GetUserId(User)!; var actor = (await users.FindByIdAsync(actorId))!; var now = DateTime.UtcNow;
        var before = ticket.Status;
        db.SupportMessages.Add(new SupportMessage { TicketId = id, AuthorId = actorId, AuthorName = actor.DisplayName, ByAdmin = true,
            Internal = input.Internal, Body = input.Body.Trim(), CreatedUtc = now });
        ticket.Status = input.Internal ? before : input.Status!; ticket.UpdatedUtc = now; ticket.AdminReadUtc = now; ticket.Version = Guid.NewGuid().ToString("N");
        if (!input.Internal) {
            ticket.UserReadUtc = null;
            db.UserNotifications.Add(new UserNotification { UserId = ticket.UserId, EventKey = "support-reply:" + Guid.NewGuid().ToString("N"),
                Title = "Administrator replied to your support request", Message = $"Request #{id}: {ticket.Subject}", Link = "/Support", CreatedUtc = now });
        }
        records.AddActivity(actorId, input.Internal ? "Added support internal note" : "Replied to support request", "Support request", id.ToString(), ticket.Subject,
            input.Internal ? "Internal note saved; not visible to the user." : "Public reply saved; user notification requested.", before, ticket.Status);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        var delivered = true;
        if (!input.Internal) {
            var recipient = await db.Users.Where(u => u.Id == ticket.UserId).Select(u => u.Email).SingleAsync();
            var link = email.SupportLink("Support");
            delivered = recipient is not null && link is not null && await email.SendSupportAsync(recipient, $"Reply to support request #{id}", $"An administrator replied to your HackAcad support request #{id}.\n\nOpen account support to read and reply. If you are signed out, verify your current HackAcad email:\n{link}\n\nRequest status: {ticket.Status}. This message does not reactivate your account.");
        }
        TempData["Success"] = input.Internal ? "Internal note saved. It is not visible to the user." : "Reply saved in the user's support conversation.";
        if (!delivered) TempData["Error"] = "The reply is saved, but the email notification could not be sent. The user can still read it through account support.";
        return RedirectToAction(nameof(SupportThread), new { id });
    }
}
