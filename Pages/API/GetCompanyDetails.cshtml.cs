using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DemoMVC.Data;

namespace DemoWebApp.API
{
    public class GetCompanyDetailsModel : PageModel
    {
        public IActionResult OnGet(string companyName)
        {
            var companyIdentifier = string.Empty;

            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "Header2.json");

            if (System.IO.File.Exists(jsonFilePath))
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(jsonData);

                var matchingResponse = parsedData?.GDSSDKResponse
                    .FirstOrDefault(r => string.Equals(r.Identifier, companyName, StringComparison.OrdinalIgnoreCase));

                if (matchingResponse != null && matchingResponse.Rows != null && matchingResponse.Rows.Count > 0)
                {
                    companyIdentifier = matchingResponse.Rows[0].Row[0];

                    //return new JsonResult(new { companyIdentifier });
                }
            }

            jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "Detail1.json");

            if (System.IO.File.Exists(jsonFilePath))
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(jsonData);

                // Find all matching responses based on Identifier
                var matchingResponses = parsedData?.GDSSDKResponse
                    .Where(r => string.Equals(r.Identifier, companyIdentifier, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingResponses != null && matchingResponses.Any())
                {
                    // Transform the matching data into a dictionary for easier frontend consumption
                    var result = matchingResponses.Select(response => new
                    {
                        Header = response.Headers?.ToList(),
                        Value = response.Rows.ToList()
                    });

                    return new JsonResult(result);
                }
            }

            return new JsonResult(new { message = "No data found for the given identifier." });
        }
    }
}
