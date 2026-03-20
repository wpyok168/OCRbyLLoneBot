
public class RecMsgMode
{
    public long GroupID { get; set; }
    public long UserID { get; set; }
    public bool IsFriend { get; set; }
    public bool IsGroupPrivate { get; set; }
    public required string RecMsgContent { get; set; }
    public string? ImageFile { get; set; }
    public string ImageUrl { get; set; } = "";
    public long Self_ID { get; set; }
    public string Echo { get; set; } = "";
    public long Time { get; set; }
    public string Message_Type { get; set; } = "";
}
