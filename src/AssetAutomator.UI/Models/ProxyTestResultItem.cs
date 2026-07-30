namespace AssetAutomator.UI.Models
{
    public class ProxyTestResultItem
    {
        public string Proxy { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "ONLINE" or "OFFLINE"
        public string Response { get; set; } = string.Empty;
        public bool IsOnline => Status == "ONLINE";
    }
}
