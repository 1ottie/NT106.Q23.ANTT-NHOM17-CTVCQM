namespace DrawClient.Models
{
    public class LoginResponse
    {
        public string token { get; set; }

        public User user { get; set; }
    }
}