using DemoMVC.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DemoWebApp.Pages
{
    public class IndexModel : PageModel
    {
        public string SearchTerm { get; set; } = string.Empty;
        public List<ResultItem> Results { get; set; } = new List<ResultItem>();
        public string CompanyIdentifier { get; set; } = string.Empty;

        public void OnGet(string searchTerm)
        {
            SearchTerm = searchTerm;

            // Load data from JSON
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "Header1.json");
            if (System.IO.File.Exists(jsonFilePath))
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(jsonData);

                // Find the matching Identifier and process Rows
                var matchingResponse = parsedData?.GDSSDKResponse
                    .FirstOrDefault(r => string.Equals(r.Identifier, SearchTerm, StringComparison.OrdinalIgnoreCase));

                if (matchingResponse != null && matchingResponse.Rows != null)
                {
                    Results = matchingResponse.Rows
                        .Select((row, index) => new ResultItem
                        {
                            Id = index + 1,
                            CompanyName = row.Row[0],  // First element as "CompanyName"
                            AsOfDate = row.Row.Count > 1 ? row.Row[1] : ""  // Second element as "AsOfDate" if exists
                        }).ToList();
                }
            }
        }

        public void OnPostViewDetails(string companyName)
        {
            // Load data from Header2.json
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "Header2.json");
            if (System.IO.File.Exists(jsonFilePath))
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(jsonData);

                // Find the matching company name in Identifier
                var matchingResponse = parsedData?.GDSSDKResponse
                    .FirstOrDefault(r => string.Equals(r.Identifier, companyName, StringComparison.OrdinalIgnoreCase));

                if (matchingResponse != null && matchingResponse.Rows != null && matchingResponse.Rows.Count > 0)
                {
                    // Extract the company identifier from Rows[0].Row[0]
                    CompanyIdentifier = matchingResponse.Rows[0].Row[0];
                }
            }
        }

        // Classes to hold filtered results
        public class ResultItem
        {
            public int Id { get; set; }
            public string CompanyName { get; set; }
            public string AsOfDate { get; set; }
        }
    }
}
