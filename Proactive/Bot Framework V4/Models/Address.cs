namespace ProactiveBot.Models
{
    public class Address
    {
        public string channelId { get; set; }
        public User user { get; set; }
        public Conversation conversation { get; set; }
        public Bot bot { get; set; }
        public string serviceUrl { get; set; }
    }
}
