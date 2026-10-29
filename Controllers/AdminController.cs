using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

// ADMIN - Shared Admin controller identity and dependencies. Feature actions live in the Admin*.cs partial files.

[Authorize(Roles = AccountRoles.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class AdminController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    UserAccounts accounts, AccountRecords records, UserTimeZone userTime, Personalization personalization) : Controller
{
}
