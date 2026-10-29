using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HackAcad.Web.Models.Entities;

// ADMIN - The Admin role marker also stores read-only auditor access; credentials remain in the users table.

public class Admin
{
    [Key, ForeignKey(nameof(User))]
    public string UserId { get; set; } = string.Empty;

    // ADMIN - Auditors retain inspection access but cannot perform management writes.
    public bool IsReadOnly { get; set; }

    public ApplicationUser? User { get; set; }
}
