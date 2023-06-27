namespace DataAcquisitionServerAppWithWebPage.Data
{
    public class User
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public static class SystemState
    {
        public static bool canConnectSQL { get; set; }
    }
}
