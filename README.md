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
    "GmailApi": { "RefreshToken": "SAVED_BY_THE_LOCAL_SETUP_TOOL" }
  }
}
```

This file is already ignored by Git. Keep local credentials out of commits. JSON quotation marks and backslashes inside passwords must be escaped. A local user with permission to create the database and apply migrations is needed during setup.

```powershell
dotnet restore
dotnet run
```

The checked-in launch profile selects Development and `http://localhost:5000`. Startup applies pending migrations and seeds the three development accounts below. Stop a previously running instance with Ctrl+C before rebuilding. The account-status migration adds `users.IsDisabled`, defaulting to `false` for existing users; it does not reset existing data.

## Shared development accounts

| Role | Email | Initial password |
| --- | --- | --- |
| Student | demo.student@hackacad.local | HackAcad@123! |
| Instructor | demo.instructor@hackacad.local | HackAcad@123! |
| Admin | demo.admin@hackacad.local | HackAcad@123! |

Each teammate gets these credentials in their own local database. Existing accounts, changed passwords and changed roles are preserved on restart. The original `admin@hackacad.local` account, if already present, is also preserved with its original password.

These fixtures are created **only in Development**. They are public test credentials and must not be used in a public deployment. Production admin provisioning and deployment configuration must be arranged before publishing.

## Implemented flows

- Public registration always creates an Identity user and a Student role marker in one transaction. Role fields posted by a visitor are ignored.
- New accounts require email verification before login, for email registration. Verification links expire after 10 minutes; invalid/expired links offer a resend action. Verification never signs the user in automatically.
- Login / POST-only logout, remember-me cookies, unique email validation, server password policy, five-attempt / 5-minute login lockout, safe local return URLs and CSRF protection.
- Profile username updates, password changes requiring the old password, and an account dashboard reading the user's learning totals.
- Admin overview, account search / role / status filters with pagination, read-only user details and reasoned account disable/reactivate actions. Instructor promotion is available only through the application review workflow.
- Admin moderation of course catalogue details and publication state. Course creation and lesson editing remain in the Course module.
- Account disabling and role/password changes revoke existing sessions on the next request. An Admin cannot disable their own access. Existing sessions are explicitly revoked on access changes.
- Users and their personal details cannot be created, edited or deleted through Admin endpoints. Instructor access changes preserve existing learning records and courses.
- Disable/reactivate requires a reason, records the actor and action in the audit log, and preserves account and learning data.
- Course deletion is blocked when learning records exist; unpublish the course to preserve student work. Unused courses and their nested learning activities can be deleted.

Roles are resolved from `students`, `instructors` and `admins`, **not** `AspNetRoles` / `AspNetUserRoles`. Other module controllers should use `[Authorize]` or `[Authorize(Roles = "Instructor")]` and still enforce ownership / enrolment checks on individual resources. Instructor users can retain enrolments because enrolments reference `users`.

The account pages retain `/Identity/Account/Login` and `/Identity/Account/Register`. Custom MVC views now implement these routes; the unused default Identity UI package has been removed. The main pages are `/Identity/Account/Dashboard`, `/Identity/Account/Profile`, `/Admin`, `/Admin/Users` and `/Admin/Courses`.

## Gmail verification setup

Verification mail is delivered through the [Gmail API](https://developers.google.com/workspace/gmail/api/guides/sending), using a dedicated HackAcad sender. Recipients may use Gmail, Yahoo, Outlook, school or company email. No Gmail App Password or SMTP connection is used. Identity still generates and checks the 10-minute verification tokens; Google sends the mail only.

One-time local setup (from the repository folder):

1. In the existing HackAcad Google Cloud project, open **APIs & Services → Library → Gmail API → Enable**.
2. In **Google Auth Platform → Data Access**, add `https://www.googleapis.com/auth/gmail.send`. Under **Audience**, add the HackAcad sender Gmail as a test user if the app is in Testing. Only the sender grants Gmail access; recipients do not need to be test users for email delivery. Keep normal Google sign-in scopes unchanged in code.
3. Open **Google Auth Platform → Clients**, select the existing Web client and add `http://localhost:53682/` under **Authorized redirect URIs**. Keep the existing `/signin-google` URI for normal login.
4. Ensure `Email:GmailAddress` matches the sender you will choose. The tool reuses `Authentication:Google:ClientId` and `ClientSecret`, or optional separate `Email:GmailApi:ClientId` and `ClientSecret`. Credentials remain in the ignored local file.
5. Run `dotnet run --project tools/GmailSetup`, choose the HackAcad sender in the browser and grant sending permission. The tool uses state and PKCE, checks the selected verified sender, and saves the refresh token directly into ignored `appsettings.Development.json`. It never prints the token or sends a test email. Authorization expires after five minutes if unfinished. Restart the web app afterward.

`dotnet run --project tools/GmailSetup -- --check` checks local configuration without network access. If Google reports a redirect mismatch, check the exact URI above, including its trailing slash. Missing send permission, denied consent, wrong sender, invalid state and token failures do not save a new authorization. If access is revoked or expires, rerun the tool; the app leaves affected accounts unverified and permits retry after a delivery failure.

Google issues refresh tokens with a **7-day lifetime while an external OAuth app is in Testing** when Gmail scopes are requested. Reauthorize for continued development; arrange appropriate production publishing/verification before deployment. See [Google's refresh-token rules](https://developers.google.com/identity/protocols/oauth2#expiration) and [Gmail send scope](https://developers.google.com/workspace/gmail/api/auth/scopes). Google may still request its own account security checks during authorization; using OAuth does not bypass them.

`Email:PublicBaseUrl` is the web app origin, not the Gmail website. Deployment uses HTTPS and secret environment variables `Email__PublicBaseUrl`, `Email__GmailAddress`, `Email__GmailApi__RefreshToken`, plus the OAuth client ID/secret. Tokens must match their OAuth client. HTTP loopback origins are accepted only in Development. Verification links never use the request Host header. A localhost link opens the app on the recipient's own computer; teammates need their own local settings or a shared deployed URL.

Registration leaves the account pending when sending fails and explains how to request another email. Registration shows the saved recipient with a direct Resend verification email button; no email lookup form or resend link is shown on Login. The recipient comes from protected browser context, never a posted address. A correct password for an unverified account recovers the pending page without signing in; failed passwords still count toward lockout. Missing browser context returns to Login. Disabled/verified accounts receive no new verification mail. Delivery has a 60-second cooldown per account/address. Register/resend POSTs share a limit of 30 requests per source IP per 10 minutes. These limits are in memory for this single-instance class project; coordinate shared limits and trusted proxy forwarding before scaling a deployment. Disabled and unverified accounts cannot use existing sessions. Admin changing an email resets verification and revokes sessions; changing only its letter case preserves verification. An Admin changing their own email is signed out. Users listed as Active may still need email verification; the Users list shows `Email not verified` separately.

The existing Identity `EmailConfirmed` column is reused; no new database migration is needed. Existing real accounts are not silently marked verified. If currently unverified, they must finish the email verification flow before login succeeds. The three seeded Development demo accounts are already verified and remain usable without mail; `.local` demo addresses cannot receive real internet email. Do not change a demo address to a real address unless you intend to verify it.

For automated local tests only, explicitly set `Email__DevelopmentPickupDirectory` to a private directory outside `wwwroot` and `Email__PublicBaseUrl` to the test server URL. The app writes `.eml` files there instead of contacting Gmail. This opt-in mail capture is ignored outside Development; there is no browser link that bypasses verification and no token logging. `.local-mail/` is ignored by Git if used as the capture directory. Keep captured messages private and delete them after testing. Confirmation uses the ASP.NET Core Identity token provider; it is not an OTP or Google sign-in integration.

## Changing your own email

Self-service email changes use Manage account → Email address → Change email. The current password is required (with existing lockout protection). A 10-minute Identity change-email token is sent to the proposed address; no email/login change happens until it is confirmed. Confirmation updates email, verification status and Identity login name in one transaction and invalidates existing sessions and old tokens. It never logs the user in automatically. Conflicts, expired/malformed/replayed tokens, disabled accounts and mail failure leave the stored address unchanged. Change requests have a separate limit of 10 POSTs per source IP per 10 minutes and allow one delivery per account per minute, across destinations. Gmail API sender authorization remains required for real delivery; local capture is for tests only. No migration is needed.

The user-facing name field is labelled Username. It keeps the existing 2–50-character profile-name rules and is stored in DisplayName; it is not a unique login identifier. Login still uses email. Identity's internal UserName remains the email address.

## Phone verification with Twilio Verify

Phone numbers are optional under Manage account → Phone number. Once verified, a unique number can identify an account for **phone + password login** and optional **SMS two-factor authentication**. Password recovery uses the mandatory account email. The existing verified number stays unchanged until a new number is verified. Phone verification/removal revokes other sessions and refreshes the current session. Challenges are one-use and bound to the account, security stamp and purpose; SMS login requests also have a protected browser request binding. Posted recipient/account fields cannot redirect a resend or reset. Adding/removing a phone requires the current password. A unique nullable database index prevents one number belonging to two accounts.

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

`LoginHistory` records known-account password failures/lockouts and completed sign-ins, including second-factor outcomes. It is personal, not a system-wide log. `Sessions` combines active browser sessions, approximate browser/OS, IP, creation/last-activity time, per-other-session Sign out and Sign out all other devices. Both sign-out actions require the current password and CSRF protection, are scoped to the signed-in user, and retain this device. Bulk sign-out also invalidates pending 2FA challenges. Active means the login remains valid, not that the browser is online. Closing a browser may leave a row until its cookie expires; forgotten/stamp-invalid/revoked rows are excluded. All displayed dates and times use the signed-in viewer’s saved time zone; stored timestamps stay UTC.

Student, Instructor and Admin can enable optional, per-account Authenticator TOTP under Manage account → Two-factor authentication. Setup requires the current password, a locally rendered QR/manual key and a valid app code. The QR never goes to an external service. Setup authorization lasts 10 minutes. No Gmail App Password or paid SMS is needed. The shared account sidebar offers setup for all three roles. Each user enables it for their own account; role changes do not disable it. Google-only accounts must first set a HackAcad password in Password settings. It is not automatically enabled for users. Login then requires the password plus an app code or one unused recovery code. Pending sign-in challenges last 5 minutes and are bound to the current security stamp. Recovery codes are displayed only when generated. Regeneration/disable requires the password plus a current app code or unused recovery code and revokes other logins. Five failed password/code attempts lock access for 5 minutes; security-action POSTs additionally have a 20/IP/10-minute limit and 2FA login POSTs 30/IP/10 minutes. Limits and pending setup grants use memory on this single-instance assignment.

`/Admin/Activity` shows only the signed-in Admin's successful account creation/update/deletion and course moderation/deletion actions recorded from this feature onward. Audit writes share the mutation's transaction. Failed actions are not presented as successful activity. Target snapshots survive deletion; deleting an actor clears its FK while retaining the audit row. Passwords, OTPs, authenticator secrets and recovery codes are never included in the audit. User deletion cascades personal login/session records. Establish retention rules before a public deployment.

`AddAccountSecurityRecords` adds `login_records`, `account_sessions` and `admin_activities`. Startup applies migrations with the existing initializer. Cookies issued before session tracking was added require a fresh login once. Pull the migration with the code; do not generate a duplicate.

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

Signed-in navigation uses a profile-picture dropdown with View profile, Manage account and POST-only Log out. Student accounts also have a Saved courses entry in this menu. View profile contains the picture, username, optional full name (up to 100 characters), role and joined date. Full name saves with the existing profile form; it is independent of the email used to log in. `AddProfileFullName` adds one nullable `users.FullName` column, preserving existing accounts. The shared header uses deep teal; the avatar menu remains the Profile entry point, without a duplicate sidebar button. Manage account uses `_ManageAccountLayout` and `_ManageAccountNav` to switch between Email address, Phone number, Password, eligible Two-factor authentication, Login history and Devices & sessions. The sidebar remains beside the content; the selected page is marked with `aria-current="page"`. My admin activity stays in the Administration sidebar. Phone verification forms and failed POST results belong to `/Identity/Account/ChangePhone`; `/Identity/Account/PhoneNumber` is the summary; `/Identity/Account/Settings` is the email settings entry. Without a picture, the username initial is used. Pictures are private to the signed-in user: uploads accept JPEG/PNG/WebP up to 2 MB and 4,096 × 4,096 pixels, decode/crop to 256 × 256 JPEG with SkiaSharp, discard source metadata/filenames, and save in `users.AvatarImage`. AddProfileAvatar adds only that nullable column. Identity already supplies the phone fields. Linux native assets are included alongside the Windows assets used locally.

Read the [team UI guide](UI_GUIDE.md) before building module pages. Start a Development instance and open `/Home/UiGuide` for shared component examples. This route returns 404 outside Development.

The Dashboard leads with role-aware tasks and real learning totals, alongside profile and password links. Admin overview links disabled-account and unpublished-course counts to existing filters. Course, outline and progress examples in the UI guide are illustrative; their module owners supply real data and working learning routes.

The public home page offers Account registration or login; signed-in users can open their Dashboard. Learning and badge authoring modules remain with their team owners.


Admin navigation keeps Overview, Users, Courses, Login logs and My activity on separate routes. `/Admin/LoginLogs` shows known accounts' recorded sign-in attempts with username/email search, result and inclusive date filters in the viewer’s saved time zone, and 20 rows per page. It is Admin-only; personal Login history remains in Manage account. The known-account Login logs retain the existing `login_records` view. Separate security-monitor events also count failed unknown-identifier attempts using SHA-256 identifier hashes for repeated-failure and password-spraying signals; raw unknown addresses are not retained. Deleting an account deletes its personal login records. Device labels are inferred from the browser header, not verified device identities.

Users shows Last login, derived from the latest completed successful sign-in (including completed 2FA/recovery sign-ins). Edit account and your Password page show both Last login and Last password changed with seconds, in the viewer’s saved time zone; stored timestamps remain UTC. Failed attempts and unfinished 2FA do not advance Last login. `AddPasswordChangedTimestamp` adds nullable `users.LastPasswordChangedUtc`; it is saved atomically with successful password changes. Older unknown password-change times display No record. These read-only values cannot be set by form submissions.


Google sign-in uses the ASP.NET Core Google handler (PKCE, anti-forgery-protected POST initiation, 10-minute external cookie, no stored Google access tokens). Set `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` in ignored local settings or deployment environment variables. Register `http://localhost:5000/signin-google` for the current local app; deployed origins need their own exact HTTPS callback URI. Google buttons are available on Log in and Register only when both credentials are configured. These OAuth credentials are separate from Gmail SMTP/App Password settings.

A new Google user chooses a validated Username and receives only the Student role. Google-verified Gmail/Workspace email can be confirmed directly; other domains still require HackAcad's mailbox verification. Existing accounts are never automatically merged by email: sign in normally and connect the matching Google account under Manage account → Email address → Connected accounts. Linked sign-ins preserve the existing role, enforce disabled/lockout/email/2FA checks, and use the existing session and history tracking. External-only users can set their first local password within five minutes of provider authentication; otherwise Password settings requests confirmation from a configured, linked provider again. The local password is required for existing password-protected security actions. Existing Google bindings identify the Google subject, independently of later local email changes.

`tests/GoogleFlow` verifies OAuth redirects, correlation/state, account creation and binding, password setup, role boundaries, sessions/history and Admin 2FA using a test-only Google HTTP backchannel. It refuses to run unless `ConnectionStrings__DefaultConnection` targets `hackacad_uiresearch_20261016`; use Development mail capture and fake OAuth client values. No real Google sign-in or messages are sent. Run `dotnet run --project tests/GoogleFlow -c UiResearch` from the repository with the isolated environment configured.


UI scope: desktop web only. Keep full navigation, sidebars and desktop columns; do not add small-screen breakpoints, collapsed phone navigation or mobile-specific styling/testing.


### Phone login, email recovery and SMS 2FA

- Log in accepts an email address or an E.164 verified number, together with the local password. Unverified numbers, disabled accounts and normal password lockouts remain blocked. Email verification requirements are unchanged.
- Forgot password accepts the account email, including accounts without a phone. The existing Gmail sender sends a purpose-bound Identity reset link valid for 10 minutes, with a one-minute delivery cooldown. Existing and unknown accounts receive the same generic confirmation. Opening a valid link places the proof in protected HttpOnly TempData, then redirects to a clean reset URL; no account, recipient or token is accepted from posted form fields. Reset applies the normal password policy, records Last password changed, clears password lockout, revokes all sessions and returns to login. Existing 2FA remains enabled. Used, expired, changed-email, disabled-account and wrong-purpose links cannot reset a password. Google-only accounts without a local password continue using Google. SMS password-recovery endpoints have been removed.
- Student, Instructor and Admin can choose Authenticator or SMS in Manage account → Two-factor authentication. SMS setup needs the current password and a fresh SMS confirmation, then issues ten single-use recovery codes. One method is active at a time; changing method requires turning off the existing method with the password and authenticator/SMS/recovery proof. SMS protects password and linked-Google login. Merely verifying a contact number does not turn on SMS 2FA.
- An active SMS second factor prevents changing/removing its number. Security actions require password plus SMS or an unused recovery code. Setup, login and settings SMS requests share the same send limits and Twilio allowance. Use recovery codes if SMS is unavailable; SMS resend never runs automatically.
- `AddPhoneAuthentication` adds `SmsTwoFactorEnabled` and a unique, nullable E.164 phone index. Existing valid numbers and authenticator preferences are preserved; a legacy duplicate/overlong phone must be corrected before applying the migration rather than silently assigning or deleting it.
- `tests/PhoneAuth` exercises phone login and SMS setup/login for every role, plus email recovery without a phone, ownership/CSRF, unique numbers, cooldown, invalid codes, purpose/expiry/replay and session invalidation. Run only with the isolated database and Development SMS/email capture. Never run authentication tests with a live sender.


## Time zone preferences

Manage account → Time zone is available to Student, Instructor and Admin. The selected IANA time zone is stored in `users.TimeZoneId` by the `AddUserTimeZone` migration and defaults to `Asia/Kuala_Lumpur` for existing and new accounts. Each authenticated request loads the viewer’s preference, so it applies on other devices without logging in again. `UserTimeZone` is injected into Razor views as `UserTime`; use `UserTime.Format(utc)` for future date/time displays, never fixed UTC offsets or the server’s local clock.

Profile Joined dates, Admin user dates, password timestamps, personal login history, Admin login logs/activity and device session timestamps all follow the viewer’s preference. Admin sees all users’ records in the Admin’s own selected zone. The active zone is labelled on dated pages. Login-log From/To dates include whole local calendar days; the query uses UTC boundaries and an exclusive next-day end, including 23/25-hour daylight-saving days. Invalid zone submissions are rejected; invalid stored values fall back to Malaysia. Changing a display preference never shifts stored events or changes OTP/token/session expiration deadlines.
`tests/TimeZones` verifies all three roles, persistence across signed-in devices, local-day filters, DST boundaries, validation and UTC storage. Run `dotnet run --project tests/TimeZones -c UiResearch` with the isolated `hackacad_uiresearch_20261016` database and both local email/SMS capture variables; the runner refuses a live database.


## Course integration safeguards

Enrollment, learning and progress/flag/simulation submissions require the Student role. Admin catalogue/detail URLs redirect to course moderation; Instructor catalogue/Dashboard URLs open Instructor Studio. Instructors may preview course details without enrollment controls or personal progress. Existing non-student enrollment records are preserved but cannot bypass these role checks. Course visibility and owner/Admin content-review checks still apply to details and file downloads. Learning GET requests do not create enrollments; users enroll through the CSRF-protected action. Unpublishing a course blocks subsequent student access even if an enrollment exists.

Challenge uploads are limited to 10 MB and stored under ignored `App_Data/ctf/`, outside the public web root. The download endpoint resolves only server-generated attachment paths and checks course/activity access. Existing seed attachments under `wwwroot/uploads/ctf` remain downloadable through that endpoint; direct static URLs are blocked. Existing attachment paths are loaded from the owned database record, never a posted hidden field.

Simulation configurations must be valid bounded JSON with a filesystem, starting directory and supported objectives. Configuration JSON is HTML-safely serialized; terminal input, filenames, objective labels and output are escaped. Completion submits at most 256 virtual commands; `SimulationVerifier` replays them against the stored virtual filesystem and checks cwd/cat/chmod/command objectives, including the configured chmod mode (default 755). It never executes operating-system commands or makes network requests. This validates a satisfying command sequence, not a person's physical keystrokes. Only a server-confirmed result displays saved success and any reward.

Progress and course completion status are saved in one transaction, including the final activity. Students can acknowledge a completed course once; the completion timestamp prevents the Finish Course button from returning when they review it later. Dashboard resume cards select an incomplete enrollment, and completed courses offer a browse-courses prompt. Students can save or remove published courses in the catalogue and course details, then revisit them from `/Courses/Saved`; these records are unique per student/course and persist in `saved_courses`. Instructor deletions preserve content referenced by student learning records and show a clear message instead of an unhandled database error. Dashboard, course and Instructor timestamps use the viewer's saved time zone.

`tests/CourseSecurity` covers these access, upload, injection, simulation, deletion, completion and timezone cases. It requires the separate `hackacad_mergereview_20261019` database plus both local email/SMS capture variables. Run `dotnet run --project tests/CourseSecurity -c MergeReview` with that isolated environment. `node tests/simulation_terminal.js` checks the real terminal script's escaping, command validation and submission behavior without a browser or real shell.


## Guest homepage

Guest navigation offers Home, How it works, For Education, Log in and Create account. Course catalogue and detail routes require authentication; role routing sends Admin to moderation and Instructor to Instructor Studio. The public homepage introduces learning formats without exposing course records. Its desktop composition draws on TryHackMe's left-aligned hero and practical-learning presentation while keeping HackAcad branding, factual features and account flows. The Google action uses the existing CSRF-protected GoogleLogin endpoint and appears only when configured. No external tracking or design dependencies were added.


## Instructor applications and account governance

Public registration (including Google registration) always creates Student access. `/Education` is public; only the Student avatar menu links to the same page; Guest header navigation also exposes it. Student-only `/Education/Apply` accepts a name, professional email, teaching topics, 1–10 dated teaching roles and one private PDF evidence pack (up to 5 MB, PDF signature checked). Qualifications, project links and teaching samples are optional. Relevant teaching must cover at least five years; intervals are merged so overlapping roles cannot inflate eligibility, current roles end at submission, and future/reversed dates are rejected. The five-year threshold uses the calendar anniversary on the submission date; administrators must separately verify the claimed history.

Each submission/reconsideration is a new numbered evidence revision. Only one pending/latest application is accepted per user; duplicate revisions are blocked by a database unique index. Status is Pending, Needs more information, Approved or Rejected, independently of Active/Disabled account access and email verification. Applicant and Admin may download evidence only via an authenticated, owner-checked attachment action. Evidence stays outside static assets in a private database blob, including earlier versions. It is not emailed or exposed as a public upload URL.

Admin → Instructor applications shows the latest revision per applicant. All decisions need an eligibility category and a specific public explanation. Approval additionally requires all four eligibility confirmations, five or more verified years, and private verification-source notes. Notes are visible only to administrators; applicants see reasons and revision history. Optional qualifications never become mandatory approval criteria. The server enforces these rules, self-review is blocked, closed decisions cannot be overwritten, and concurrency checks prevent a second reviewer from changing the same decision. Approval atomically replaces the Student marker with Instructor, appends a review/audit record, refreshes the security stamp and revokes old sessions. The applicant logs in again; profile and learning data are retained. A rejected or incomplete application can be resubmitted with new evidence. Verification is manual: checkboxes do not independently prove institutional identity or teaching history.

Admin users are read-only except explicit disable/reactivate actions with reasons. Removed CreateUser/EditUser/DeleteUser routes return 404. No arbitrary role picker or Admin password-reset action remains. Audit logs displays all administrators' actions, with search/pagination and an Only my actions filter. Reviews and audit records have no edit/delete routes. The old `/Admin/Activity` bookmark redirects to the personal audit filter. Administrative database operators remain outside application-level immutability guarantees.

Student, Instructor and Admin workspaces share a full-width desktop sidebar layout; The sticky header keeps the brand, appearance selector, notification bell and avatar/name; personal pages also expose the role-specific workspace return link. Personal Profile/Manage account retain their own navigation. Guest pages retain header navigation. Instructor replaces Teacher in current entities, controllers, routes, views, role labels and documentation. `InstructorApplications` migration renames the existing Teachers table and Courses.TeacherId column without deleting role markers or ownership. Earlier migration files are intentionally unchanged. Existing account emails are not renamed; an existing legacy demo instructor login continues to work, while fresh databases use demo.instructor@hackacad.local.

Verification: `tests/InstructorWorkflow` covers eligibility dates, duplicate submissions, evidence ownership, removed Admin mutations, reason/checklist enforcement, private/public review content, reconsideration, role/session changes and role sidebars. Run only with `hackacad_mergereview_20261019` and local email/SMS capture variables. Existing CourseSecurity, GoogleFlow, PhoneAuth, TimeZones and TwoFactorRoles suites use Instructor names. The historical auth_smoke.py/security_smoke.py CRUD scripts are retired with a guard; use the current suites instead. No live delivery is needed for tests.


## Appearance, notifications and profile banners

The desktop header is sticky (76px), with a subtle scrolled shadow. Workspace and Manage account sidebars stay below it; anchor/focus offsets prevent covered controls. Header geometry is shared across workspaces and personal pages. Profile content is centered, with an earned/base banner above aligned picture/details panels. Personal and Education pages expose Dashboard, Instructor Studio or Admin overview as appropriate; workspaces retain their sidebar navigation. Student For Education appears only in the avatar menu; Guest sees it in the header. Admin and Instructor have no For Education navigation entry.

Appearance offers Light, Dark and System from a labelled header disclosure. Authenticated preferences persist in users.Appearance and are authoritative on other devices; Guest preferences use localStorage. System tracks the browser/OS color preference. Dark mode uses semantic foreground/surface/field/error tokens while preserving readable terminal/provider colors. Invalid preference values and CSRF-free changes are rejected.

`user_notifications` stores per-recipient application submissions/resubmissions (each Admin), review outcomes (applicant), Admin unpublishing (course owner) and earned banner updates (Student). The bell shows the unread count and latest five, with a paginated All notifications page. Opening an item marks it read and follows its server-generated local destination. Individual/all read actions are CSRF-protected POSTs scoped to the current account. Admins have independent copies and read state. The recipient/event unique index prevents retry duplicates. Counts update when loading a page; no paid SMS/email channel or real-time push connection is introduced.

All roles can choose Foundation, Evergreen or Midnight preset vector banners. Only Students can choose course rewards. Personalization.SyncRewards checks that every current lesson, published CTF and published lab has a matching completed progress row; enrolment, a posted key or a Completed status string alone is insufficient. Final course completion grants the reward inside the progress transaction; signed-in visits also backfill historical completions. A unique user/course reward record and notification event key prevent duplicates. Theme choices reflect course subjects (cipher lock, terminal, network or learning/book), and locked options state the course requirement. Earned records persist, while Admin/Instructor profiles use base choices. There are no user-uploaded banners or permission rewards.

For Education presents requirements followed by ordered application steps. The application CTA appears before any submission, then View my application replaces it after submission. A direct status URL without a record leads to Apply with feedback. Required application fields show a red asterisk; every field has a labelled inline error target, including added teaching roles, dates, PDF and declaration. Client blur/submit checks supplement server validation; overlapping periods cannot inflate five years. ASP.NET's invariant date helper fields are preserved when rows are reindexed. Optional fields remain optional. Invalid submissions preserve text; file inputs require reselection. Browser validation bubbles are suppressed on the application and Admin access/review forms.

2FA uses a distinct On/Off status badge at the top right of its card. Password timestamps omit development-history commentary. Devices & sessions uses consistent metadata rows and hides password confirmation when no other device can be signed out. Course deletion first checks enrolments/learning records: protected courses show a reason and publication-settings link instead of a destructive control, while unused courses show a permanent-delete confirmation. POST guards remain authoritative for concurrent changes.

Migration PersonalizationAndNotifications only adds appearance/banner columns and the notification/reward tables; existing accounts default to System and Foundation. No private configuration is changed. tests/Personalization uses only the guarded review database and local message captures, exercising navigation, validation, CSRF, ownership/read isolation, review and publication events, historical/final completion rewards and locked banner rejection. Keep Desktop Web as the only layout target.


### Focused page layout refinements

For Education, Apply and application Status use the public/personal layout without the role workspace sidebar. Signed-in users retain a header return link, while Guest navigation stays public. The account settings navigation remains available beside a centered 42rem content column. Review course centers its heading and form within the workspace content; delete/blocked-course confirmation uses a narrower 40rem measure. Profile picture/details panels stretch to equal height without spacing out their contents. Banner selection returns to Profile without a gallery fragment so the new cover and success message are visible at the top; reward notifications still link to the banner gallery.

Success feedback is rendered inside the workspace main column, the account content wrapper or the Profile container. It never inserts a row above the entire workspace or moves its sidebar. Pending Instructor reviews retain the two-column evidence/decision layout. Closed reviews show a centered single-column record with specific Approved, Rejected or waiting-for-information text; older revisions offer View latest submission. The closed page has no empty decision card. The 2FA status badge remains at the card's top right with smaller text/padding.

Application full names require letters and allow Unicode name letters/marks, spaces and common name punctuation, without digits. Teaching topics and subjects taught require 10–200 trimmed characters, with inline guidance and both browser/server validation. A short description padded with spaces is rejected; institutions and positions still rely on human evidence review for authenticity. Failed validation creates no application/revision. Historical submissions remain unmodified. This refinement adds no database migration and no dependencies.


### Account and review UI refinements
- Appearance choices apply and close the menu on success; save failures remain visible. Auth step numbers use yellow markers with dark numerals in both themes.
- Manage account navigation and content share a centered desktop grid. Login history fits its four columns; 2FA uses the phone verification dot-and-text status at the card's upper right.
- Notifications are centered in the workspace. User detail cards stretch equally; an Admin's own account uses one read-only card. Return actions use outline buttons.
- Application status colors are shared across Admin, Student, review history and submissions: Pending blue, Needs more information orange, Approved green, Rejected red. Submission history labels the latest version and separates date/status columns.
- Instructor application email validation suggests corrections for known common provider typos (for example `gmail.c`), without rewriting the input or restricting institutional domains. This does not verify mailbox ownership.
- Review course enables Save only when values differ from persisted values. The server compares trimmed values and treats empty optional fields consistently; unchanged requests return “No changes to save.” without mutation, update audit entries or notifications.


### Review history presentation and email typo coverage
- Review records label Reason and Explanation, followed by reviewer/time. The review section states the timezone once. Admin-only verification uses a collapsed native disclosure with labeled Confirmed / Not confirmed rows; empty notes have no heading. No verification details are rendered for Students.
- Submission history aligns Submission left, Submitted on center, and Status right, including headers. Back to For Education uses the secondary outline button beside Submit application.
- Application email checks share one server-generated correction map with the browser. Common `.c`, `.con`, `.cmo` and `.cm` mistakes are covered for Gmail, Yahoo, Microsoft, Apple, Proton Mail, AOL, QQ and NetEase; Proton `.me` mistakes are also covered. Matching is case-insensitive and does not rewrite addresses, enforce a provider allowlist, or verify mailbox ownership. Institution, company, country and `.co` domains remain supported except the specific existing `gmail.co` typo rule.


### GitHub login

Login and Register show Google and GitHub choices only when each provider has both credentials configured. Both share the same protected external-cookie onboarding, explicit account linking, recent-login password proof, session records and disabled/lockout/email/2FA guards. New registrations always become Students. Existing accounts are identified by provider + immutable provider ID, never silently merged by matching email. A signed-in owner can connect a provider using the same verified HackAcad email in Email address → Connected accounts. Connected accounts shows provider identity snapshots. Disconnect requires fresh proof from a remaining configured login method and any enabled 2FA; the final transaction rechecks available methods. Successful connection, disconnection and confirmed email changes create account-owned security activity and notifications. Email changes preserve provider bindings; notices go to both old and new addresses. No database migration is required.

GitHub uses the built-in OAuth handler with PKCE and `user:email` only (no repository access), reads `/user`, and selects the verified primary address from `/user/emails` even when the public profile email is private. A usable provider email is required. GitHub registrations still require HackAcad email verification; an unconfirmed account can return to its verification page by proving the same linked provider identity. Access/refresh tokens are not stored.

`tests/ExternalFlow` runs GitHub through fake provider HTTP backchannels with test-only client values and local email captures; it enforces the isolated `hackacad_uiresearch_20261016` database. It covers provider availability, PKCE/state/CSRF, private/unusable email, Student-only onboarding, mailbox verification, forged data, provider mix-up, safe redirects, first-password proof, explicit linking, sessions/history, lockout, disable and 2FA. Run it together with `tests/GoogleFlow` for regression coverage. No live OAuth or outbound messages are exercised by these tests. No database migration is needed: Identity's existing `user_logins` table stores each provider binding.


### Administrator security and account support

`AdminSecurityAndSupport` adds alert/event records, support access/conversations/messages, session MFA confirmation, audit context and `admins.IsReadOnly`. Startup applies this migration through the existing initializer. Keep the migration and model snapshot together; do not generate a second migration for the same changes.

Administrators must enable their existing authenticator or SMS two-factor authentication before using the website. An existing Admin without MFA is sent to Manage account → Two-factor authentication, including after external sign-in. Account setup and logout remain available. Administrators cannot turn MFA off. Save the generated recovery codes. Admin browser sessions expire after 15 minutes without activity and after eight hours in total; checking a session does not extend the absolute limit.

Admin changes require fresh password (when configured) plus the enabled second factor or one unused recovery code. A protected challenge is bound to the current user, session, security stamp, action and target. The resulting grant lasts five minutes for that scope. Submit the completed form first. A missing grant opens a native verification dialog; success automatically continues the exact original operation. Cancel or failed verification leaves the draft in place. Unchanged course/access/investigation submissions do not need a new grant. Pending forms and idempotency keys are bound to the user, browser session, security stamp, action, target and payload for ten minutes; concurrent or repeated delivery within that window does not repeat a completed mutation. A changed record must be reloaded and reviewed. Without JavaScript, verification returns a protected continuation form with a fresh CSRF token and a Continue fallback. A reused authenticator code is rejected for two minutes within this server process. Verification grants, SMS challenges and rate limits use memory on this single-instance project; a restart invalidates them. Shared storage and equivalent distributed replay protection are needed before running multiple instances.

Administration → Security shows failed sign-ins in the last 24 hours, currently locked accounts, open alerts. Investigation saves return to the original alert list, preserving its status filter and page. Rules flag five failures for one account in ten minutes, ten distinct failed identifiers from one IP in ten minutes, and a new Admin device/IP combination. Unique time-bucket keys deduplicate alerts; a resolved alert is not silently reopened. Signals are leads for investigation, not proof of an attack. Administrators record New/Investigating/Resolved with investigation notes; optimistic concurrency prevents overwriting another review. Login POSTs are limited to 30/IP/10 minutes. Device labels come from browser headers and can be spoofed.

Users → User details → Review sessions lists usable browser sessions and lets a verified administrator revoke one or all with a reason. Changes, revocations, support replies, access decisions and alert investigations retain actor, UTC time, result, device, IP, request correlation and relevant before/after values. The audit page defaults to management actions, with explicit account-security and all-event filters. Compact summaries hide full identifiers, complete reasons, before/after field values and request metadata in expandable full-width detail rows. Missing historical values are labelled Not recorded. Audit data is access-controlled application history, not cryptographically tamper-proof storage. Logs do not store passwords, provider tokens, OTP values or recovery codes.

Another administrator's permissions can be changed to Read-only auditor in User details. This is a restricted Admin marker: it can read security alerts, login logs and audit history, and cannot manage users, courses, applications or support. The server enforces the restriction on direct requests and invalidates old sessions after permission changes. Self-demotion is rejected; access changes preserve at least one active administrator with management permissions.

`/Support` remains available without a normal login, including for disabled users. An active, email-confirmed user with an application session enters their own support area directly through Contact support. Signed-out and disabled users prove possession of their current HackAcad mailbox using a random, one-use link valid for ten minutes. Only a SHA-256 token hash is stored. A GET previews the link; an anti-forgery-protected POST consumes it. The separate support cookie is HttpOnly, scoped to `/Support`, expires after 30 minutes and is invalidated by security-stamp changes. It grants no application role or learning/admin access, and does not reactivate an account. Unknown and known emails receive the same generic response. The existing email delivery and configured public base URL are reused; local tests capture email instead of sending it.

Mailbox-verified users see only their own requests and public messages. One open request per user, a three-new-requests/day limit, one-minute access-link cooldown, message cooldowns and IP rate limits reduce spam. User replies return a request to Awaiting admin, including reopening a resolved request. Support types are Sign-in issue, Account security concern, Other account issue, and Account suspension appeal. The last type is available only while the account is disabled, enforced again after locking the user record. The `SupportRequestCategories` migration preserves older requests as Account support without guessing their original type. Administration → Account support provides search, status/type filters, current account access, public replies, internal notes and links to the user's access history and access controls. Public replies create an in-app notification and an email notice; internal notes appear only to administrators and are never emailed. A delivery failure leaves the saved reply available and displays an explicit notification failure. Closing a request does not restore account access: reactivation is a separate, verified and audited decision.

Verification: `tests/AdminSecurityFlow` runs only against `hackacad_uiresearch_20261016` with local email pickup. It covers MFA gates, deferred verification and automatic continuation, exact payload/session binding, no-op handling, concurrent replay, support category restrictions, readable/escaped audit details, CSRF, role/ownership checks, support-only access, one-use/expired links, private notes, notifications, concurrency, session revocation/expiry, alerts and rate limits. Existing connection, course and Instructor workflow fixtures complete the real mandatory Admin MFA and verification flow. Tests do not use the live application database or real email/SMS/OAuth.

Support conversations display status beside the Conversation heading. While Awaiting admin, the user sees a waiting notice and no reply composer. A public administrator reply with Awaiting user enables one user reply; that reply returns the request to Awaiting admin. The locked server write checks both status and the latest public message. Internal notes do not change request status or enable replies. Resolved requests retain the existing follow-up path after an administrator's public reply. Signed-in Admin menus omit Contact support; the signed-out support path remains available.

A successfully verified, already linked Google/GitHub identity receives a clear account-disabled notice and Contact support action. This grants no application session and does not restore access. Failed OAuth, unlinked email matches and password recovery retain their existing generic feedback. Security overview groups three metric cards above an inline alert toolbar.

Filtered administration lists show result counts above the table with singular/plural wording and keep counts beside pagination. Security alerts support combined name/IP and status filters, preserved across pagination. Audit logs default to All events; application-review summaries show decision and revision, with the stored reason in View details. Application timestamps omit repeated per-record zone labels while retaining viewer timezone conversion. Devices & sessions places its zone in the subtitle and uses the available measure for its device explanation. New support requests default to Sign-in issue for both active and disabled users; appeal eligibility remains enforced.


### Administrator dashboard

The administrator overview uses existing records for current account/course totals, pending support and review queues, daily unique successful sign-ins, course enrolment comparisons and account roles. Activity periods cover 7, 30 or 90 calendar days in the viewing administrator's time zone; the comparison covers the preceding equal calendar period. Active users means users who signed in during that period, not users currently online. Role totals count each user once using the existing account-role precedence; disabled accounts are reported separately. Charts provide keyboard navigation, exact-value tooltips and expandable data tables. Recent management events use current account names while keeping the recorded audit target intact. No schema migration is required. `tests/AdminDashboardFlow` checks date/DST boundaries, distinct-user counts, roles, enrolment comparisons, queues and period validation against the isolated research database.

### Database table naming

`UseReadableTableNames` renames application tables to lowercase snake_case and removes the Identity `AspNet` table prefix: `users`, `user_claims`, `user_logins`, and `user_tokens`. For example, `AdminActivities` becomes `admin_activities`. The migration preserves existing rows and foreign-key relationships; columns and C# entity/DbSet names remain unchanged. Back up the database, stop the running site, then apply the migration with `dotnet ef database update` before restarting with the matching code. Historical migrations retain their original names. EF's internal `__EFMigrationsHistory` table retains its standard name so migration tracking remains compatible.


## Course approval

New instructor courses are saved as unpublished drafts. Add at least one learner-visible curriculum item and use **Submit for review** in the curriculum page. Admins review the complete course at **Courses → Review**; rejecting requires feedback. Approval leaves the course unpublished so its instructor can choose when to publish. Rejected courses must be edited before resubmission.

Any saved change to course details, lessons, challenges, labs or course notebooks cancels pending/previous approval and unpublishes the course. Unchanged saves and publication-only toggles preserve approval. All curriculum writes update the parent course version atomically; stale decisions or resubmissions cannot overwrite a newer revision. Admin review uses the existing MFA verification, read-only restrictions, audit log and notification system.

The `AddCourseApproval` migration adds review fields to `courses` without removing tables or records. Already-published courses are treated as approved; existing unpublished courses require review. Restart the application after pulling to apply migrations before using the workflow. Run `tests/CourseReviewFlow` against its guarded isolated database with local email/SMS capture for end-to-end checks.
