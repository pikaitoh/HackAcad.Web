# HackAcad Web

ASP.NET Core MVC (.NET 8), EF Core 8 and MySQL. This repository contains a shared project skeleton and the Admin / User Authentication module. Course authoring, CTF, simulations and badges belong to the other team modules.

## Run locally

Install a .NET SDK capable of building `net8.0`, the ASP.NET Core 8 runtime, and MySQL 8. Start your MySQL server.

Create `appsettings.Development.json` beside `HackAcad.Web.csproj`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=127.0.0.1;port=3306;database=hackacad_db;user=YOUR_USER;password=YOUR_PASSWORD;"
  },
  "Email": {
    "PublicBaseUrl": "http://localhost:5000",
    "GmailAddress": "YOUR_HACKACAD_GMAIL@gmail.com",
    "AppPassword": "YOUR_GOOGLE_APP_PASSWORD"
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
- New accounts require email verification before login, including accounts created by Admin. Verification links expire after 10 minutes; invalid/expired links offer a resend action. Verification never signs the user in automatically.
- Login / POST-only logout, remember-me cookies, unique email validation, server password policy, five-attempt / 5-minute login lockout, safe local return URLs and CSRF protection.
- Profile username updates, password changes requiring the old password, and an account dashboard reading the user's learning totals.
- Admin overview, account search / role / status filters with pagination, account creation / editing / disabling / deletion and Student-to-Teacher promotion.
- Admin moderation of course catalogue details and publication state. Course creation and lesson editing remain in the Course module.
- Account disabling and role/password changes revoke existing sessions on the next request. An Admin cannot remove their own Admin access, disable themselves or delete themselves.
- Teachers who own courses cannot be deleted or moved out of the Teacher role; disable them to retain their content. The owning module must provide course transfer before role removal can be allowed.
- Deleting a user removes their enrolments, associated progress, submissions and Identity records. It requires typing the email on a confirmation page.
- Course deletion is blocked when learning records exist; unpublish the course to preserve student work. Unused courses and their nested learning activities can be deleted.

Roles are resolved from `Students`, `Teachers` and `Admins`, **not** `AspNetRoles` / `AspNetUserRoles`. Other module controllers should use `[Authorize]` or `[Authorize(Roles = "Teacher")]` and still enforce ownership / enrolment checks on individual resources. Teacher users can retain enrolments because enrolments reference `AspNetUsers`.

The account pages retain `/Identity/Account/Login` and `/Identity/Account/Register`. Custom MVC views now implement these routes; the unused default Identity UI package has been removed. The main pages are `/Identity/Account/Dashboard`, `/Identity/Account/Profile`, `/Admin`, `/Admin/Users` and `/Admin/Courses`.

## Gmail verification setup

Create a dedicated Gmail for HackAcad. Enable Google 2-Step Verification, then create an [App Password](https://myaccount.google.com/apppasswords) named HackAcad. Put that App Password (not the ordinary Gmail password) and sender address in the ignored `appsettings.Development.json` as shown above. Do not share credentials in chat or commit them. App Password availability depends on Google account settings; see [Google's setup instructions](https://support.google.com/accounts/answer/185833).

MailKit sends through `smtp.gmail.com:587` with required STARTTLS and normal certificate validation. `Email:PublicBaseUrl` determines the link destination and must match the app address, not the Gmail website. For a deployment, use its HTTPS URL and environment variables `Email__PublicBaseUrl`, `Email__GmailAddress`, and `Email__AppPassword`. HTTP loopback addresses are accepted only in Development. Links do not use the request's Host header. Teammates running locally need their own local mail configuration; a localhost verification link opens the app on the recipient's own computer.

Registration leaves the account pending when sending fails and explains how to request another email. Resend returns the same message for unknown, disabled, verified and pending accounts, sends nothing for the first three states, and has a 60-second cooldown per account/address. Register/resend POSTs share a limit of 30 requests per source IP per 10 minutes. These limits are in memory for this single-instance class project; coordinate shared limits and trusted proxy forwarding before scaling a deployment. Disabled and unverified accounts cannot use existing sessions. Admin changing an email resets verification and revokes sessions; changing only its letter case preserves verification. An Admin changing their own email is signed out. Users listed as Active may still need email verification; the Users list shows `Email not verified` separately.

The existing Identity `EmailConfirmed` column is reused; no new database migration is needed. Existing real accounts are not silently marked verified. They must use Resend verification email before logging in if currently unverified. The three seeded Development demo accounts are already verified and remain usable without mail; `.local` demo addresses cannot receive real internet email. Do not change a demo address to a real address unless you intend to verify it.

For automated local tests only, explicitly set `Email__DevelopmentPickupDirectory` to a private directory outside `wwwroot` and `Email__PublicBaseUrl` to the test server URL. The app writes `.eml` files there instead of contacting Gmail. This opt-in mail capture is ignored outside Development; there is no browser link that bypasses verification and no token logging. `.local-mail/` is ignored by Git if used as the capture directory. Keep captured messages private and delete them after testing. Confirmation uses the ASP.NET Core Identity token provider; it is not an OTP or Google sign-in integration.

## Changing your own email

Self-service email changes use Manage account → Email address → Change email. The current password is required (with existing lockout protection). A 10-minute Identity change-email token is sent to the proposed address; no email/login change happens until it is confirmed. Confirmation updates email, verification status and Identity login name in one transaction and invalidates existing sessions and old tokens. It never logs the user in automatically. Conflicts, expired/malformed/replayed tokens, disabled accounts and mail failure leave the stored address unchanged. Change requests have a separate limit of 10 POSTs per source IP per 10 minutes and allow one delivery per account per minute, across destinations. SMTP setup remains required for real delivery; local capture is for tests only. No migration is needed.

The user-facing name field is labelled Username. It keeps the existing 2–50-character profile-name rules and is stored in DisplayName; it is not a unique login identifier. Login still uses email. Identity's internal UserName remains the email address.

## Phone verification with Twilio Verify

Phone numbers are optional contact details under `/Identity/Account/Settings`. This verifies ownership; it does **not** enable phone login, password recovery or SMS two-factor authentication. The existing verified number stays unchanged until a new number is verified. Phone verification/removal revokes other sessions and refreshes the current session. Checks are bound to the signed-in account, its security stamp and pending phone; posted phone numbers cannot redirect a confirmation.

1. Create a [Twilio account](https://www.twilio.com/try-twilio) and open Verify. Follow [Try out Verify](https://www.twilio.com/docs/usage/trials/try-out-verify) and create/use a Verify Service with **six-digit codes** and SMS enabled. Trial accounts send only to pre-verified recipients, have country restrictions, and expire after 30 days. Upgrading may be required for other recipients. Check trial terms in the Console before spending money.
2. Enable Malaysia in **Verify → Settings → Geo permissions** for Malaysian numbers. Verify has its own permissions; configuring Programmable Messaging alone is insufficient. See [Verify Geo permissions](https://www.twilio.com/docs/verify/preventing-toll-fraud/verify-geo-permissions) and [Malaysia SMS guidelines](https://www.twilio.com/en-us/guidelines/my/sms). API acceptance does not guarantee carrier delivery.
3. Copy Account SID (`AC…`), Auth Token and Verify Service SID (`VA…`) privately. Merge this section into the ignored `appsettings.Development.json`; keep the existing ConnectionStrings/Email sections and never put secrets in tracked `appsettings.json`:

```json
"Sms": {
  "TwilioAccountSid": "YOUR_ACCOUNT_SID",
  "TwilioAuthToken": "YOUR_AUTH_TOKEN",
  "TwilioVerifyServiceSid": "YOUR_VERIFY_SERVICE_SID"
}
```

For deployment use environment variables `Sms__TwilioAccountSid`, `Sms__TwilioAuthToken` and `Sms__TwilioVerifyServiceSid`. Never send an Auth Token in chat or commit it. Verify charges a verification fee **plus delivery fees**; check [current pricing](https://www.twilio.com/en-us/verify/pricing) and Console limits before real SMS. The code uses Verify's fixed HTTPS endpoints and needs no Twilio SDK or sending-number setting. Account/region rules still apply.

Phone format is E.164: `+` followed by 8–15 digits without spaces, e.g. `+60123456789`. Format does not prove that a number is assigned/mobile; OTP proves possession. The app allows one send per account per minute, five sends per account and per destination per hour, 15 send POSTs per IP per 10 minutes, and five code checks per pending request (plus 30 check POSTs per IP per 10 minutes). Twilio applies additional limits. Codes normally expire after 10 minutes; Twilio may resend the same code while pending. Approved/expired/exhausted requests cannot be reused locally.

Pending requests and app limits use in-process memory for this single-instance assignment. Restarting loses pending requests; request another SMS. Use shared state/limits before scaling. Delivery failures and missing credentials never confirm or replace a phone; pages and logs do not expose codes or credentials.

For automated tests only, explicitly set `Sms__DevelopmentPickupDirectory` to a private directory **outside wwwroot**. Development captures random test OTPs as JSON instead of contacting Twilio; the Phone number page labels test mode. The setting is ignored outside Development, and `.local-sms/` is ignored by Git if used. Remove the pickup setting for real Twilio. Do not run smoke tests against a real SMS sender: set `HACKACAD_TEST_SMS_DIR` to the same private capture directory in the test process. Tests use disposable fictional numbers and never send paid SMS.

## Personal account security

Manage account → Email address shows the signed-in user's actual email verification status. Registered but unverified accounts exist while awaiting confirmation; Admin-created/changed addresses also await confirmation. Development demo fixtures are preverified for local testing.

`LoginHistory` records known-account password failures/lockouts and completed sign-ins, including second-factor outcomes. It is personal, not a system-wide log. `Sessions` combines active browser sessions, approximate browser/OS, IP, creation/last-activity time, per-other-session Sign out and Sign out all other devices. Both sign-out actions require the current password and CSRF protection, are scoped to the signed-in user, and retain this device. Bulk sign-out also invalidates pending 2FA challenges. Active means the login remains valid, not that the browser is online. Closing a browser may leave a row until its cookie expires; forgotten/stamp-invalid/revoked rows are excluded. Login history and device/activity timestamps display Malaysia time (UTC+8); stored timestamps stay UTC. Existing Joined date views retain their explicitly labelled UTC format.

Admin can enable optional, per-account Authenticator TOTP under Manage account → Two-factor authentication. Setup requires the current password, a locally rendered QR/manual key and a valid app code. The QR never goes to an external service. Setup authorization lasts 10 minutes. No Gmail App Password or paid SMS is needed. Initial setup is Admin-only; someone with already-enabled 2FA can still manage it after a role change. It is not automatically enabled for all users. Login then requires the password plus an app code or one unused recovery code. Pending sign-in challenges last 5 minutes and are bound to the current security stamp. Recovery codes are displayed only when generated. Regeneration/disable requires the password plus a current app code or unused recovery code and revokes other logins. Five failed password/code attempts lock access for 5 minutes; security-action POSTs additionally have a 20/IP/10-minute limit and 2FA login POSTs 30/IP/10 minutes. Limits and pending setup grants use memory on this single-instance assignment.

`/Admin/Activity` shows only the signed-in Admin's successful account creation/update/deletion and course moderation/deletion actions recorded from this feature onward. Audit writes share the mutation's transaction. Failed actions are not presented as successful activity. Target snapshots survive deletion; deleting an actor clears its FK while retaining the audit row. Passwords, OTPs, authenticator secrets and recovery codes are never included in the audit. User deletion cascades personal login/session records. Establish retention rules before a public deployment.

`AddAccountSecurityRecords` adds `LoginRecords`, `AccountSessions` and `AdminActivities`. Startup applies migrations with the existing initializer. Cookies issued before session tracking was added require a fresh login once. Pull the migration with the code; do not generate a duplicate.

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
$env:HACKACAD_TEST_MAIL_DIR = "C:\path\to\private-test-mail"
$env:HACKACAD_TEST_SMS_DIR = "C:\path\to\private-test-sms"
python tests/auth_smoke.py http://127.0.0.1:5097
# Restart the isolated server before this separate security run:
python tests/security_smoke.py http://127.0.0.1:5097
```

The server must use Development mail capture in that same directory, with `Email__PublicBaseUrl` matching the test URL. Restart the isolated server before a fresh test run to reset in-memory request limits. Python uses only its standard library. Set `HACKACAD_DEMO_PASSWORD` if the seeded Admin password has been changed. Optional `TEST_COURSE_RETAINED_ID` and `TEST_COURSE_UNUSED_ID` enable moderation checks with fixtures in the dedicated database; the unused course is deleted by the check.

## Shared UI

Signed-in navigation uses a profile-picture dropdown with View profile, Manage account and POST-only Log out. View profile contains the picture, username, optional full name (up to 100 characters), role and joined date. Full name saves with the existing profile form; it is independent of the email used to log in. `AddProfileFullName` adds one nullable `AspNetUsers.FullName` column, preserving existing accounts. The shared header uses deep teal; the avatar menu remains the Profile entry point, without a duplicate sidebar button. Manage account uses `_ManageAccountLayout` and `_ManageAccountNav` to switch between Email address, Phone number, Password, eligible Two-factor authentication, Login history and Devices & sessions. The sidebar becomes a horizontally scrollable function menu on mobile; the selected page is marked with `aria-current="page"`. My admin activity stays in the Administration sidebar. Phone verification forms and their POST results belong to `/Identity/Account/PhoneNumber`; `/Identity/Account/Settings` is the email settings entry. It stays visible on mobile. There are no links to unfinished Badge/Saved/Rewards modules. Without a picture, the username initial is used. Pictures are private to the signed-in user: uploads accept JPEG/PNG/WebP up to 2 MB and 4,096 × 4,096 pixels, decode/crop to 256 × 256 JPEG with SkiaSharp, discard source metadata/filenames, and save in `AspNetUsers.AvatarImage`. AddProfileAvatar adds only that nullable column. Identity already supplies the phone fields. Linux native assets are included alongside the Windows assets used locally.

Read the [team UI guide](UI_GUIDE.md) before building module pages. Start a Development instance and open `/Home/UiGuide` for shared component examples. This route returns 404 outside Development.

The Dashboard leads with role-aware tasks and real learning totals, alongside profile and password links. Admin overview links disabled-account and unpublished-course counts to existing filters. Course, outline and progress examples in the UI guide are illustrative; their module owners supply real data and working learning routes.

The public home page offers Account registration or login; signed-in users can open their Dashboard. Learning and badge authoring modules remain with their team owners.


Admin navigation keeps Overview, Users, Courses, Login logs and My activity on separate routes. `/Admin/LoginLogs` shows known accounts' recorded sign-in attempts with username/email search, result and inclusive Malaysia-date filters, and 20 rows per page. It is Admin-only; personal Login history remains in Manage account. Unknown-email attempts are not stored, and deleting an account also deletes its login records. Device labels are inferred from the browser header, not verified device identities.

Users shows Last login, derived from the latest completed successful sign-in (including completed 2FA/recovery sign-ins). Edit account and your Password page show both Last login and Last password changed with seconds, in Malaysia time (UTC+8); stored timestamps remain UTC. Failed attempts and unfinished 2FA do not advance Last login. `AddPasswordChangedTimestamp` adds nullable `AspNetUsers.LastPasswordChangedUtc`; it is saved atomically with successful password changes. Older unknown password-change times display No record. These read-only values cannot be set by form submissions.
