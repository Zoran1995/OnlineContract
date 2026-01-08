namespace OnlineContract.Dtos
{
    public class ClientErrorDto
    {
        public string Description { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public int UserId { get; set; }
        public string? EventTypeOverride { get; set; }
    }
}
