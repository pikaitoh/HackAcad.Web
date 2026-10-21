using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HackAcad.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class UseReadableTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MySQL names every primary key PRIMARY. Keep existing keys and AUTO_INCREMENT
            // values intact; only table, index and foreign-key names change.

            migrationBuilder.DropForeignKey(
                name: "FK_AccountSessions_AspNetUsers_UserId",
                table: "AccountSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_AdminActivities_AspNetUsers_ActorUserId",
                table: "AdminActivities");

            migrationBuilder.DropForeignKey(
                name: "FK_Admins_AspNetUsers_UserId",
                table: "Admins");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                table: "AspNetUserClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                table: "AspNetUserLogins");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                table: "AspNetUserTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_BannerRewards_AspNetUsers_UserId",
                table: "BannerRewards");

            migrationBuilder.DropForeignKey(
                name: "FK_Challenges_Courses_CourseId",
                table: "Challenges");

            migrationBuilder.DropForeignKey(
                name: "FK_Courses_Instructors_InstructorId",
                table: "Courses");

            migrationBuilder.DropForeignKey(
                name: "FK_Enrollments_Lessons_LastLessonId",
                table: "Enrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_InstructorApplications_AspNetUsers_UserId",
                table: "InstructorApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_InstructorReviews_InstructorApplications_ApplicationId",
                table: "InstructorReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_Instructors_AspNetUsers_UserId",
                table: "Instructors");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Courses_CourseId",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_LoginRecords_AspNetUsers_UserId",
                table: "LoginRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_ProgressRecords_Challenges_ChallengeId",
                table: "ProgressRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_ProgressRecords_Enrollments_EnrollmentId",
                table: "ProgressRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_ProgressRecords_Lessons_LessonId",
                table: "ProgressRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_ProgressRecords_SimulationTasks_SimulationTaskId",
                table: "ProgressRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedCourses_AspNetUsers_UserId",
                table: "SavedCourses");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedCourses_Courses_CourseId",
                table: "SavedCourses");

            migrationBuilder.DropForeignKey(
                name: "FK_SimulationTasks_Courses_CourseId",
                table: "SimulationTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Students_AspNetUsers_UserId",
                table: "Students");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportAccesses_AspNetUsers_UserId",
                table: "SupportAccesses");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportMessages_SupportTickets_TicketId",
                table: "SupportMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_AspNetUsers_UserId",
                table: "SupportTickets");

            migrationBuilder.DropForeignKey(
                name: "FK_UserNotifications_AspNetUsers_UserId",
                table: "UserNotifications");

            migrationBuilder.RenameTable(name: "Students", newName: "__rename_students");
            migrationBuilder.RenameTable(name: "__rename_students", newName: "students");

            migrationBuilder.RenameTable(name: "Lessons", newName: "__rename_lessons");
            migrationBuilder.RenameTable(name: "__rename_lessons", newName: "lessons");

            migrationBuilder.RenameTable(name: "Instructors", newName: "__rename_instructors");
            migrationBuilder.RenameTable(name: "__rename_instructors", newName: "instructors");

            migrationBuilder.RenameTable(name: "Enrollments", newName: "__rename_enrollments");
            migrationBuilder.RenameTable(name: "__rename_enrollments", newName: "enrollments");

            migrationBuilder.RenameTable(name: "Courses", newName: "__rename_courses");
            migrationBuilder.RenameTable(name: "__rename_courses", newName: "courses");

            migrationBuilder.RenameTable(name: "Challenges", newName: "__rename_challenges");
            migrationBuilder.RenameTable(name: "__rename_challenges", newName: "challenges");

            migrationBuilder.RenameTable(name: "Admins", newName: "__rename_admins");
            migrationBuilder.RenameTable(name: "__rename_admins", newName: "admins");

            migrationBuilder.RenameTable(
                name: "UserNotifications",
                newName: "user_notifications");

            migrationBuilder.RenameTable(
                name: "SupportTickets",
                newName: "support_tickets");

            migrationBuilder.RenameTable(
                name: "SupportMessages",
                newName: "support_messages");

            migrationBuilder.RenameTable(
                name: "SupportAccesses",
                newName: "support_accesses");

            migrationBuilder.RenameTable(
                name: "SimulationTasks",
                newName: "simulation_tasks");

            migrationBuilder.RenameTable(
                name: "SecurityEvents",
                newName: "security_events");

            migrationBuilder.RenameTable(
                name: "SecurityAlerts",
                newName: "security_alerts");

            migrationBuilder.RenameTable(
                name: "SavedCourses",
                newName: "saved_courses");

            migrationBuilder.RenameTable(
                name: "ProgressRecords",
                newName: "progress_records");

            migrationBuilder.RenameTable(
                name: "LoginRecords",
                newName: "login_records");

            migrationBuilder.RenameTable(
                name: "InstructorReviews",
                newName: "instructor_reviews");

            migrationBuilder.RenameTable(
                name: "InstructorApplications",
                newName: "instructor_applications");

            migrationBuilder.RenameTable(
                name: "ChallengeSubmissions",
                newName: "challenge_submissions");

            migrationBuilder.RenameTable(
                name: "BannerRewards",
                newName: "banner_rewards");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                newName: "user_tokens");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                newName: "users");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                newName: "user_logins");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                newName: "user_claims");

            migrationBuilder.RenameTable(
                name: "AdminActivities",
                newName: "admin_activities");

            migrationBuilder.RenameTable(
                name: "AccountSessions",
                newName: "account_sessions");

            migrationBuilder.RenameIndex(name: "IX_Lessons_CourseId", table: "lessons", newName: "IX_rename_794ade4fd5d9");
            migrationBuilder.RenameIndex(name: "IX_rename_794ade4fd5d9", table: "lessons", newName: "IX_lessons_CourseId");

            migrationBuilder.RenameIndex(name: "IX_Enrollments_UserId_CourseId", table: "enrollments", newName: "IX_rename_acf5ae38b9d1");
            migrationBuilder.RenameIndex(name: "IX_rename_acf5ae38b9d1", table: "enrollments", newName: "IX_enrollments_UserId_CourseId");

            migrationBuilder.RenameIndex(name: "IX_Enrollments_LastLessonId", table: "enrollments", newName: "IX_rename_16ed37543283");
            migrationBuilder.RenameIndex(name: "IX_rename_16ed37543283", table: "enrollments", newName: "IX_enrollments_LastLessonId");

            migrationBuilder.RenameIndex(name: "IX_Courses_InstructorId", table: "courses", newName: "IX_rename_7e4bd87bb195");
            migrationBuilder.RenameIndex(name: "IX_rename_7e4bd87bb195", table: "courses", newName: "IX_courses_InstructorId");

            migrationBuilder.RenameIndex(name: "IX_Challenges_CourseId", table: "challenges", newName: "IX_rename_3d16057a1889");
            migrationBuilder.RenameIndex(name: "IX_rename_3d16057a1889", table: "challenges", newName: "IX_challenges_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_UserNotifications_UserId_ReadUtc_CreatedUtc",
                table: "user_notifications",
                newName: "IX_user_notifications_UserId_ReadUtc_CreatedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_UserNotifications_UserId_EventKey",
                table: "user_notifications",
                newName: "IX_user_notifications_UserId_EventKey");

            migrationBuilder.RenameIndex(
                name: "IX_SupportTickets_UserId_UpdatedUtc",
                table: "support_tickets",
                newName: "IX_support_tickets_UserId_UpdatedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_SupportMessages_TicketId",
                table: "support_messages",
                newName: "IX_support_messages_TicketId");

            migrationBuilder.RenameIndex(
                name: "IX_SupportAccesses_UserId_CreatedUtc",
                table: "support_accesses",
                newName: "IX_support_accesses_UserId_CreatedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_SupportAccesses_TokenHash",
                table: "support_accesses",
                newName: "IX_support_accesses_TokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_SimulationTasks_CourseId",
                table: "simulation_tasks",
                newName: "IX_simulation_tasks_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_SecurityEvents_SubjectHash_OccurredUtc",
                table: "security_events",
                newName: "IX_security_events_SubjectHash_OccurredUtc");

            migrationBuilder.RenameIndex(
                name: "IX_SecurityEvents_IpAddress_OccurredUtc",
                table: "security_events",
                newName: "IX_security_events_IpAddress_OccurredUtc");

            migrationBuilder.RenameIndex(
                name: "IX_SecurityAlerts_Status_CreatedUtc",
                table: "security_alerts",
                newName: "IX_security_alerts_Status_CreatedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_SecurityAlerts_EventKey",
                table: "security_alerts",
                newName: "IX_security_alerts_EventKey");

            migrationBuilder.RenameIndex(
                name: "IX_SavedCourses_UserId_SavedUtc",
                table: "saved_courses",
                newName: "IX_saved_courses_UserId_SavedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_SavedCourses_UserId_CourseId",
                table: "saved_courses",
                newName: "IX_saved_courses_UserId_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_SavedCourses_CourseId",
                table: "saved_courses",
                newName: "IX_saved_courses_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_ProgressRecords_SimulationTaskId",
                table: "progress_records",
                newName: "IX_progress_records_SimulationTaskId");

            migrationBuilder.RenameIndex(
                name: "IX_ProgressRecords_LessonId",
                table: "progress_records",
                newName: "IX_progress_records_LessonId");

            migrationBuilder.RenameIndex(
                name: "IX_ProgressRecords_EnrollmentId_SimulationTaskId",
                table: "progress_records",
                newName: "IX_progress_records_EnrollmentId_SimulationTaskId");

            migrationBuilder.RenameIndex(
                name: "IX_ProgressRecords_EnrollmentId_LessonId",
                table: "progress_records",
                newName: "IX_progress_records_EnrollmentId_LessonId");

            migrationBuilder.RenameIndex(
                name: "IX_ProgressRecords_EnrollmentId_ChallengeId",
                table: "progress_records",
                newName: "IX_progress_records_EnrollmentId_ChallengeId");

            migrationBuilder.RenameIndex(
                name: "IX_ProgressRecords_ChallengeId",
                table: "progress_records",
                newName: "IX_progress_records_ChallengeId");

            migrationBuilder.RenameIndex(
                name: "IX_LoginRecords_UserId_OccurredUtc",
                table: "login_records",
                newName: "IX_login_records_UserId_OccurredUtc");

            migrationBuilder.RenameIndex(
                name: "IX_InstructorReviews_ApplicationId",
                table: "instructor_reviews",
                newName: "IX_instructor_reviews_ApplicationId");

            migrationBuilder.RenameIndex(
                name: "IX_InstructorApplications_UserId_Revision",
                table: "instructor_applications",
                newName: "IX_instructor_applications_UserId_Revision");

            migrationBuilder.RenameIndex(
                name: "IX_InstructorApplications_Status_SubmittedUtc",
                table: "instructor_applications",
                newName: "IX_instructor_applications_Status_SubmittedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_ChallengeSubmissions_UserId_ChallengeId",
                table: "challenge_submissions",
                newName: "IX_challenge_submissions_UserId_ChallengeId");

            migrationBuilder.RenameIndex(
                name: "IX_BannerRewards_UserId_CourseId",
                table: "banner_rewards",
                newName: "IX_banner_rewards_UserId_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUsers_PhoneNumber",
                table: "users",
                newName: "IX_users_PhoneNumber");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "user_logins",
                newName: "IX_user_logins_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "user_claims",
                newName: "IX_user_claims_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_AdminActivities_ActorUserId_OccurredUtc",
                table: "admin_activities",
                newName: "IX_admin_activities_ActorUserId_OccurredUtc");

            migrationBuilder.RenameIndex(
                name: "IX_AccountSessions_UserId_ExpiresUtc",
                table: "account_sessions",
                newName: "IX_account_sessions_UserId_ExpiresUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_account_sessions_users_UserId",
                table: "account_sessions",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_admin_activities_users_ActorUserId",
                table: "admin_activities",
                column: "ActorUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_admins_users_UserId",
                table: "admins",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_banner_rewards_users_UserId",
                table: "banner_rewards",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_challenges_courses_CourseId",
                table: "challenges",
                column: "CourseId",
                principalTable: "courses",
                principalColumn: "CourseId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_courses_instructors_InstructorId",
                table: "courses",
                column: "InstructorId",
                principalTable: "instructors",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_enrollments_lessons_LastLessonId",
                table: "enrollments",
                column: "LastLessonId",
                principalTable: "lessons",
                principalColumn: "LessonId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_instructor_applications_users_UserId",
                table: "instructor_applications",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_instructor_reviews_instructor_applications_ApplicationId",
                table: "instructor_reviews",
                column: "ApplicationId",
                principalTable: "instructor_applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_instructors_users_UserId",
                table: "instructors",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_lessons_courses_CourseId",
                table: "lessons",
                column: "CourseId",
                principalTable: "courses",
                principalColumn: "CourseId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_login_records_users_UserId",
                table: "login_records",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_progress_records_challenges_ChallengeId",
                table: "progress_records",
                column: "ChallengeId",
                principalTable: "challenges",
                principalColumn: "ChallengeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_progress_records_enrollments_EnrollmentId",
                table: "progress_records",
                column: "EnrollmentId",
                principalTable: "enrollments",
                principalColumn: "EnrollmentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_progress_records_lessons_LessonId",
                table: "progress_records",
                column: "LessonId",
                principalTable: "lessons",
                principalColumn: "LessonId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_progress_records_simulation_tasks_SimulationTaskId",
                table: "progress_records",
                column: "SimulationTaskId",
                principalTable: "simulation_tasks",
                principalColumn: "TaskId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_saved_courses_courses_CourseId",
                table: "saved_courses",
                column: "CourseId",
                principalTable: "courses",
                principalColumn: "CourseId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_saved_courses_users_UserId",
                table: "saved_courses",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_simulation_tasks_courses_CourseId",
                table: "simulation_tasks",
                column: "CourseId",
                principalTable: "courses",
                principalColumn: "CourseId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_students_users_UserId",
                table: "students",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_support_accesses_users_UserId",
                table: "support_accesses",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_support_messages_support_tickets_TicketId",
                table: "support_messages",
                column: "TicketId",
                principalTable: "support_tickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_support_tickets_users_UserId",
                table: "support_tickets",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_user_claims_users_UserId",
                table: "user_claims",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_logins_users_UserId",
                table: "user_logins",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_notifications_users_UserId",
                table: "user_notifications",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_tokens_users_UserId",
                table: "user_tokens",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_account_sessions_users_UserId",
                table: "account_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_admin_activities_users_ActorUserId",
                table: "admin_activities");

            migrationBuilder.DropForeignKey(
                name: "FK_admins_users_UserId",
                table: "admins");

            migrationBuilder.DropForeignKey(
                name: "FK_banner_rewards_users_UserId",
                table: "banner_rewards");

            migrationBuilder.DropForeignKey(
                name: "FK_challenges_courses_CourseId",
                table: "challenges");

            migrationBuilder.DropForeignKey(
                name: "FK_courses_instructors_InstructorId",
                table: "courses");

            migrationBuilder.DropForeignKey(
                name: "FK_enrollments_lessons_LastLessonId",
                table: "enrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_instructor_applications_users_UserId",
                table: "instructor_applications");

            migrationBuilder.DropForeignKey(
                name: "FK_instructor_reviews_instructor_applications_ApplicationId",
                table: "instructor_reviews");

            migrationBuilder.DropForeignKey(
                name: "FK_instructors_users_UserId",
                table: "instructors");

            migrationBuilder.DropForeignKey(
                name: "FK_lessons_courses_CourseId",
                table: "lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_login_records_users_UserId",
                table: "login_records");

            migrationBuilder.DropForeignKey(
                name: "FK_progress_records_challenges_ChallengeId",
                table: "progress_records");

            migrationBuilder.DropForeignKey(
                name: "FK_progress_records_enrollments_EnrollmentId",
                table: "progress_records");

            migrationBuilder.DropForeignKey(
                name: "FK_progress_records_lessons_LessonId",
                table: "progress_records");

            migrationBuilder.DropForeignKey(
                name: "FK_progress_records_simulation_tasks_SimulationTaskId",
                table: "progress_records");

            migrationBuilder.DropForeignKey(
                name: "FK_saved_courses_courses_CourseId",
                table: "saved_courses");

            migrationBuilder.DropForeignKey(
                name: "FK_saved_courses_users_UserId",
                table: "saved_courses");

            migrationBuilder.DropForeignKey(
                name: "FK_simulation_tasks_courses_CourseId",
                table: "simulation_tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_students_users_UserId",
                table: "students");

            migrationBuilder.DropForeignKey(
                name: "FK_support_accesses_users_UserId",
                table: "support_accesses");

            migrationBuilder.DropForeignKey(
                name: "FK_support_messages_support_tickets_TicketId",
                table: "support_messages");

            migrationBuilder.DropForeignKey(
                name: "FK_support_tickets_users_UserId",
                table: "support_tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_user_claims_users_UserId",
                table: "user_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_user_logins_users_UserId",
                table: "user_logins");

            migrationBuilder.DropForeignKey(
                name: "FK_user_notifications_users_UserId",
                table: "user_notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_user_tokens_users_UserId",
                table: "user_tokens");

            migrationBuilder.RenameTable(name: "students", newName: "__rename_students");
            migrationBuilder.RenameTable(name: "__rename_students", newName: "Students");

            migrationBuilder.RenameTable(name: "lessons", newName: "__rename_lessons");
            migrationBuilder.RenameTable(name: "__rename_lessons", newName: "Lessons");

            migrationBuilder.RenameTable(name: "instructors", newName: "__rename_instructors");
            migrationBuilder.RenameTable(name: "__rename_instructors", newName: "Instructors");

            migrationBuilder.RenameTable(name: "enrollments", newName: "__rename_enrollments");
            migrationBuilder.RenameTable(name: "__rename_enrollments", newName: "Enrollments");

            migrationBuilder.RenameTable(name: "courses", newName: "__rename_courses");
            migrationBuilder.RenameTable(name: "__rename_courses", newName: "Courses");

            migrationBuilder.RenameTable(name: "challenges", newName: "__rename_challenges");
            migrationBuilder.RenameTable(name: "__rename_challenges", newName: "Challenges");

            migrationBuilder.RenameTable(name: "admins", newName: "__rename_admins");
            migrationBuilder.RenameTable(name: "__rename_admins", newName: "Admins");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "AspNetUsers");

            migrationBuilder.RenameTable(
                name: "user_tokens",
                newName: "AspNetUserTokens");

            migrationBuilder.RenameTable(
                name: "user_notifications",
                newName: "UserNotifications");

            migrationBuilder.RenameTable(
                name: "user_logins",
                newName: "AspNetUserLogins");

            migrationBuilder.RenameTable(
                name: "user_claims",
                newName: "AspNetUserClaims");

            migrationBuilder.RenameTable(
                name: "support_tickets",
                newName: "SupportTickets");

            migrationBuilder.RenameTable(
                name: "support_messages",
                newName: "SupportMessages");

            migrationBuilder.RenameTable(
                name: "support_accesses",
                newName: "SupportAccesses");

            migrationBuilder.RenameTable(
                name: "simulation_tasks",
                newName: "SimulationTasks");

            migrationBuilder.RenameTable(
                name: "security_events",
                newName: "SecurityEvents");

            migrationBuilder.RenameTable(
                name: "security_alerts",
                newName: "SecurityAlerts");

            migrationBuilder.RenameTable(
                name: "saved_courses",
                newName: "SavedCourses");

            migrationBuilder.RenameTable(
                name: "progress_records",
                newName: "ProgressRecords");

            migrationBuilder.RenameTable(
                name: "login_records",
                newName: "LoginRecords");

            migrationBuilder.RenameTable(
                name: "instructor_reviews",
                newName: "InstructorReviews");

            migrationBuilder.RenameTable(
                name: "instructor_applications",
                newName: "InstructorApplications");

            migrationBuilder.RenameTable(
                name: "challenge_submissions",
                newName: "ChallengeSubmissions");

            migrationBuilder.RenameTable(
                name: "banner_rewards",
                newName: "BannerRewards");

            migrationBuilder.RenameTable(
                name: "admin_activities",
                newName: "AdminActivities");

            migrationBuilder.RenameTable(
                name: "account_sessions",
                newName: "AccountSessions");

            migrationBuilder.RenameIndex(name: "IX_lessons_CourseId", table: "Lessons", newName: "IX_rename_882de67976cc");
            migrationBuilder.RenameIndex(name: "IX_rename_882de67976cc", table: "Lessons", newName: "IX_Lessons_CourseId");

            migrationBuilder.RenameIndex(name: "IX_enrollments_UserId_CourseId", table: "Enrollments", newName: "IX_rename_24709d5e8930");
            migrationBuilder.RenameIndex(name: "IX_rename_24709d5e8930", table: "Enrollments", newName: "IX_Enrollments_UserId_CourseId");

            migrationBuilder.RenameIndex(name: "IX_enrollments_LastLessonId", table: "Enrollments", newName: "IX_rename_998e011cb76f");
            migrationBuilder.RenameIndex(name: "IX_rename_998e011cb76f", table: "Enrollments", newName: "IX_Enrollments_LastLessonId");

            migrationBuilder.RenameIndex(name: "IX_courses_InstructorId", table: "Courses", newName: "IX_rename_7cd05e6234b1");
            migrationBuilder.RenameIndex(name: "IX_rename_7cd05e6234b1", table: "Courses", newName: "IX_Courses_InstructorId");

            migrationBuilder.RenameIndex(name: "IX_challenges_CourseId", table: "Challenges", newName: "IX_rename_036b88ce6fbd");
            migrationBuilder.RenameIndex(name: "IX_rename_036b88ce6fbd", table: "Challenges", newName: "IX_Challenges_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_users_PhoneNumber",
                table: "AspNetUsers",
                newName: "IX_AspNetUsers_PhoneNumber");

            migrationBuilder.RenameIndex(
                name: "IX_user_notifications_UserId_ReadUtc_CreatedUtc",
                table: "UserNotifications",
                newName: "IX_UserNotifications_UserId_ReadUtc_CreatedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_user_notifications_UserId_EventKey",
                table: "UserNotifications",
                newName: "IX_UserNotifications_UserId_EventKey");

            migrationBuilder.RenameIndex(
                name: "IX_user_logins_UserId",
                table: "AspNetUserLogins",
                newName: "IX_AspNetUserLogins_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_user_claims_UserId",
                table: "AspNetUserClaims",
                newName: "IX_AspNetUserClaims_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_support_tickets_UserId_UpdatedUtc",
                table: "SupportTickets",
                newName: "IX_SupportTickets_UserId_UpdatedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_support_messages_TicketId",
                table: "SupportMessages",
                newName: "IX_SupportMessages_TicketId");

            migrationBuilder.RenameIndex(
                name: "IX_support_accesses_UserId_CreatedUtc",
                table: "SupportAccesses",
                newName: "IX_SupportAccesses_UserId_CreatedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_support_accesses_TokenHash",
                table: "SupportAccesses",
                newName: "IX_SupportAccesses_TokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_simulation_tasks_CourseId",
                table: "SimulationTasks",
                newName: "IX_SimulationTasks_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_security_events_SubjectHash_OccurredUtc",
                table: "SecurityEvents",
                newName: "IX_SecurityEvents_SubjectHash_OccurredUtc");

            migrationBuilder.RenameIndex(
                name: "IX_security_events_IpAddress_OccurredUtc",
                table: "SecurityEvents",
                newName: "IX_SecurityEvents_IpAddress_OccurredUtc");

            migrationBuilder.RenameIndex(
                name: "IX_security_alerts_Status_CreatedUtc",
                table: "SecurityAlerts",
                newName: "IX_SecurityAlerts_Status_CreatedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_security_alerts_EventKey",
                table: "SecurityAlerts",
                newName: "IX_SecurityAlerts_EventKey");

            migrationBuilder.RenameIndex(
                name: "IX_saved_courses_UserId_SavedUtc",
                table: "SavedCourses",
                newName: "IX_SavedCourses_UserId_SavedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_saved_courses_UserId_CourseId",
                table: "SavedCourses",
                newName: "IX_SavedCourses_UserId_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_saved_courses_CourseId",
                table: "SavedCourses",
                newName: "IX_SavedCourses_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_progress_records_SimulationTaskId",
                table: "ProgressRecords",
                newName: "IX_ProgressRecords_SimulationTaskId");

            migrationBuilder.RenameIndex(
                name: "IX_progress_records_LessonId",
                table: "ProgressRecords",
                newName: "IX_ProgressRecords_LessonId");

            migrationBuilder.RenameIndex(
                name: "IX_progress_records_EnrollmentId_SimulationTaskId",
                table: "ProgressRecords",
                newName: "IX_ProgressRecords_EnrollmentId_SimulationTaskId");

            migrationBuilder.RenameIndex(
                name: "IX_progress_records_EnrollmentId_LessonId",
                table: "ProgressRecords",
                newName: "IX_ProgressRecords_EnrollmentId_LessonId");

            migrationBuilder.RenameIndex(
                name: "IX_progress_records_EnrollmentId_ChallengeId",
                table: "ProgressRecords",
                newName: "IX_ProgressRecords_EnrollmentId_ChallengeId");

            migrationBuilder.RenameIndex(
                name: "IX_progress_records_ChallengeId",
                table: "ProgressRecords",
                newName: "IX_ProgressRecords_ChallengeId");

            migrationBuilder.RenameIndex(
                name: "IX_login_records_UserId_OccurredUtc",
                table: "LoginRecords",
                newName: "IX_LoginRecords_UserId_OccurredUtc");

            migrationBuilder.RenameIndex(
                name: "IX_instructor_reviews_ApplicationId",
                table: "InstructorReviews",
                newName: "IX_InstructorReviews_ApplicationId");

            migrationBuilder.RenameIndex(
                name: "IX_instructor_applications_UserId_Revision",
                table: "InstructorApplications",
                newName: "IX_InstructorApplications_UserId_Revision");

            migrationBuilder.RenameIndex(
                name: "IX_instructor_applications_Status_SubmittedUtc",
                table: "InstructorApplications",
                newName: "IX_InstructorApplications_Status_SubmittedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_challenge_submissions_UserId_ChallengeId",
                table: "ChallengeSubmissions",
                newName: "IX_ChallengeSubmissions_UserId_ChallengeId");

            migrationBuilder.RenameIndex(
                name: "IX_banner_rewards_UserId_CourseId",
                table: "BannerRewards",
                newName: "IX_BannerRewards_UserId_CourseId");

            migrationBuilder.RenameIndex(
                name: "IX_admin_activities_ActorUserId_OccurredUtc",
                table: "AdminActivities",
                newName: "IX_AdminActivities_ActorUserId_OccurredUtc");

            migrationBuilder.RenameIndex(
                name: "IX_account_sessions_UserId_ExpiresUtc",
                table: "AccountSessions",
                newName: "IX_AccountSessions_UserId_ExpiresUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountSessions_AspNetUsers_UserId",
                table: "AccountSessions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AdminActivities_AspNetUsers_ActorUserId",
                table: "AdminActivities",
                column: "ActorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Admins_AspNetUsers_UserId",
                table: "Admins",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                table: "AspNetUserClaims",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                table: "AspNetUserLogins",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                table: "AspNetUserTokens",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BannerRewards_AspNetUsers_UserId",
                table: "BannerRewards",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Challenges_Courses_CourseId",
                table: "Challenges",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "CourseId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_Instructors_InstructorId",
                table: "Courses",
                column: "InstructorId",
                principalTable: "Instructors",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Enrollments_Lessons_LastLessonId",
                table: "Enrollments",
                column: "LastLessonId",
                principalTable: "Lessons",
                principalColumn: "LessonId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_InstructorApplications_AspNetUsers_UserId",
                table: "InstructorApplications",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InstructorReviews_InstructorApplications_ApplicationId",
                table: "InstructorReviews",
                column: "ApplicationId",
                principalTable: "InstructorApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Instructors_AspNetUsers_UserId",
                table: "Instructors",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Courses_CourseId",
                table: "Lessons",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "CourseId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LoginRecords_AspNetUsers_UserId",
                table: "LoginRecords",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProgressRecords_Challenges_ChallengeId",
                table: "ProgressRecords",
                column: "ChallengeId",
                principalTable: "Challenges",
                principalColumn: "ChallengeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProgressRecords_Enrollments_EnrollmentId",
                table: "ProgressRecords",
                column: "EnrollmentId",
                principalTable: "Enrollments",
                principalColumn: "EnrollmentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProgressRecords_Lessons_LessonId",
                table: "ProgressRecords",
                column: "LessonId",
                principalTable: "Lessons",
                principalColumn: "LessonId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProgressRecords_SimulationTasks_SimulationTaskId",
                table: "ProgressRecords",
                column: "SimulationTaskId",
                principalTable: "SimulationTasks",
                principalColumn: "TaskId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SavedCourses_AspNetUsers_UserId",
                table: "SavedCourses",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SavedCourses_Courses_CourseId",
                table: "SavedCourses",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "CourseId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SimulationTasks_Courses_CourseId",
                table: "SimulationTasks",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "CourseId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Students_AspNetUsers_UserId",
                table: "Students",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAccesses_AspNetUsers_UserId",
                table: "SupportAccesses",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportMessages_SupportTickets_TicketId",
                table: "SupportMessages",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_AspNetUsers_UserId",
                table: "SupportTickets",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserNotifications_AspNetUsers_UserId",
                table: "UserNotifications",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
