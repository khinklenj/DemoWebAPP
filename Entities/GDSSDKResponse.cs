using Newtonsoft.Json;
using System.Collections.Generic;

namespace DemoMVC.Data
{
    public class GDSSDKResponseRoot
    {
        [JsonProperty("GDSSDKResponse")]
        public List<GDSSDKResponse> GDSSDKResponse { get; set; }
    }

    public class GDSSDKResponse
    {
        public string ErrMsg { get; set; }
        public List<string> Headers { get; set; }
        public int NumRows { get; set; }
        public string Seniority { get; set; }
        public Properties Properties { get; set; }
        public string EndDate { get; set; }
        public string CacheExpiryTime { get; set; }
        public string StartDate { get; set; }
        public string Function { get; set; }
        public string Identifier { get; set; }
        public int NumCols { get; set; }
        public string Mnemonic { get; set; }
        public string Frequency { get; set; }
        public string Limit { get; set; }
        public List<RowWrapper> Rows { get; set; }
    }

    public class Properties
    {
        public string endrank { get; set; }
        public string startrank { get; set; }
    }

    public class RowWrapper
    {
        [JsonProperty("Row")]
        public List<string> Row { get; set; }
    }
}
