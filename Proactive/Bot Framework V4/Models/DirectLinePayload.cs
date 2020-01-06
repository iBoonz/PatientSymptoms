namespace ProactiveBot.Models
{
    public class DirectLinePayload
    {
        public string conversationId { get; set; }
        public string token { get; set; }
        public int expires_in { get; set; }
    }
}
