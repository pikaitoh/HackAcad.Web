using System.Security.Cryptography;
using System.Text;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        if (!services.GetRequiredService<IWebHostEnvironment>().IsDevelopment()) return;

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var accounts = services.GetRequiredService<UserAccounts>();

        // 1. Seed Accounts
        foreach (var role in AccountRoles.All)
        {
            var email = $"demo.{role.ToLowerInvariant()}@hackacad.local";
            if (await users.FindByEmailAsync(email) is not null) continue;
            var result = await accounts.CreateAsync(new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = $"Demo {role}",
                EmailConfirmed = true
            }, "HackAcad@123!", role);

            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not create the {role} development fixture: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        var teacher = await users.FindByEmailAsync("demo.teacher@hackacad.local");
        var student = await users.FindByEmailAsync("demo.student@hackacad.local");
        if (teacher == null || student == null) return;

        // 2. Seed Sample Courses if not already present
        if (!await db.Courses.AnyAsync())
        {
            // --- Course 1: Linux Command Line Fundamentals & Security ---
            var course1 = new Course
            {
                Title = "Linux Command Line Fundamentals & Security",
                Description = "Master the Linux command line hierarchy, file permissions, log inspection, and security auditing with hands-on simulated labs and CTF forensics.",
                Category = "Linux Basics",
                ThumbnailUrl = "",
                IsPublished = true,
                TeacherId = teacher.Id,
                CreatedDate = DateTime.UtcNow.AddDays(-5)
            };
            db.Courses.Add(course1);
            await db.SaveChangesAsync();

            // Course 1 Lessons
            var c1l1 = new Lesson
            {
                CourseId = course1.CourseId,
                OrderIndex = 1,
                Title = "Introduction to Linux Shell & Directory Hierarchy",
                Content = @"# Linux Shell & Directory Navigation

The Linux command line is the primary operational environment for systems engineers, penetration testers, and security analysts. Unlike graphical interfaces, the shell provides deterministic control and scriptability.

### The Standard Filesystem Hierarchy (FHS)
* `/bin` & `/usr/bin`: Primary user command binaries (`ls`, `cat`, `grep`).
* `/etc`: Host-specific system-wide configuration files (e.g. `/etc/passwd`, `/etc/hosts`).
* `/var`: Variable data files, notably `/var/log` where system services write audit traces.
* `/home`: User personal working directories.

### Essential Navigation Commands
```bash
# Print your current working directory path
pwd

# List contents with file details and hidden files
ls -la

# Change directory to the system log store
cd /var/log

# Return to your personal home directory
cd ~
```

### Pro Tip
Always inspect file permissions before modifying or executing administrative utilities.",
                VideoUrl = "https://www.youtube.com/watch?v=s3ii48qYBxA"
            };

            var c1l2 = new Lesson
            {
                CourseId = course1.CourseId,
                OrderIndex = 2,
                Title = "File Permissions & Access Control (chmod, chown)",
                Content = @"# Linux Permissions & Access Control

Security in Unix-like systems revolves around user privileges and read/write/execute permissions.

### Understanding the Permission Triad
Permissions are divided into three tiers:
1. **User (u)**: The owning user account.
2. **Group (g)**: The owning security group.
3. **Others (o)**: Any other local user on the machine.

Each tier has three standard attributes:
* **Read (r / 4)**: Permission to open and read file contents.
* **Write (w / 2)**: Permission to modify or truncate the file.
* **Execute (x / 1)**: Permission to execute scripts or binaries.

```bash
# Set read, write, execute for user, read/execute for others
chmod 755 audit.sh

# Change ownership of a file to user student
sudo chown student:student /var/secret/audit.sh
```",
                VideoUrl = ""
            };

            var c1l3 = new Lesson
            {
                CourseId = course1.CourseId,
                OrderIndex = 3,
                Title = "Security Hardening & Log Auditing",
                Content = @"# Security Hardening & Log Auditing

When investigating security incidents or unauthorized intrusion attempts, log files are your primary source of ground truth.

### Key Linux Log Files
* `/var/log/auth.log` (or `secure` on RedHat): Tracks SSH logins, `sudo` elevation, and authentication failures.
* `/var/log/syslog`: General operating system event notifications.

```bash
# Search for suspicious failed login attempts
grep 'Failed password' /var/log/auth.log

# Extract unique malicious IP addresses
grep 'Failed password' /var/log/auth.log | awk '{print $11}' | sort | uniq -c
```",
                VideoUrl = ""
            };

            db.Lessons.AddRange(c1l1, c1l2, c1l3);

            // Course 1 CTF Challenge
            var c1Challenge = new Challenge
            {
                CourseId = course1.CourseId,
                Title = "Hidden in the Audit Logs",
                Category = "Forensics",
                Difficulty = "Easy",
                Points = 100,
                Description = @"An adversary attempted an SSH brute-force attack and left an encoded malicious payload trace in our server authentication logs.

1. Download the attached `auth_audit.log` evidence file.
2. Analyze the log entries using `grep` or a text editor to locate the intrusion trace.
3. Submit the secret flag found in the log record.",
                FilePath = "/uploads/ctf/auth_audit.log",
                FlagHash = Hash("flag{l1nux_log_4n4lys1s_m4st3r}"),
                IsPublished = true
            };
            db.Challenges.Add(c1Challenge);

            // Course 1 Simulation Task
            var c1SimJson = @"{
  ""allowedCommands"": [""help"", ""clear"", ""pwd"", ""ls"", ""cd"", ""cat"", ""grep"", ""chmod"", ""whoami"", ""id""],
  ""initialDirectory"": ""/home/student"",
  ""fileSystem"": {
    ""home"": {
      ""student"": {
        ""instructions.txt"": ""Lab Objective: Locate the audit script in /var/secret, set permissions to 755 using chmod, and inspect audit.sh to find the secret token.""
      }
    },
    ""var"": {
      ""secret"": {
        ""audit.sh"": ""#!/bin/bash\n# HackAcad Security Token Verification\necho 'Verification Key: flag{l1nux_sh3ll_m4st3ry}'""
      }
    }
  },
  ""objectives"": [
    { ""id"": ""nav_secret"", ""text"": ""Navigate to /var/secret directory"", ""type"": ""cwd"", ""target"": ""/var/secret"" },
    { ""id"": ""chmod_audit"", ""text"": ""Change permissions of audit.sh to 755"", ""type"": ""chmod"", ""target"": ""audit.sh"" },
    { ""id"": ""read_audit"", ""text"": ""Inspect audit.sh with cat to reveal token"", ""type"": ""cat"", ""target"": ""audit.sh"" }
  ]
}";
            var c1Sim = new SimulationTask
            {
                CourseId = course1.CourseId,
                Title = "Server Recon & Permission Audit Lab",
                Instructions = "Navigate into the protected /var/secret directory, update the file permissions of audit.sh, and read the script contents to complete the lab exercise.",
                VirtualFileSystemJson = c1SimJson,
                RewardFlagHash = "flag{l1nux_sh3ll_m4st3ry}",
                IsPublished = true
            };
            db.SimulationTasks.Add(c1Sim);

            // --- Course 2: Web Application Security & OWASP Top 10 ---
            var course2 = new Course
            {
                Title = "Web Application Security & OWASP Top 10",
                Description = "Explore the most critical web application vulnerabilities including SQL Injection, Cross-Site Scripting (XSS), and Insecure Direct Object References (IDOR).",
                Category = "Web Security",
                ThumbnailUrl = "",
                IsPublished = true,
                TeacherId = teacher.Id,
                CreatedDate = DateTime.UtcNow.AddDays(-3)
            };
            db.Courses.Add(course2);
            await db.SaveChangesAsync();

            var c2l1 = new Lesson
            {
                CourseId = course2.CourseId,
                OrderIndex = 1,
                Title = "SQL Injection (SQLi) Mechanics & Prevention",
                Content = @"# SQL Injection (SQLi) Fundamentals

SQL Injection occurs when untrusted user input is directly concatenated into database queries without parameterization or escaping.

### Vulnerable Query Example
```sql
-- Vulnerable inline concatenation:
SELECT * FROM Users WHERE Username = 'admin' AND Password = '' OR '1'='1';
```

### Remediation: Parameterized Queries
Always utilize parameterized statements / Object Relational Mappers (EF Core, Dapper) to guarantee input is treated strictly as literal data rather than executable SQL syntax.",
                VideoUrl = "https://www.youtube.com/watch?v=ciNHn38EyRc"
            };

            var c2l2 = new Lesson
            {
                CourseId = course2.CourseId,
                OrderIndex = 2,
                Title = "Cross-Site Scripting (XSS) & Content Security Policy",
                Content = @"# Cross-Site Scripting (XSS)

Cross-Site Scripting happens when an application includes untrusted data in a web page without proper validation or encoding.

### Types of XSS
1. **Reflected XSS**: Malicious script is reflected off a web server in an error message or search result.
2. **Stored XSS**: Script is permanently stored in a database (e.g. comment forum) and executed whenever a user views the record.
3. **DOM-based XSS**: Vulnerability exists client-side in DOM manipulation scripts (`innerHTML`, `document.write`).",
                VideoUrl = ""
            };

            db.Lessons.AddRange(c2l1, c2l2);

            var c2Challenge = new Challenge
            {
                CourseId = course2.CourseId,
                Title = "Client-Side Source Code Inspector",
                Category = "Web",
                Difficulty = "Easy",
                Points = 100,
                Description = @"A web developer wrote client-side logic to authenticate users and accidentally embedded the admin recovery token directly into the browser JavaScript.

1. Download the `auth_portal.js` script.
2. Inspect the script to find the base64-encoded token and decode it.
3. Submit the recovered flag.",
                FilePath = "/uploads/ctf/auth_portal.js",
                FlagHash = Hash("flag{cl13nt_s1d3_v4l1d4t10n_f41l}"),
                IsPublished = true
            };
            db.Challenges.Add(c2Challenge);

            var c2SimJson = @"{
  ""allowedCommands"": [""help"", ""clear"", ""ping"", ""nmap"", ""curl"", ""whoami"", ""pwd""],
  ""initialDirectory"": ""/home/student"",
  ""fileSystem"": {
    ""home"": {
      ""student"": {
        ""readme.txt"": ""Mission: Discover services on target.hackacad.local with nmap, then query the debug endpoint with curl.""
      }
    }
  },
  ""objectives"": [
    { ""id"": ""scan"", ""text"": ""Scan target with nmap"", ""type"": ""command"", ""target"": ""nmap"" },
    { ""id"": ""curl_api"", ""text"": ""Query debug API with curl"", ""type"": ""command"", ""target"": ""curl"" }
  ]
}";
            var c2Sim = new SimulationTask
            {
                CourseId = course2.CourseId,
                Title = "Web Reconnaissance with cURL & Nmap",
                Instructions = "Use the simulated network exploration tools to scan the host and make an HTTP request to query the debug API.",
                VirtualFileSystemJson = c2SimJson,
                RewardFlagHash = "flag{w3b_r3c0n_curl_m4st3r}",
                IsPublished = true
            };
            db.SimulationTasks.Add(c2Sim);

            // --- Course 3: Cryptography & Secret Ciphers ---
            var course3 = new Course
            {
                Title = "Cryptography & Secret Ciphers",
                Description = "Explore classical encryption algorithms, modern cryptographic hashing, and cipher cracking techniques.",
                Category = "Cryptography",
                ThumbnailUrl = "",
                IsPublished = true,
                TeacherId = teacher.Id,
                CreatedDate = DateTime.UtcNow.AddDays(-1)
            };
            db.Courses.Add(course3);
            await db.SaveChangesAsync();

            var c3l1 = new Lesson
            {
                CourseId = course3.CourseId,
                OrderIndex = 1,
                Title = "Classical Ciphers: Caesar and Substitution",
                Content = @"# Classical Ciphers & Shift Ciphers

The Caesar Cipher is one of the oldest known substitution ciphers. Each letter in the plaintext is shifted a fixed number of positions down the alphabet.

### Shift Formula
$$E_n(x) = (x + n) \pmod{26}$$

For example, with a shift of 3, `A` becomes `D`, `B` becomes `E`, and `Z` wraps around to `C`.",
                VideoUrl = ""
            };
            db.Lessons.Add(c3l1);

            var c3Challenge = new Challenge
            {
                CourseId = course3.CourseId,
                Title = "The Caesar Cipher Relic",
                Category = "Crypto",
                Difficulty = "Easy",
                Points = 50,
                Description = @"We intercepted an encrypted message shifted by 3 positions:

`IODJ{f43s4u_c1ph3u_d3c0d3d}`

Shift each letter backwards by 3 to recover the plaintext flag.",
                FlagHash = Hash("flag{c43s4r_c1ph3r_d3c0d3d}"),
                IsPublished = true
            };
            db.Challenges.Add(c3Challenge);

            await db.SaveChangesAsync();

            // 3. Pre-enroll demo.student into Course 1 with Lesson 1 completed and CTF solved
            var studentEnrollment = new Enrollment
            {
                UserId = student.Id,
                CourseId = course1.CourseId,
                Status = "InProgress",
                EnrolledDate = DateTime.UtcNow.AddDays(-2),
                LastLessonId = c1l2.LessonId
            };
            db.Enrollments.Add(studentEnrollment);
            await db.SaveChangesAsync();

            // Mark Lesson 1 complete
            db.ProgressRecords.Add(new Progress
            {
                EnrollmentId = studentEnrollment.EnrollmentId,
                ContentType = ProgressContentType.Lesson,
                LessonId = c1l1.LessonId,
                IsCompleted = true,
                LastUpdatedDate = DateTime.UtcNow.AddDays(-2)
            });

            // Mark CTF 1 solved
            db.ProgressRecords.Add(new Progress
            {
                EnrollmentId = studentEnrollment.EnrollmentId,
                ContentType = ProgressContentType.Challenge,
                ChallengeId = c1Challenge.ChallengeId,
                IsCompleted = true,
                LastUpdatedDate = DateTime.UtcNow.AddDays(-1)
            });

            db.ChallengeSubmissions.Add(new ChallengeSubmission
            {
                UserId = student.Id,
                ChallengeId = c1Challenge.ChallengeId,
                SubmittedFlag = "flag{l1nux_log_4n4lys1s_m4st3r}",
                IsCorrect = true,
                SubmittedDate = DateTime.UtcNow.AddDays(-1)
            });

            await db.SaveChangesAsync();
        }
    }

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
