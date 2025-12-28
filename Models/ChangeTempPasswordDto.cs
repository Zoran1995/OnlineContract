namespace OnlineContract.Models
{
    public class ChangeTempPasswordDto
    {
        public string Code { get; set; } = "";
        public string NewPassword { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
        public int? Stamp { get; set; }
    }
}