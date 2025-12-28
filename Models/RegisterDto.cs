public class RegisterDto
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    // Optional address fields
    public string? City { get; set; }
    public string? StreetAddress { get; set; }
    public string? PostalCode { get; set; }
    // Optional desired role; default is Customer when null
    public int? RoleId { get; set; }
    public bool IsTempPassword { get; set; }
}