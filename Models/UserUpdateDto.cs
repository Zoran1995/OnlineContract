namespace OnlineContract.Models
{
    public class UserUpdateDto
    {
        public string? Code { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public int? RoleId { get; set; }
        public bool? IsActive { get; set; }
        public int? OwnerId { get; set; }
        public string? City { get; set; }
        public string? StreetAddress { get; set; }
        public string? PostalCode { get; set; }
        public string? Password { get; set; }
        public int? Stamp { get; set; }
    }
}
