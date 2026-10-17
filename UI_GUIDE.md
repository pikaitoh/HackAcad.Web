# HackAcad UI guide for the team

This guide records the shared UI used by all current pages, based on the Home design selected by the user. The current user owns Admin and User/Authentication. Course authoring, CTF, simulation, badges and progress remain with their module owners. Use the existing shared shell and controls when implementing those modules. Keep the selected mint, teal, yellow and Figtree direction consistent when adding module pages.

This guide contains the shared design tokens and visual rules. Start a Development instance and open `/Home/UiGuide` for button, field, role/status and empty-state examples. Open that route directly; it has no link in the site footer and returns 404 outside Development. Full form/table examples are in `Views/Account/Register.cshtml` and `Views/Admin/Users.cshtml`.

## Where the shared UI lives

| File | Responsibility |
| --- | --- |
| `Views/Shared/_Layout.cshtml` | Bootstrap 5.3.3, shared font preload, header, skip link, main, footer, CSS and scripts. |
| `Views/Shared/_Brand.cshtml` | Shared open-book/code-bracket mark and HackAcad wordmark (no trailing dot); use the partial instead of making a new logo. |
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

The layout loads Bootstrap first, then `site.css`, `accounts.css`, `admin.css` and `learning.css`, in that order, on every page. Module views normally inherit `_Layout` through `Views/_ViewStart.cshtml`; do not copy the complete HTML shell. The Admin layout is for Admin pages, not a shell for student/teacher learning modules. Custom layouts that need scripts must forward the optional `Scripts` section as the existing nested layouts do.

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

Use Figtree everywhere. The shared layout preloads the self-hosted variable font once; its declaration is in `site.css` and its license is `wwwroot/fonts/OFL-Figtree.txt`. Body text is `.9375rem`, ordinary page titles `2rem` at weight 750, section titles `1.25rem`, smaller headings `1.0625rem`, labels/buttons `.875rem`, helper text `.8125rem`, and compact metadata `.75rem`. Mobile form controls use `1rem`. Home keeps its larger display title (`3.5rem` desktop); account and Admin screens keep task-sized headings and practical density.

Use yellow primary buttons with deep teal text, white/outlined secondary buttons, teal links and selected navigation, and red destructive actions. Buttons are pills; fields use `12px` corners and panels use `16px`. The shared header uses deep teal (`--ha-ink`) to separate navigation from the mint canvas. Its wordmark/navigation are light, hover/current links and focus rings use yellow, and the public registration button is yellow/teal. The book mark retains its existing colours on a small mint backing; avatar menus remain white with teal text. The footer is dark teal. Authentication introductions, the Dashboard next-step panel and the Admin summary use dark teal with light text and yellow accents. Focus rings are teal on light surfaces and yellow on dark surfaces. Do not turn status colours into decoration.

Use Bootstrap spacing utilities where they already fit: `mb-3` for ordinary field groups, `mb-4` before a form submit or between larger groups, `mb-5` between guide sections, and `gap-3` for a normal action gap. Shared container padding is `2rem` desktop / `1.25rem` mobile. Use `.form-actions` and `.table-actions` for wrapping actions instead of manual spaces. The CSS contains recurring rem values rather than a separate spacing-variable system.

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

This is a structure example: use your module's real controller/action. Add one meaningful `h1`, then logical heading levels. `.page-heading` stacks its title and action on mobile. `.section-heading` aligns a section title and optional short link. Existing `.surface-panel` groups related content; `.form-measure` provides a bordered form up to `38rem` wide. These classes are already available because all common CSS files load globally.

## Actions and feedback

On `Profile`, keep picture controls on the left, with outlined Upload picture and Remove picture buttons in a wrapping action row. The right-hand card groups Profile details (role and joined date) above the Username and Full name form, separated by one divider. Full name sits below Username, is optional and accepts up to 100 characters; it does not change the email login. On mobile the picture section precedes the details card. Omit the Dashboard / Profile breadcrumb and security actions. Email management and verification status belong to Manage account → Email address.

The signed-in header replaces My profile and the standalone logout button with an avatar dropdown (`_LoginPartial`, `account-menu.js`, `accounts.css`). Keep only working destinations: View profile, Manage account, and POST-only Log out. Use the uploaded photo or a grapheme-aware name initial; never show a broken default photo. Native `details`/`summary` supports keyboard activation without JavaScript; the script adds outside-click and Escape dismissal. Signed-in mobile navigation keeps Dashboard and the avatar directly visible. Manage account uses its shared sidebar for security functions, with the current mobile function scrolled into view without moving the page vertically. The menu opens inward from the right edge and has readable text plus matching line icons. Do not add placeholder menu links for Badge/Saved/Rewards or unrelated commercial services.

`Profile` manages picture upload/removal and username. `Settings` manages email status, the password link, optional phone number and SMS OTP. Keep phone pending/verified states separate; requesting a code never replaces the saved number. OTP uses a six-digit text input with `inputmode="numeric"` and `autocomplete="one-time-code"`, with inline feedback, a cancel action and resend guidance. Show an explicit Development test-mode notice when messages are captured locally; never display the code itself in the page. Phone verification is a contact check, not phone login or SMS 2FA. Avatar constraints and crop behaviour are described beside the file input; actual images are decoded/re-encoded by the server, not trusted by filename or MIME type.

Use `data-validated-form` on ordinary forms such as Admin course review to reuse the shared validation script; existing account forms retain `data-account-form`. It reads Razor's validation rules for inputs, selects and textareas, puts each error beside its field and prevents duplicate native browser bubbles when JavaScript is available. Server validation remains required. For course review, Title is required (150 characters maximum), Category optional (50) and Description optional (1,000). Surrounding whitespace is trimmed; blank optional metadata is stored as null so the catalogue can display its empty-category fallback. Keep each field's help/error IDs in `aria-describedby` and use `aria-live="polite"` on the error span.

Email verification reuses `_AuthLayout` and existing form/error styles. Registration leads to `VerificationPending`, not a signed-in Dashboard. `ResendVerification` uses the same inline Email validation as registration, a generic submission status to avoid revealing whether an account exists, and a Back to log in link. `ConfirmEmail` shows success with Log in or an invalid/expired state with Request a new link. Never show a verification URL/token in the browser as a development shortcut. Keep disabled access and unverified email distinct; Admin Users shows `Email not verified` under the address when appropriate. Admin email edits explain the re-verification and sign-out consequences before submission.

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

`.role-badge` with `data-role="Admin"`, `Teacher` or `Student` displays actual account roles: Admin uses purple (`#624080` on `#eee6f7`), Teacher yellow, and Student green. Keep the role text visible so colour is not the only distinction. `.status-label.status-active` / `.status-label.status-disabled` displays access state with a dot and readable label. Do not reuse these account meanings to imply unrelated course completion states. A new module state needs its own explicit label and scoped pattern.

## Learning patterns and module handoff

The learning patterns draw on TryHackMe's clear next-step hierarchy, Cisco NetAcad's course information and outlines, and HTB Academy's content structure. They inherit the same Figtree family, mint/teal palette and yellow primary controls as the rest of HackAcad.

| Pattern | Existing classes / reference | Integration requirement |
| --- | --- | --- |
| Task-first dashboard | `.dashboard-layout`, `.workspace-start`, `.dashboard-activity`, `.activity-summary`, `.learning-empty`; `Views/Account/Dashboard.cshtml` | Admin uses `/Admin` as its Dashboard; the account Dashboard endpoint redirects Admin there. Student/Teacher keep the existing account dashboard and profile action for their module owners to extend. Learning counts come from the account model. Add a course action only when its destination exists. |
| Account/security shortcuts | `.dashboard-account`, `.account-shortcuts`, `.account-access` | Keep real profile/password links and role-specific access descriptions. |
| Admin review queue | `.review-queue`; `Views/Admin/Index.cshtml` | Use `DisabledUsers` and `Courses - PublishedCourses`; links target existing Users `status=disabled` and Courses `status=draft` filters. Unpublished courses are not necessarily awaiting review. |
| Expandable outline | `.learning-outline`, `.outline-content`, `.outcome-list` | Keep native `details` / `summary`, logical headings and definition-list learning goals. The guide shows illustrative lesson content; Home introduces learning formats in its separate landing theme. |
| Course summary | `.course-preview`, `.course-metadata` | Supply real title, description, category, teacher and lesson count from the owning module. Keep metadata text readable; icons supplement it. |
| Learning progress | `.learning-progress` with labelled native `progress` | Supply reliable completed/total values and readable text. Track height is `7px`, radius `4px`. Without a reliable denominator, show a completed count instead of a percentage. |
| Example notice | `.guide-example-note`, `.guide-learning` | Keep illustrative course, teacher, lesson and progress data explicitly labelled in Development UI guide. |

The project currently has no `CourseController` or resume engine and no duration/difficulty schema. UI-guide course and progress values are examples, not saved account records. Course, progress, CTF, simulation and badge owners must provide real data, access/ownership checks and working routes before connecting learning actions. `Enrollment.LastLessonId` is only an integration clue: validate course membership, enrolment and access before using it to resume. Omit unsupported duration, difficulty and prerequisites until the owning module supplies their data. Do not add fake enrol/resume buttons.

## Forms and validation

Use Username for the user-facing profile name everywhere (including registration and Admin forms); the existing DisplayName model/property is retained. Login continues to use email. Change email opens a dedicated form with New email and Current password. Keep validation inline, explain that the current email remains usable until confirmation, and use buttons for Send confirmation link and Back to profile. Confirmation signs the user out; invalid/used links offer a profile recovery action. Never present a pending or undelivered email as already changed.

Use Bootstrap `.form-label`, `.form-control`, `.form-select`, `.form-check-input`, `.form-check-label` and `.form-text` on native controls. Keep persistent labels linked to unique input IDs. Use appropriate input types, autocomplete, required/max-length constraints and model validation. Do not add a second validation library.

Follow the existing Register view: an `asp-validation-summary="ModelOnly"` element with `.validation-summary` and `role="alert"`; an `asp-validation-for` span with `.field-error` per field; `aria-describedby` pointing to the field's help/error IDs; and `aria-invalid="true"` when ModelState has an error. ASP.NET applies `.input-validation-error` for the server error border. Keep POST forms using Tag Helpers so existing antiforgery behavior is preserved.

For account forms, `data-account-form` enables the shared script to read server-generated DataAnnotation rules and display errors only in each field's `.field-error` span. It suppresses native validation popups while active, validates on submit, rechecks invalid fields during editing, and focuses the first error. Without JavaScript, native constraints and server validation remain available. Identity email/password errors are mapped to their fields; the form summary is reserved for form-level failures. Delete account uses the same inline feedback and checks the confirmation against the selected account's stored email (trimmed, case-insensitive), not a new-email format rule. This also allows correcting/removing legacy accounts with older email formats. Keep the server-side equality check and deletion safeguards. Server validation, authorization and ownership checks remain required regardless of the UI. Usernames are trimmed, 2–50 characters, allow Unicode letters/numbers and common name punctuation, and need not be unique. Email addresses are trimmed and require a dotted domain with a letter suffix; format validation does not verify mailbox ownership. Passwords retain the existing 8–100 character, ASCII uppercase/lowercase, digit and symbol policy. Keep password policy guidance accurate; copy it from the current model rather than weakening the policy through presentation changes.

## Navigation, tables and responsive behavior

Coordinate shared navigation links in `_LoginPartial` and reuse `_Brand`. Apply `aria-current="page"` to the active destination, preserve the Bootstrap navbar's accessible toggle, and retain the skip link / `main-content` target. Client-side visibility does not replace controller authorization.

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

The table has a `42rem` minimum width and scrolls horizontally within its wrapper. Use `.record-secondary` beneath a primary name and `.table-actions` for row links; include the record name in accessible action labels. Use `.filter-bar` and `.filter-search` for labelled GET filters, preserve filter values through pagination links, and use `.pagination-bar` for page context plus Previous/Next navigation. Show `.empty-state` when no records match instead of an empty table.

Common layout changes use max-width breakpoints `1199px`, `991px` and `767px`. At mobile widths, headings stack, filters become full-width rows, account/profile columns collapse, and Admin navigation wraps horizontally. The authentication aside is hidden so the form takes priority. Bootstrap navbar collapse begins below `768px`. Keep long text wrapping; do not create page-wide overflow to make a table fit.

Before handing off a module page, check a phone-sized and a desktop viewport, keyboard navigation/focus, native and server validation errors, empty and long records, and visible active/status labels. Respect the existing reduced-motion rule. Add only routes and functionality actually implemented by your module; Home offers visitors registration or login and sends signed-in users to their Dashboard.

## Home page

Home follows the user's OpenLearning and Expertise Pays references: pale mint, deep teal text, a centred photo-led hero, yellow primary actions, rounded secondary controls, a dark teal learning-format band and native FAQ disclosures. This direction now applies to every existing page. `.home-landing` scopes only the Home composition; account and Admin pages inherit the same shared palette, font, header and controls while retaining task-oriented layouts.

`Views/Home/Index.cshtml` sets the body class and loads `home.css` through the shared layout's optional `Styles` section. The shared layout already preloads Figtree, so Home does not load a second font. The headline is `3.5rem` desktop, `3rem` up to `1199px`, and `2.375rem` up to `767px`; weight 800, with a 1.12 desktop line-height. The landing colours inherit the shared tokens: ink `#193e46`, green `#33796e`, mint `#f2fbf7`, action yellow `#f2cf45`, and dark band `#28545a`.

The transparent learner image (`wwwroot/images/home-student.png`) was generated with OpenAI image_gen on 2026-10-16 as an illustrative learning scene, not a real user or testimonial. Its exact prompt and origin are embedded in PNG metadata. Do not present it as customer evidence. The learning-format preview contains descriptions, not user records. Terminal output is explicitly an example exercise; it is not an interactive terminal. Use genuine records before adding course-specific actions or statistics.

Home is for signed-out visitors only. `HomeController.Index` redirects authenticated users to their Dashboard, including direct `/` and `/Home/Index` visits. Signed-in navigation omits Home. Admin has one Dashboard link and logo targeting `/Admin`; Student/Teacher target the existing account Dashboard. The default sign-in destination resolves through `AccountController.Dashboard`; valid local return URLs still take priority. There is no separate Administration link duplicating Admin Dashboard; after logout the visitor returns to Home. Keep registration and login links working. Home's FAQ answers describe the existing account flows. New sections must not introduce invented learner counts, customer reviews, certificates or partner logos. Responsive layouts reposition the hero's learner and preview, then stack the supporting content. Keep the learner photo extended below the hero crop so the desk has a straight horizontal edge flush with the learning-format band. Respect reduced motion and visible keyboard focus.


## Personal security pages

View profile keeps picture controls on the left and Profile details (Role, Joined, Username and Full name) on the right. Security functions belong to Manage account. Its shared `_ManageAccountLayout` / `_ManageAccountNav` provides Email address, Phone number, Password, eligible Two-factor authentication, Login history and Devices & sessions. Email status uses readable Verified/Not verified text with `.status-label` on the email page. Keep My admin activity in Administration, and action controls as buttons. Do not add a View profile button beneath the Manage account sidebar; use the avatar menu for that destination. On mobile, the settings sidebar becomes a horizontal, scrollable menu with visible active-page styling; keyboard and touch can reach every function. Use `.account-workspace`, `.account-sidebar`, `.account-main` and `.account-panel` instead of nesting a second `.container.module-shell` inside the layout.

Devices & sessions combines a plain list of real current-account logins with password-confirmed per-device and bulk sign-out. Mark the current row `This device`; it never has a Sign out button. Use `.session-form`, `.session-password` and `.session-row`; mobile rows stack. Explain that active means usable, not online, and device names are estimates. History/activity use the existing `.module-table`, scoped scrollable regions, table headers/captions and real pagination; do not invent earlier records. Display these timestamps in Malaysia time (UTC+8), retaining UTC storage.

Authenticator setup uses local QR PNGs, a wrapping manual key and a six-digit field. Never use a remote QR image endpoint or include the key in a link. Recovery-code results are private, no-cache and shown once after generation; they are not a static guide/example. New forms retain shared inline validation, descriptive labels and field help/error associations. Admin activity inherits `_AdminLayout` and `_AdminNav`; do not duplicate the sidebar inside the view.
