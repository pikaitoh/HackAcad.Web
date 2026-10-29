namespace HackAcad.Web.Models.Entities;

// ADMIN - Course moderation states are independent of the instructor's publication choice.

public static class CourseReviewStatus
{
    public const string Draft = "Draft";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}
