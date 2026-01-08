using System.ComponentModel.DataAnnotations;

namespace OnlineContract.Dtos
{
    public class ProfileDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string StreetAddress { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public int Stamp { get; set; }
    }

    public class ProfileUpdateDto
    {
        [Required] public string Code { get; set; } = string.Empty;
        [Required] public string FirstName { get; set; } = string.Empty;
        [Required] public string LastName { get; set; } = string.Empty;
        [Required] public string Email { get; set; } = string.Empty;
        [Required] public string PhoneNumber { get; set; } = string.Empty;
        [Required] public string City { get; set; } = string.Empty;
        [Required] public string StreetAddress { get; set; } = string.Empty;
        [Required] public string PostalCode { get; set; } = string.Empty;
        [Required] public int Stamp { get; set; }
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
        public string? ConfirmNewPassword { get; set; }
    }
}
