using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OnlineContract.Models
{
    public class NoteDto
    {
        public int Id { get; set; }
        public string? Comment { get; set; }
        public bool IsActive { get; set; }
        public string? Text { get => Comment; set => Comment = value; }
        public bool IsMain { get; set; }
        public bool IsDeleted { get; set; }
        public string InputDt { get; set; } = "";
        public int? InputUserId { get; set; }
        public string InputUserCode { get; set; } = "";
        public int? LastModifiedById { get; set; }
        public string LastModifiedByCode { get; set; } = "";
        public string LastUpdatedDt { get; set; } = "";
        public int Stamp { get; set; }
    }

    public class NoteCreateDto
    {
        [Required]
        public string? Comment { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Text { get => Comment; set => Comment = value; }
        public bool IsDeleted { get; set; } = false;
    }

    public class NoteUpdateDto
    {
        [Required]
        public int Id { get; set; }
        public string? Comment { get; set; }
        public bool? IsActive { get; set; }
        public string? Text { get => Comment; set => Comment = value; }
        public bool? IsDeleted { get; set; }
        public int? Stamp { get; set; }
    }

    public class NoteDeleteDto
    {
        [Required]
        public int Id { get; set; }
        public int? Stamp { get; set; }
    }

    public class NotesBulkSaveDto
    {
        public List<NoteCreateDto> Add { get; set; } = new();
        public List<NoteUpdateDto> Update { get; set; } = new();
        public List<NoteDeleteDto> Delete { get; set; } = new();
        public int? SetMainId { get; set; }
        public int? SetMainStamp { get; set; }
    }

    public class NoteCreateSimpleDto
    {
        public string? Subject { get; set; }
        public string? Comment { get; set; }
        public int? ContractId { get; set; }
        public int? ProductId { get; set; }
        public bool? IsActive { get; set; }
    }
}
