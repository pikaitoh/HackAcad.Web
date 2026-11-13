# HackAcad Web

HackAcad is a cybersecurity learning website. Learners can study courses, solve CTF challenges, complete simulated terminal labs, track progress, and earn badges. Instructors can also use the learning features while managing their own courses. Admins review courses and manage the platform.

## What you can do

### Students and instructors

- Browse published courses, enroll, and continue where you left off.
- Complete lessons, submit CTF flags, and work through simulated terminal labs.
- View course progress, learning activity, score points, and earned badges.
- Save courses to find them again later.
- Review badge requirements and see progress toward locked badges.
- Manage profile and account settings, notifications, and security options.

Instructors have these learner features too. They can enroll in other instructors’ courses, but not in their own courses. In addition, instructors can create and manage courses, lessons, challenges, labs, and course notebooks. Their course page shows enrolled learners, progress, latest activity, and elapsed time from enrollment to the learner’s latest completed activity. This is elapsed calendar time, not a timer of hours actively spent learning.

### Admins

- Review instructor applications and submitted courses.
- Approve courses before instructors publish them.
- Manage user accounts and review platform activity, security alerts, and support requests.

## How the website works

The website uses ASP.NET Core MVC. A controller handles a page request, applies the relevant rules, and reads or saves data through Entity Framework Core. Razor views build the pages, while CSS and JavaScript provide the layout and browser interactions.

MySQL stores accounts, courses, enrollments, learning progress, saved courses, badge awards, notifications, and other site data. ASP.NET Core Identity handles sign-in and account security. The app checks the user’s role before showing role-specific pages and actions.

The terminal labs are simulated exercises inside HackAcad; they do not open a real computer terminal.

## Run the website on your computer

### Requirements

- .NET 8 SDK
- MySQL 8 or a compatible MySQL/MariaDB server
- A MySQL account that can create and update tables in the project database

### 1. Start MySQL and create a database

Create an empty database. For example, in MySQL Workbench or the MySQL command line, run:

```sql
CREATE DATABASE hackacad_db;
```

### 2. Add your local database settings

In the same folder as `HackAcad.Web.csproj`, create a file named `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=127.0.0.1;port=3306;database=hackacad_db;user=YOUR_USER;password=YOUR_PASSWORD;"
  },
  "Email": {
    "PublicBaseUrl": "http://localhost:5000"
  }
}
```

Replace `YOUR_USER` and `YOUR_PASSWORD` with your local MySQL login. This local settings file is excluded from Git, so do not commit credentials. Keep the database server running while using the website.

### 3. Restore packages and start the app

Open PowerShell or a terminal in the project folder and run:

```powershell
dotnet restore
dotnet run
```

Open **http://localhost:5000** in your browser. On startup, the app applies its saved database migrations to create or update the tables. You do not need to import `hackacad_schema_only.sql` to run the app; that file is for viewing or generating the database diagram.

Press **Ctrl+C** in the terminal to stop the app. The project does not automatically create demo accounts or sample courses. Existing accounts can sign in; new accounts need email verification. To send real verification emails, configure the Gmail API credentials used by the project. Google/GitHub sign-in and Twilio SMS features also need their own credentials; they are optional integrations.

## Main folders and files

- `Controllers/` — handles requests and application actions.
- `Views/` — Razor page templates shown in the browser.
- `Models/` — database entities and page data models.
- `Data/` — database context, role checks, and EF Core migrations.
- `Services/` — shared rules for badges, activity, account security, email, and other features.
- `wwwroot/` — CSS, JavaScript, and image files used by the pages.
- `Program.cs` — configures the app, database, sign-in, services, and routes.
- `appsettings.Development.json` — your local settings; create this file yourself and keep it private.

## If the app cannot connect to MySQL

Check that MySQL is running, the database name and login in `appsettings.Development.json` are correct, and the MySQL account can create and update tables. Restart the app after changing local settings.
