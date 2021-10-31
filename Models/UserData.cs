namespace TSApi.Models
{
    public class UserData
    {
        public string login { get; set; }

        public string passwd { get; set; }

        public string torPath { get; set; }

        public bool allowedToChangeSettings { get; set; }
    }
}
