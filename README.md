# HackAcad Web

ASP.NET Core MVC (.NET 8), EF Core 8 and MySQL. This repository contains a shared project skeleton and the Admin / User Authentication module. Course authoring, CTF, simulations and badges belong to the other team modules.

## Run locally

Install a .NET SDK capable of building `net8.0`, the ASP.NET Core 8 runtime, and MySQL 8. Start your MySQL server.

Create `appsettings.Development.json` beside `HackAcad.Web.csproj`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=127.0.0.1;port=3306;database=hackacad_db;user=YOUR_USER;password=YOUR_PASSWORD;"
  }
}
```

This file is already ignored by Git. Keep local credentials out of commits. JSON quotation marks and backslashes inside passwords must be escaped. A local user with permission to create the database and apply migrations is needed during setup.

```powershell
dotnet restore
dotnet run
```

The checked-in launch profile selects Development and `http://localhost:5000`. Startup applies pending migrations and seeds the three development accounts below. Stop a previously running instance with Ctrl+C before rebuilding. The account-status migration adds `AspNetUsers.IsDisabled`, defaulting to `false` for existing users; it does not reset existing data.

## Shared development accounts

| Role | Email | Initial password |
| --- | --- | --- |
| Student | demo.student@hackacad.local | HackAcad@123! |
| Teacher | demo.teacher@hackacad.local | HackAcad@123! |
| Admin | demo.admin@hackacad.local | HackAcad@123! |

Each teammate gets these credentials in their own local database. Existing accounts, changed passwords and changed roles are preserved on restart. The original `admin@hackacad.local` account, if already present, is also preserved with its original password.

These fixtures are created **only in Development**. They are public test credentials and must not be used in a public deployment. Production admin provisioning and deployment configuration must be arranged before publishing.

## Implemented flows

- Public registration always creates an Identity user and a Student role marker in one transaction. Role fields posted by a visitor are ignored.
- Login / POST-only logout, remember-me cookies, unique email validation, server password policy, five-attempt / 15-minute login lockout, safe local return URLs and CSRF protection.
- Profile display-name updates, password changes requiring the old password, and an account dashboard reading the user's learning totals.
- Admin overview, account search / role / status filters with pagination, account creation / editing / disabling / deletion and Student-to-Teacher promotion.
- Admin moderation of course catalogue details and publication state. Course creation and lesson editing remain in the Course module.
- Account disabling and role/password changes revoke existing sessions on the next request. An Admin cannot remove their own Admin access, disable themselves or delete themselves.
- Teachers who own courses cannot be deleted or moved out of the Teacher role; disable them to retain their content. The owning module must provide course transfer before role removal can be allowed.
- Deleting a user removes their enrolments, associated progress, submissions and Identity records. It requires typing the email on a confirmation page.
- Course deletion is blocked when learning records exist; unpublish the course to preserve student work. Unused courses and their nested learning activities can be deleted.

Roles are resolved from `Students`, `Teachers` and `Admins`, **not** `AspNetRoles` / `AspNetUserRoles`. Other module controllers should use `[Authorize]` or `[Authorize(Roles = "Teacher")]` and still enforce ownership / enrolment checks on individual resources. Teacher users can retain enrolments because enrolments reference `AspNetUsers`.

The account pages retain `/Identity/Account/Login` and `/Identity/Account/Register`. Custom MVC views now implement these routes; the unused default Identity UI package has been removed. The main pages are `/Identity/Account/Dashboard`, `/Identity/Account/Profile`, `/Admin`, `/Admin/Users` and `/Admin/Courses`.

## Database changes as a team

InitialCreate and AddAccountStatus are source artifacts that must be shared through Git with the rest of this module. Teammates should pull and apply existing migrations, not generate their own second InitialCreate. Coordinate model changes in the shared `ApplicationDbContext` and add one migration per agreed schema change.

If using the command-line EF tools manually, match the project's version:

```powershell
dotnet tool install --global dotnet-ef --version 8.0.8
$env:DOTNET_ENVIRONMENT = "Development"
dotnet ef database update
```

## Verification

Run the HTTP checks against a Development instance backed by a dedicated test database. Set `ConnectionStrings__DefaultConnection` to that database's connection string in the server process environment before starting the instance. The test creates random temporary accounts and deletes only those accounts afterward.

```powershell
python tests/auth_smoke.py http://127.0.0.1:5097
```

Python uses only its standard library. Set `HACKACAD_DEMO_PASSWORD` if the seeded Admin password has been changed. Optional `TEST_COURSE_RETAINED_ID` and `TEST_COURSE_UNUSED_ID` enable moderation checks with fixtures in the dedicated database; the unused course is deleted by the check.

## Shared UI

Read the [team UI guide](UI_GUIDE.md) before building module pages. Start a Development instance and open `/Home/UiGuide` for shared component examples. This route returns 404 outside Development.

The Dashboard leads with role-aware tasks and real learning totals, alongside profile and password links. Admin overview links disabled-account and unpublished-course counts to existing filters. Course, outline and progress examples in the UI guide are illustrative; their module owners supply real data and working learning routes.

The public home page offers Account registration or login; signed-in users can open their Dashboard. Learning and badge authoring modules remain with their team owners.
