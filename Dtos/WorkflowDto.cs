namespace OnlineContract.Dtos
{
    public sealed class ChangeStateModalDto
    {
        public int CurrentStateId { get; set; }
        public string CurrentStateName { get; set; } = "";
        public bool IsEndState { get; set; }
        public List<NextStateDto> NextStates { get; set; } = new();
    }

    public sealed class NextStateDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class SetStateDto
    {
        public int NextStateId { get; set; }
    }
}
