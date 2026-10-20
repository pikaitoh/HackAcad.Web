# HackAcad UI guide for the team

This guide records the shared UI used by all current pages, based on the Home design selected by the user. The current user owns Admin and User/Authentication. Course authoring, CTF, simulation, badges and progress remain with their module owners. Use the existing shared shell and controls when implementing those modules. Keep the selected mint, teal, yellow and Figtree direction consistent when adding module pages.

This guide contains the shared design tokens and visual rules. Start a Development instance and open `/Home/UiGuide` for button, field, role/status and empty-state examples. Open that route directly; it has no link in the site footer and returns 404 outside Development. Full form/table examples are in `Views/Account/Register.cshtml` and `Views/Admin/Users.cshtml`.

## Where the shared UI lives

| File | Responsibility |
| --- | --- |
| `Views/Shared/_Layout.cshtml` | Bootstrap 5.3.3, shared font preload, header, skip link, main, footer, CSS and scripts. |
| `Views/Shared/_Brand.cshtml` | Shared selected E book-and-shield mark and HackAcad wordmark (no trailing dot); use the partial instead of making a new logo. |
| `Views/Shared/_LoginPartial.cshtml` | Role-aware public/account navigation. Logout is a POST form. |
| `Views/Shared/_AuthLayout.cshtml` | Login/register two-column introduction and narrow form. |
| `Views/Shared/_AdminLayout.cshtml` | Admin workspace/sidebar, nested inside the shared layout. |
| `Views/Admin/_ViewStart.cshtml` | Applies the Admin layout to Admin views. |
| `Views/Shared/_AdminNav.cshtml` | Admin navigation and current-page states. |
| `wwwroot/css/site.css` | Shared tokens, typography, controls, headings, forms and states. |
| `wwwroot/css/home.css` | Home composition (hero, photograph, format band and FAQ), inheriting the shared theme. Loaded through the Home view's `Styles` section. |
| `wwwroot/css/accounts.css` | Authentication, profile and account layout styles. |
| `wwwroot/css/admin.css` | Admin layout, panels, filters and shared table presentation. |
| `wwwroot/css/learning.css` | Learning outlines, role-aware dashboard, review queue and illustrative course/progress patterns. |
| `wwwroot/js/account-forms.js` | Shared inline validation from Razor's `data-val-*` rules, including password match, textareas and first-invalid-field focus. |

The layout loads Bootstrap first, then `site.css`, `accounts.css`, `admin.css` and `learning.css`, in that order, on every page. Module views normally inherit `_Layout` through `Views/_ViewStart.cshtml`; do not copy the complete HTML shell. The Admin layout is for Admin pages, not a shell for student/instructor learning modules. Custom layouts that need scripts must forward the optional `Scripts` section as the existing nested layouts do.

## Tokens, type and spacing

The `--ha-*` properties in `site.css` are the source of truth:

| Use | CSS property | Value |
| --- | --- | --- |
| Main text / supporting text | `--ha-ink` / `--ha-muted` | `#193e46` / `#4d686a` |
| Links, focus and selection / hover | `--ha-primary` / `--ha-primary-hover` | `#33796e` / `#275e56` |
| Page / panel backgrounds | `--ha-canvas` / `--ha-surface` | `#f2fbf7` / `#ffffff` |
| Dividers / introduction tint | `--ha-line` / `--ha-tint` | `#cbded5` / `#e3f1eb` |
| Positive / destructive state | `--ha-success` / `--ha-danger` | `#1d694b` / `#b12e3f` |
| Primary button / hover | `--ha-action` / `--ha-action-hover` | `#f2cf45` / `#e6be2d` |
| Field boundary | `--ha-field-line` | `#74978c` |
| Dark panel / supporting text | `--ha-dark` / `--ha-on-dark` | `#28545a` / `#d2e5df` |
| Panel corners | `--ha-radius` | `16px` |

Use Figtree everywhere. The shared layout preloads the self-hosted variable font once; its declaration is in `site.css` and its license is `wwwroot/fonts/OFL-Figtree.txt`. Body text is `.9375rem`, ordinary page titles `2rem` at weight 750, section titles `1.25rem`, smaller headings `1.0625rem`, labels/buttons `.875rem`, helper text `.8125rem`, and compact metadata `.75rem`. Home keeps its larger display title (`3.5rem` desktop); account and Admin screens keep task-sized headings and practical density.

Use yellow primary buttons with deep teal text, white/outlined secondary buttons, teal links and selected navigation, and red destructive actions. Buttons are pills; fields use `12px` corners and panels use `16px`. The shared header uses deep teal (`--ha-header`) to separate navigation from the mint canvas. Its wordmark/navigation are light, hover/current links and focus rings use yellow, and the public registration button is yellow/teal. The selected book-and-shield mark uses a light reversed variant directly on the dark header; avatar menus use the active theme surface and text tokens. The footer is dark teal. Authentication introductions, the Dashboard next-step panel and the Admin summary use dark teal with light text and yellow accents. Focus rings are teal on light surfaces and yellow on dark surfaces. Do not turn status colours into decoration.

Use Bootstrap spacing utilities where they already fit: `mb-3` for ordinary field groups, `mb-4` before a form submit or between larger groups, `mb-5` between guide sections, and `gap-3` for a normal action gap. Shared container padding is `2rem`. Use `.form-actions` and `.table-actions` for wrapping actions instead of manual spaces. The CSS contains recurring rem values rather than a separate spacing-variable system.

Do not invent a module palette, new font or competing button system. Do not change a global selector or core stylesheet to solve a single page. If a new local pattern is needed, add a module stylesheet and a unique wrapper such as `.ctf-module`; scope its rules under that wrapper. Load it through the optional `Styles` section (nested layouts must forward that section), or coordinate a shared-layout edit if it is used across several modules. Reuse `var(--ha-...)` values. Discuss changes to shared tokens/components before applying them across modules.

## Start a normal module page

```cshtml
@{ ViewData["Title"] = "My learning"; }
<div class="container module-shell">
    <div class="page-heading">
        <div><h1>My learning</h1><p class="text-secondary">Continue your courses.</p></div>
        <a class="btn btn-primary" asp-controller="Courses" asp-action="Index">Find a course</a>
    </div>
    <!-- Module-owned content -->
</div>
```

This is a structure example: use your module's real controller/action. Add one meaningful `h1`, then logical heading levels. `.page-heading` aligns its title and action horizontally. `.section-heading` aligns a section title and optional short link. Existing `.surface-panel` groups related content; `.form-measure` provides a bordered form up to `38rem` wide. These classes are already available because all common CSS files load globally.

## Actions and feedback

On `Profile`, keep picture controls on the left, with outlined Upload picture and Remove picture buttons in a wrapping action row. The right-hand card groups Profile details (role and joined date) above the Username and Full name form, separated by one divider. Full name sits below Username, is optional and accepts up to 100 characters; it does not change the email login. Omit the Dashboard / Profile breadcrumb and security actions. Email management and verification status belong to Manage account → Email address.

The signed-in header replaces My profile and the standalone logout button with an avatar dropdown (`_LoginPartial`, `account-menu.js`, `accounts.css`). Keep only working destinations: View profile, Manage account, and POST-only Log out. Use the uploaded photo or a grapheme-aware name initial; never show a broken default photo. Native `details`/`summary` supports keyboard activation without JavaScript; the script adds outside-click and Escape dismissal. Manage account uses its shared vertical sidebar for security functions. The menu opens inward from the right edge and has readable text plus matching line icons. Do not add placeholder menu links for Badge/Saved/Rewards or unrelated commercial services.

`Profile` manages picture upload/removal and username. `Settings` manages email status, the password link, optional phone number and SMS OTP. Keep phone pending/verified states separate; requesting a code never replaces the saved number. OTP uses a six-digit text input with `inputmode="numeric"` and `autocomplete="one-time-code"`, with inline feedback, a cancel action and resend guidance. Show an explicit Development test-mode notice when messages are captured locally; never display the code itself in the page. Verified phone numbers support phone + password login; password recovery always uses the account email. SMS 2FA is an explicit separate setup choice, never enabled automatically by contact verification. Avatar constraints and crop behaviour are described beside the file input; actual images are decoded/re-encoded by the server, not trusted by filename or MIME type.

Use `data-validated-form` on ordinary forms such as Admin course review to reuse the shared validation script; existing account forms retain `data-account-form`. It reads Razor's validation rules for inputs, selects and textareas, puts each error beside its field and prevents duplicate native browser bubbles when JavaScript is available. Server validation remains required. For course review, Title is required (150 characters maximum), Category optional (50) and Description optional (1,000). Surrounding whitespace is trimmed; blank optional metadata is stored as null so the catalogue can display its empty-category fallback. Keep each field's help/error IDs in `aria-describedby` and use `aria-live="polite"` on the error span.

Email verification reuses `_AuthLayout` and existing form/error styles. Registration leads to `VerificationPending`, not a signed-in Dashboard. `VerificationPending` displays the registration email and resends directly with an anti-forgery protected POST. The recipient comes from protected browser TempData, never a form field or query string. Keep the one-minute delivery cooldown. Login has no resend link; a correct password for an unverified account returns to the pending page. A fresh browser without registration context returns to login; no email lookup form is offered. `ConfirmEmail` shows success with Log in or an invalid/expired state with Request a new link. Never show a verification URL/token in the browser as a development shortcut. Keep disabled access and unverified email distinct; Admin Users shows `Email not verified` under the address when appropriate. Admin email edits explain the re-verification and sign-out consequences before submission.

| Pattern | Existing class |
| --- | --- |
| Main action (yellow, teal text) | `btn btn-primary` |
| Alternative / cancel | `btn btn-outline-secondary` |
| Destructive action | `btn btn-danger` |
| Compact / large variant | Add `btn-sm` / `btn-lg` |
| Positive result | `alert alert-success` with `role="status"` |
| Supporting text | `text-secondary`, `form-text`, `record-secondary` |
| Empty result | `empty-state` with heading, explanation and recovery link |

Use one primary action per section. Use links for navigation and buttons for submission/actions. Give every button a `type`. Native `disabled` is appropriate for unavailable buttons; changing opacity alone does not disable an action. Avoid duplicate success alerts: `_Layout` already renders `TempData["Success"]`.

`.role-badge` with `data-role="Admin"`, `Instructor` or `Student` displays actual account roles: Admin uses purple (`#624080` on `#eee6f7`), Instructor yellow, and Student green. Keep the role text visible so colour is not the only distinction. `.status-label.status-active` / `.status-label.status-disabled` displays access state with a dot and readable label. Do not reuse these account meanings to imply unrelated course completion states. A new module state needs its own explicit label and scoped pattern.

## Learning patterns and module handoff

The learning patterns draw on TryHackMe's clear next-step hierarchy, Cisco NetAcad's course information and outlines, and HTB Academy's content structure. They inherit the same Figtree family, mint/teal palette and yellow primary controls as the rest of HackAcad.

| Pattern | Existing classes / reference | Integration requirement |
| --- | --- | --- |
| Task-first dashboard | `.dashboard-layout`, `.workspace-start`, `.dashboard-activity`, `.activity-summary`, `.learning-empty`; `Views/Account/Dashboard.cshtml` | Admin uses `/Admin` as its Dashboard; the account Dashboard endpoint redirects Admin there. Student uses the learning Dashboard; Instructor Dashboard routes to Instructor Studio. Both use the shared workspace sidebar. Learning counts come from the account model. Add a course action only when its destination exists. |
| Account/security shortcuts | `.dashboard-account`, `.account-shortcuts`, `.account-access` | Keep real profile/password links and role-specific access descriptions. |
| Admin review queue | `.review-queue`; `Views/Admin/Index.cshtml` | Use `DisabledUsers` and `Courses - PublishedCourses`; links target existing Users `status=disabled` and Courses `status=draft` filters. Unpublished courses are not necessarily awaiting review. |
| Expandable outline | `.learning-outline`, `.outline-content`, `.outcome-list` | Keep native `details` / `summary`, logical headings and definition-list learning goals. The guide shows illustrative lesson content; Home introduces learning formats in its separate landing theme. |
| Course summary | `.course-preview`, `.course-metadata` | Supply real title, description, category, instructor and lesson count from the owning module. Keep metadata text readable; icons supplement it. |
| Learning progress | `.learning-progress` with labelled native `progress` | Supply reliable completed/total values and readable text. Track height is `7px`, radius `4px`. Without a reliable denominator, show a completed count instead of a percentage. |
| Example notice | `.guide-example-note`, `.guide-learning` | Keep illustrative course, instructor, lesson and progress data explicitly labelled in Development UI guide. |

The project currently has no `CourseController` or resume engine and no duration/difficulty schema. UI-guide course and progress values are examples, not saved account records. Course, progress, CTF, simulation and badge owners must provide real data, access/ownership checks and working routes before connecting learning actions. `Enrollment.LastLessonId` is only an integration clue: validate course membership, enrolment and access before using it to resume. Omit unsupported duration, difficulty and prerequisites until the owning module supplies their data. Do not add fake enrol/resume buttons.

## Forms and validation

Use Username for the user-facing profile name everywhere (including registration and Admin forms); the existing DisplayName model/property is retained. Login continues to use email. Change email opens a dedicated form with New email and Current password. Keep validation inline, explain that the current email remains usable until confirmation, and use buttons for Send confirmation link and Back to profile. Confirmation signs the user out; invalid/used links offer a profile recovery action. Never present a pending or undelivered email as already changed.

Use Bootstrap `.form-label`, `.form-control`, `.form-select`, `.form-check-input`, `.form-check-label` and `.form-text` on native controls. Keep persistent labels linked to unique input IDs. Use appropriate input types, autocomplete, required/max-length constraints and model validation. Do not add a second validation library.

Follow the existing Register view: an `asp-validation-summary="ModelOnly"` element with `.validation-summary` and `role="alert"`; an `asp-validation-for` span with `.field-error` per field; `aria-describedby` pointing to the field's help/error IDs; and `aria-invalid="true"` when ModelState has an error. ASP.NET applies `.input-validation-error` for the server error border. Keep POST forms using Tag Helpers so existing antiforgery behavior is preserved.

For account forms, `data-account-form` enables the shared script to read server-generated DataAnnotation rules and display errors only in each field's `.field-error` span. It suppresses native validation popups while active, validates on submit, rechecks invalid fields during editing, and focuses the first error. Without JavaScript, native constraints and server validation remain available. Identity email/password errors are mapped to their fields; the form summary is reserved for form-level failures. Delete account uses the same inline feedback and checks the confirmation against the selected account's stored email (trimmed, case-insensitive), not a new-email format rule. This also allows correcting/removing legacy accounts with older email formats. Keep the server-side equality check and deletion safeguards. Server validation, authorization and ownership checks remain required regardless of the UI. Usernames are trimmed, 2–50 characters, allow Unicode letters/numbers and common name punctuation, and need not be unique. Email addresses are trimmed and require a dotted domain with a letter suffix; format validation does not verify mailbox ownership. Passwords retain the existing 8–100 character, ASCII uppercase/lowercase, digit and symbol policy. Keep password policy guidance accurate; copy it from the current model rather than weakening the policy through presentation changes.

## Navigation and tables

Coordinate shared navigation links in `_LoginPartial` and reuse `_Brand`. Apply `aria-current="page"` to the active destination, keep the full navigation visible, and retain the skip link / `main-content` target. Client-side visibility does not replace controller authorization.

For record tables, follow `Views/Admin/Users.cshtml`:

```html
<div class="table-responsive">
  <table class="table align-middle module-table">
    <caption class="visually-hidden">Course records</caption>
    <thead><tr><th scope="col">Course</th><th scope="col">Actions</th></tr></thead>
    <tbody><!-- Real module-owned rows --></tbody>
  </table>
</div>
```

The table has a `42rem` minimum width and scrolls horizontally within its wrapper. Use `.record-secondary` beneath a primary name and `.table-actions` for row links; include the record name in accessible action labels. Use `.filter-bar` and `.filter-search` for labelled GET filters, preserve filter values through pagination links, and use `.pagination-bar` for page context plus Previous/Next navigation. Show `.empty-state` when valid filters match no records, never when invalid filters prevented the search. Login logs uses `.login-log-filters` to keep field tops aligned when date errors wrap; actions occupy a separate row. Mark invalid date fields with `aria-invalid`. The Administration sidebar contains Overview, Users, Instructor applications, Courses, Login logs and Audit log, without an explanatory footer or duplicate Profile link.

Build for desktop browsers only. Keep the full header navigation visible, account and Admin sidebars vertical, and the desktop column structure. Do not add device-width breakpoints, phone navigation or small-screen layout overrides. Keep long text wrapping and wide data tables scrollable within their own wrapper.

Before handing off a module page, check desktop browser layouts, keyboard navigation/focus, native and server validation errors, empty and long records, and visible active/status labels. Respect the existing reduced-motion rule. Add only routes and functionality actually implemented by your module; Home offers visitors registration or login and sends signed-in users to their Dashboard.

## Home page

Home follows the user's OpenLearning and Expertise Pays references: pale mint, deep teal text, a centred photo-led hero, yellow primary actions, rounded secondary controls, a dark teal learning-format band and native FAQ disclosures. This direction now applies to every existing page. `.home-landing` scopes only the Home composition; account and Admin pages inherit the same shared palette, font, header and controls while retaining task-oriented layouts.

`Views/Home/Index.cshtml` sets the body class and loads `home.css` through the shared layout's optional `Styles` section. The shared layout already preloads Figtree, so Home does not load a second font. The headline is `3.5rem` desktop, `3rem` up to `1199px`, and `2.375rem` up to `767px`; weight 800, with a 1.12 desktop line-height. The landing colours inherit the shared tokens: ink `#193e46`, green `#33796e`, mint `#f2fbf7`, action yellow `#f2cf45`, and dark band `#28545a`.

The transparent learner image (`wwwroot/images/home-student.png`) was generated with OpenAI image_gen on 2026-10-16 as an illustrative learning scene, not a real user or testimonial. Its exact prompt and origin are embedded in PNG metadata. Do not present it as customer evidence. The learning-format preview contains descriptions, not user records. Terminal output is explicitly an example exercise; it is not an interactive terminal. Use genuine records before adding course-specific actions or statistics.

Home is for signed-out visitors only. `HomeController.Index` redirects authenticated users to their Dashboard, including direct `/` and `/Home/Index` visits. Signed-in navigation omits Home. Admin logo targets `/Admin`; Student/Instructor logos target their role dashboard through the account Dashboard route. Module links live in the shared workspace sidebar. The default sign-in destination resolves through `AccountController.Dashboard`; valid local return URLs still take priority. There is no separate Administration link duplicating Admin Dashboard; after logout the visitor returns to Home. Keep registration and login links working. Home's FAQ answers describe the existing account flows. New sections must not introduce invented learner counts, customer reviews, certificates or partner logos. Keep the dark teal hero, terminal preview and truthful learning-format examples. Respect reduced motion and visible keyboard focus.


## Personal security pages

View profile keeps picture controls on the left and Profile details (Role, Joined, Username and Full name) on the right. Security functions belong to Manage account. Its shared `_ManageAccountLayout` / `_ManageAccountNav` provides Email address, Phone number, Password, eligible Two-factor authentication, Login history and Devices & sessions. Email status uses readable Verified/Not verified text with `.status-label` on the email page. Keep the Audit log in Administration, and action controls as buttons. Do not add a View profile button beneath the Manage account sidebar; use the avatar menu for that destination. Keep the settings sidebar vertical with visible active-page styling and keyboard access. Use `.account-workspace`, `.account-sidebar`, `.account-main` and `.account-panel` instead of nesting a second `.container.module-shell` inside the layout.

Devices & sessions combines a plain list of real current-account logins with password-confirmed per-device and bulk sign-out. Mark the current row `This device`; it never has a Sign out button. Use `.session-form`, `.session-password` and `.session-row`. Explain that active means usable, not online, and device names are estimates. History/activity use the existing `.module-table`, scoped scrollable regions, table headers/captions and real pagination; do not invent earlier records. Display these timestamps in the signed-in viewer’s saved time zone, retaining UTC storage.

Authenticator setup uses local QR PNGs, a wrapping manual key and a six-digit field. Never use a remote QR image endpoint or include the key in a link. Recovery-code results are private, no-cache and shown once after generation; they are not a static guide/example. New forms retain shared inline validation, descriptive labels and field help/error associations. Admin activity inherits `_AdminLayout` and `_AdminNav`; do not duplicate the sidebar inside the view.


Admin navigation keeps Overview, Users, Instructor applications, Courses, Login logs and Audit log on separate routes. `/Admin/LoginLogs` shows known accounts' recorded sign-in attempts with username/email search, result and inclusive date filters in the viewer’s saved time zone, and 20 rows per page. It is Admin-only; personal Login history remains in Manage account. Unknown-email attempts are not stored. Disabling an account preserves login records. Device labels are inferred from the browser header, not verified device identities.

Users shows Last login, derived from the latest completed successful sign-in (including completed 2FA/recovery sign-ins). Edit account and your Password page show both Last login and Last password changed with seconds, in the viewer’s saved time zone; stored timestamps remain UTC. Failed attempts and unfinished 2FA do not advance Last login. `AddPasswordChangedTimestamp` adds nullable `AspNetUsers.LastPasswordChangedUtc`; it is saved atomically with successful password changes. Older unknown password-change times display No record. These read-only values cannot be set by form submissions.


Google sign-in uses the ASP.NET Core Google handler (PKCE, anti-forgery-protected POST initiation, 10-minute external cookie, no stored Google access tokens). Set `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` in ignored local settings or deployment environment variables. Register `http://localhost:5000/signin-google` for the current local app; deployed origins need their own exact HTTPS callback URI. Google buttons are available on Log in and Register only when both credentials are configured. These OAuth credentials are separate from Gmail SMTP/App Password settings.

A new Google user chooses a validated Username and receives only the Student role. Google-verified Gmail/Workspace email can be confirmed directly; other domains still require HackAcad's mailbox verification. Existing accounts are never automatically merged by email: sign in normally and connect the matching Google account under Manage account → Email address → Connected accounts. Linked sign-ins preserve the existing role, enforce disabled/lockout/email/2FA checks, and use the existing session and history tracking. External-only users can set their first local password within five minutes of provider authentication; otherwise Password settings requests confirmation from a configured, linked provider again. The local password is required for existing password-protected security actions. Existing Google bindings identify the Google subject, independently of later local email changes.

`tests/GoogleFlow` verifies OAuth redirects, correlation/state, account creation and binding, password setup, role boundaries, sessions/history and Admin 2FA using a test-only Google HTTP backchannel. It refuses to run unless `ConnectionStrings__DefaultConnection` targets `hackacad_uiresearch_20261016`; use Development mail capture and fake OAuth client values. No real Google sign-in or messages are sent. Run `dotnet run --project tests/GoogleFlow -c UiResearch` from the repository with the isolated environment configured.


Desktop web is the only UI target going forward. Verify the desktop browser only; do not add phone/tablet layout branches or mobile-specific QA assets.

Two-factor authentication is available to Student, Instructor and Admin in the shared Manage account sidebar. Each account opts in using its own current password, an Authenticator app and recovery codes. Existing setup, lockout and session safeguards apply equally to all roles. Google-only accounts must set a local password first. No role can view or modify another user’s authenticator.

## Logo: E — Knowledge and protection

The user selected concept E: an open book integrated into a shield-shaped cover, with a yellow central spine. The open pages represent structured learning; the protective outline represents cybersecurity; their combined shape expresses building protection skills through knowledge. The central yellow spine connects the two pages and is intended to emphasise the connection between learning and practical action. These are design intentions, not claims about universal colour meanings.

The shared `_Brand` partial keeps the existing HackAcad wordmark, without a trailing dot, and an inline SVG so the mark stays sharp at different display scales. Dark headers use a light reversed version. `wwwroot/images/hackacad-mark.svg` is the standalone colour mark for report use. `wwwroot/favicon.svg` uses the same symbol with a mint backing for visibility on browser tabs; the shared layout links it with asset versioning.

Suggested report wording: “The HackAcad logo integrates an open book with a shield-shaped outline to represent cybersecurity education. The book symbolises structured knowledge, while the shield represents protection. Their combined form reflects the platform’s purpose of helping learners develop practical security skills through courses, challenges and simulated terminal exercises. The yellow spine visually connects the pages, emphasising the relationship between learning and application.”

Phone number settings follow the Email address pattern: `/Identity/Account/PhoneNumber` is a summary with the current contact number, verification status and Add/Change/Continue verification action. `/Identity/Account/ChangePhone` contains the number and SMS code forms. Keep Phone number selected in the sidebar on both routes. Failed sends/code checks remain on the change form; successful verification returns to the summary. Pending changes never replace the current number until verified. Removal opens a password-confirmation page before its CSRF-protected POST. Adding/changing a number also requires the current password; an active SMS factor must be turned off first.

After a phone OTP is sent, ChangePhone shows only the code step. Verify and Cancel share one action row. `phone-verification.js` counts down the server's remaining 60-second resend cooldown; afterward an empty code input changes the primary action to Resend verification code, while entering a code restores Verify phone number. Resend is always an explicit CSRF-protected POST and uses the authenticated user's pending number, never posted recipient data. Reloading preserves the remaining cooldown; server rate limits remain authoritative. Without JavaScript, users may reload after the minute and use the fallback resend button.


Phone authentication keeps the existing desktop Web layouts. Log in labels its identifier Email or phone number and links to Forgot password. SMS challenges use `SmsChallenge` for setup and login, with a six-digit field, code/resend action, one-minute cooldown and Back/Cancel beside the primary action. Never expose OTPs or protected recovery tokens in HTML. Forgot password collects the account email and uses the same generic confirmation for known and unknown addresses. The email reset link lasts 10 minutes. A valid link opens a clean Reset password URL collecting only a new password and confirmation; expired, used or invalid links show Request a new reset link. No phone is required for recovery. Two-factor settings show the active Authenticator/SMS method, password and proof for security changes, or the two setup choices while Off. Keep one method active and offer saved recovery codes during SMS login. All forms reuse shared labels, field errors, focus states, teal/yellow palette and keyboard controls; no mobile layouts.


## Time zone preferences

Manage account → Time zone is available to Student, Instructor and Admin. The selected IANA time zone is stored in `AspNetUsers.TimeZoneId` by the `AddUserTimeZone` migration and defaults to `Asia/Kuala_Lumpur` for existing and new accounts. Each authenticated request loads the viewer’s preference, so it applies on other devices without logging in again. `UserTimeZone` is injected into Razor views as `UserTime`; use `UserTime.Format(utc)` for future date/time displays, never fixed UTC offsets or the server’s local clock.

Profile Joined dates, Admin user dates, password timestamps, personal login history, Admin login logs/activity and device session timestamps all follow the viewer’s preference. Admin sees all users’ records in the Admin’s own selected zone. The active zone is labelled on dated pages. Login-log From/To dates include whole local calendar days; the query uses UTC boundaries and an exclusive next-day end, including 23/25-hour daylight-saving days. Invalid zone submissions are rejected; invalid stored values fall back to Malaysia. Changing a display preference never shifts stored events or changes OTP/token/session expiration deadlines.


## Course integration behavior

Keep the teammate's catalogue, Instructor Studio, curriculum and terminal layouts. The Courses workspace sidebar link is Student-only; authenticated headers keep appearance, notifications and the avatar menu; personal pages include a workspace return link. Admin uses Administration → Courses; opening an old catalogue/detail link redirects to course moderation. Instructor uses Instructor Studio, including the Dashboard entry, and read-only course previews never show enrollment or learning progress. Only Students can enroll, learn or submit completion/flags; backend role checks also block old non-student enrollments. Course enrollment remains an explicit CSRF-protected action; opening Learn without enrollment returns to the course details. Private courses are available only to their owner/Admin. Student learning and attachment access stop when a course/activity is unpublished.

Instructor attachment uploads use private storage and the authenticated Download File action; do not expose a static upload URL or let a form choose an existing server path. A blocked content deletion displays its explanation at the top of Curriculum and keeps student progress intact.

The terminal safely displays instructor/student text and submits its command history for server verification. Local objective ticks alone do not show the saved-success banner. Display that banner and optional reward only after a successful server response; failed saves appear as terminal text and can be retried by running a command. JSON validation stays beside the Instructor configuration field. New course, Dashboard and Instructor date/time displays reuse `UserTime.Format` and identify the viewer's zone. Desktop web remains the only design target.


## Guest homepage

Desktop Web only. Use a dark teal first viewport with left-aligned Figtree heading, yellow registration action and the configured Google action. The right-side learning workspace is an explicitly labelled example, not live user data. Follow with an inline topic strip, an accessible keyboard-operated learning-format tab group, three ordered signup/learning steps, native FAQ disclosures and a final registration call to action. Reuse the HackAcad mark and palette; do not copy TryHackMe branding, user counts, reviews or unavailable platform features. Guest headers have no Courses link, and catalogue/details require login. All homepage styles are scoped to home classes; account and Admin pages retain their existing styles.


## Education and unified workspaces

Role workspaces use `_WorkspaceLayout`: 240px desktop sidebar, main region fills the remaining width with 32px padding, no central workspace max-width. Header keeps the brand, appearance, notifications and avatar/name; Student sidebar links to Dashboard/Courses, Instructor to Studio/Create course, Admin to Overview/Users/Instructor applications/Courses/Login logs/Audit log. No mobile layouts are added. Keep the Profile and Manage account navigation separate. Course Player retains its curriculum navigation inside the Student workspace.

The public For Education page uses HackAcad's dark teal/yellow visual language and explains the minimum five-year related-teaching condition, verifiable evidence, optional supporting material and review steps. Guest applies by registering/logging in first; only Student avatars link to this same page. Admin and Instructor menus omit it. Application form uses a labelled date history, native PDF upload, inline errors, accessible add/remove experience buttons and a declaration. Require a fresh evidence PDF for each revision; old evidence remains private and retained. Optional qualifications/samples must remain labelled optional.

Admin's application queue supports name/email and independent application-status filters. Review details and prior evidence sit beside a decision form. Four visible confirmations, verified years, private source notes, public reason and a decision/category are required as applicable; server validation is authoritative. Closed reviews are read-only. Applicant status shows the reason, revision links and resubmit action for Rejected/Needs more information, but never internal verification notes. Every timestamp uses the viewer's selected time zone. User details contain read-only identity information plus a separate, reasoned Disable/Reactivate form; no create/edit/delete controls. Audit history includes actor identity and all administrators, with Only my actions available.


## Appearance, notifications and profile banners

The desktop header is sticky (76px), with a subtle scrolled shadow. Workspace and Manage account sidebars stay below it; anchor/focus offsets prevent covered controls. Header geometry is shared across workspaces and personal pages. Profile content is centered, with an earned/base banner above aligned picture/details panels. Personal and Education pages expose Dashboard, Instructor Studio or Admin overview as appropriate; workspaces retain their sidebar navigation. Student For Education appears only in the avatar menu; Guest sees it in the header. Admin and Instructor have no For Education navigation entry.

Appearance offers Light, Dark and System from a labelled header disclosure. Authenticated preferences persist in AspNetUsers.Appearance and are authoritative on other devices; Guest preferences use localStorage. System tracks the browser/OS color preference. Dark mode uses semantic foreground/surface/field/error tokens while preserving readable terminal/provider colors. Invalid preference values and CSRF-free changes are rejected.

UserNotifications stores per-recipient application submissions/resubmissions (each Admin), review outcomes (applicant), Admin unpublishing (course owner) and earned banner updates (Student). The bell shows the unread count and latest five, with a paginated All notifications page. Opening an item marks it read and follows its server-generated local destination. Individual/all read actions are CSRF-protected POSTs scoped to the current account. Admins have independent copies and read state. The recipient/event unique index prevents retry duplicates. Counts update when loading a page; no paid SMS/email channel or real-time push connection is introduced.

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

`tests/ExternalFlow` runs GitHub through fake provider HTTP backchannels with test-only client values and local email captures; it enforces the isolated `hackacad_uiresearch_20261016` database. It covers provider availability, PKCE/state/CSRF, private/unusable email, Student-only onboarding, mailbox verification, forged data, provider mix-up, safe redirects, first-password proof, explicit linking, sessions/history, lockout, disable and 2FA. Run it together with `tests/GoogleFlow` for regression coverage. No live OAuth or outbound messages are exercised by these tests. No database migration is needed: Identity's existing AspNetUserLogins table stores each provider binding.


### Account support and administrator security

Keep Account support and Security in the Administration sidebar, with queue counts and active-route markers. Read-only auditors see only Security, Login logs and Audit log. Security overview uses real metric totals and a paginated alert table; each alert has a status and investigation notes. A scoped Verify identity prompt appears before sensitive forms. Verification returns to the form and never silently resubmits its earlier draft.

Support has a separate centered public layout with no application-account menu. Start with mailbox verification, then show the user's paginated request list and conversation. Keep request status at the upper right and administrator/internal-note labels explicit. Admin conversation shows account access and history separately from replies; internal notes must never be rendered in the user view. Public replies and internal notes share one clearly labelled composer. Status colors retain text and have light/dark variants. Filters and buttons stay on one desktop row; tables fill their available measure. Reuse the existing numbered pager and time-zone formatting. No mobile layout or external visual dependency is added.
