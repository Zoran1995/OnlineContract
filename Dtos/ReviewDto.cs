namespace OnlineContract.Dtos
{
    public class ReviewDto
    {
        public int Id { get; set; }
        public DateTime InputDt { get; set; }
        public int InputUserId { get; set; }
        public string UserCode { get; set; } = "Anonymous";
        public string? Comment { get; set; }
        public int Mark { get; set; }
        public int Stamp { get; set; }
    }

    public class ReviewCreateDto
    {
        public string? Comment { get; set; }
        public int Mark { get; set; }
    }

    public class ReviewListResponse
    {
        public List<ReviewDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public decimal AverageRating { get; set; }
        public int TotalReviews { get; set; }
    }
}
