using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using DemoMVC.Data;
using Microsoft.Data.SqlClient;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static DemoWebApp.Pages.IndexModel;
using System.Reflection.Metadata;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;

namespace DemoWebApp.API
{
    [ApiController]
    [Route("api/[controller]")]
    public class CompanyDetailsController : ControllerBase
    {
        [HttpGet("GetCompanyDetails")]
        public IActionResult GetCompanyDetails(string companyName)
        {
            if (string.IsNullOrWhiteSpace(companyName))
            {
                return BadRequest("Company name is required.");
            }

            return GetCompanyData(companyName);
        }

        [HttpPost("StoreCompanyDetails")]
        public async Task<IActionResult> StoreCompanyDetailsAsync([FromBody] StoreCompanyDetailsRequest request)
        {
            // Validate the request
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest("Title is required.");
            }

            List<SelectCompanyDetails> selectCompanyDetails = JsonConvert.DeserializeObject<List<SelectCompanyDetails>>(request.Details);

            // Database connection
            string connectionString = "Server=KEITHLAPTOP\\SQLEXPRESS;Database=CompanyData;Integrated Security=True;TrustServerCertificate=True;";

            try
            {

                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    foreach (SelectCompanyDetails companyDetails in selectCompanyDetails)
                    {
                        // Call GetCompanyData to fetch company details
                        var companyDetailsResult = GetCompanyData(companyDetails.CompanyName.Replace("\\'", "'"));
                        // Extract the details from the JsonResult
                        var details = companyDetailsResult.Value;

                        // Cast 'details' to a strongly typed object
                        var detailsJson = JsonConvert.SerializeObject(details);
                        var detailsData = JsonConvert.DeserializeObject<CompanyDetailsResponse>(detailsJson);

                        var detail1Data = detailsData.Detail1Data ?? new List<DetailItem>();
                        var transactionData = detailsData.TransactionData ?? new List<TransactionItem>();


                        // Step 1: Insert into Request table
                        var requestId = 0;
                        var insertRequestQuery = @"
                    INSERT INTO [dbo].[Request] (RequestName, RequestType, CreateUserIdentity, CreateDate)
                    OUTPUT INSERTED.RequestId
                    VALUES (@RequestName, @RequestType, @CreateUserIdentity, GETDATE())";

                        using (var command = new SqlCommand(insertRequestQuery, connection))
                        {
                            command.Parameters.AddWithValue("@RequestName", request.Title);
                            command.Parameters.AddWithValue("@RequestType", companyDetails.CompanyName);
                            command.Parameters.AddWithValue("@CreateUserIdentity", "SystemUser"); // Replace with actual user identity

                            requestId = (int)await command.ExecuteScalarAsync();
                        }

                        if (companyDetails.CompanyDetails)
                        {
                            // Initialize consolidated data for CoreData
                            string entityName = null;
                            string entityAddress = null;
                            string entityPhone = null;
                            string entityRevenue = null;
                            string entityEmployees = null;
                            string entityYearFounded = null;
                            string entityWebUrl = null;
                            string entityDescription = null;

                            // Consolidate Detail1Data into a single row
                            foreach (var item in detail1Data)
                            {
                                var headers = item.Headers as List<string>;
                                var values = item.Values as List<List<string>>;

                                if (headers != null && values != null)
                                {
                                    for (int i = 0; i < headers.Count; i++)
                                    {
                                        var header = headers[i];
                                        var value = values.FirstOrDefault()?.ElementAtOrDefault(0); // Get the first value

                                        switch (header)
                                        {
                                            case "IQ_COMPANY_NAME":
                                                entityName = value;
                                                break;
                                            case "IQ_COMPANY_ADDRESS":
                                                entityAddress = value;
                                                break;
                                            case "IQ_COMPANY_PHONE":
                                                entityPhone = value;
                                                break;
                                            case "IQ_TOTAL_REV":
                                                entityRevenue = value;
                                                break;
                                            case "IQ_EMPLOYEES":
                                                entityEmployees = value;
                                                break;
                                            case "IQ_YEAR_FOUNDED":
                                                entityYearFounded = value;
                                                break;
                                            case "IQ_COMPANY_WEBSITE":
                                                entityWebUrl = value;
                                                break;
                                            case "IQ_BUSINESS_DESCRIPTION":
                                                entityDescription = value;
                                                break;
                                        }
                                    }
                                }
                            }

                            using (var command = new SqlCommand("INSERT INTO CoreData (fkRequestID, EntityName, EntityAddress, EntityPhone, EntityRevenue, EntityEmployeeCount, EntityYearFounded, EntityWebUrl, EntityOverviewer, CreateDate, CreateUserIdentity) VALUES (@fkRequestID, @EntityName, @EntityAddress, @EntityPhone, @EntityRevenue, @EntityEmployeeCount, @EntityYearFounded, @EntityWebUrl, @EntityOverviewer, @CreateDate, @CreateUserIdentity)", connection))
                            {
                                command.Parameters.AddWithValue("@fkRequestID", requestId);
                                command.Parameters.AddWithValue("@EntityName", entityName ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@EntityAddress", entityAddress ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@EntityPhone", entityPhone ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@EntityRevenue", entityRevenue ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@EntityEmployeeCount", entityEmployees ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@EntityYearFounded", entityYearFounded ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@EntityWebUrl", entityWebUrl ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@EntityOverviewer", entityDescription ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                command.Parameters.AddWithValue("@CreateUserIdentity", "SystemUser");

                                command.ExecuteNonQuery();
                            }
                        }

                        if (companyDetails.Professionals)
                        {

                            // Insert into KeyExecutives
                            var professionalData = detail1Data
                            .Where(item => item.Headers.Contains("IQ_PROFESSIONAL"))
                            .SelectMany(item => item.Values)
                            .ToList();

                            var professionalTitles = detail1Data
                                .Where(item => item.Headers.Contains("IQ_PROFESSIONAL_TITLE"))
                                .SelectMany(item => item.Values)
                                .ToList();

                            for (int i = 0; i < professionalData.Count; i++)
                            {
                                var title = professionalTitles.ElementAtOrDefault(i)?.FirstOrDefault() ?? "Unknown Title";
                                var name = professionalData.ElementAtOrDefault(i)?.FirstOrDefault() ?? "Unknown Name";

                                var insertKeyExecutivesQuery = @"
                        INSERT INTO [dbo].[KeyExecutives] 
                        (fkRequestID, Title, Name, CreateDate, CreateUserIdentity)
                        VALUES 
                        (@RequestId, @Title, @Name, GETDATE(), @CreateUserIdentity)";

                                using (var command = new SqlCommand(insertKeyExecutivesQuery, connection))
                                {
                                    command.Parameters.AddWithValue("@RequestId", requestId);
                                    command.Parameters.AddWithValue("@Title", title);
                                    command.Parameters.AddWithValue("@Name", name);
                                    command.Parameters.AddWithValue("@CreateUserIdentity", "SystemUser");

                                    await command.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        if (companyDetails.Transactions)
                        {
                            // Insert into TransactionSummary
                            foreach (var transaction in transactionData)
                            {
                                var headers = transaction.Headers ?? new List<string>();
                                var values = transaction.Values ?? new List<List<string>>();

                                for (int i = 0; i < headers.Count; i++)
                                {
                                    var transactionDescription = headers[i]; // Use header as the description
                                    var transactionValue = values.ElementAtOrDefault(i)?.FirstOrDefault(); // Get the corresponding value
                                    var transactionDate = DateTime.Now; // Current date
                                    var transactionCounsel = DBNull.Value; // Set as NULL

                                    var insertTransactionSummaryQuery = @"
                                INSERT INTO [dbo].[TransactionSummary] 
                                (fkRequestID, TransactionDate, TransactionDescription, TransactionValue, TransactionCounsel, CreateDate, CreateUserIdentity)
                                VALUES 
                                (@RequestId, @TransactionDate, @TransactionDescription, @TransactionValue, @TransactionCounsel, GETDATE(), @CreateUserIdentity)";

                                    using (var command = new SqlCommand(insertTransactionSummaryQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@RequestId", requestId);
                                        command.Parameters.AddWithValue("@TransactionDate", transactionDate);
                                        command.Parameters.AddWithValue("@TransactionDescription", transactionDescription ?? (object)DBNull.Value);
                                        command.Parameters.AddWithValue("@TransactionValue", transactionValue ?? (object)DBNull.Value);
                                        command.Parameters.AddWithValue("@TransactionCounsel", transactionCounsel);
                                        command.Parameters.AddWithValue("@CreateUserIdentity", "SystemUser");

                                        await command.ExecuteNonQueryAsync();
                                    }
                                }
                            }
                        }
                    }
                }

                return Ok(new { message = "Data stored successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error storing data: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred while storing the data.", error = ex.Message });
            }
        }

        [HttpGet("GetDataSourceSearch1")]
        public IActionResult GetDataSourceSearch1(string searchTerm)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return BadRequest("Search term cannot be empty.");
            }

            // Load data from JSON file
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "Header1.json");
            if (!System.IO.File.Exists(jsonFilePath))
            {
                return NotFound("Data file not found.");
            }

            try
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(jsonData);

                if (parsedData?.GDSSDKResponse == null)
                {
                    return NotFound("No data available in the file.");
                }

                // Find the matching Identifier and process Rows
                var matchingResponse = parsedData.GDSSDKResponse
                    .FirstOrDefault(r => string.Equals(r.Identifier, searchTerm, StringComparison.OrdinalIgnoreCase));

                if (matchingResponse != null && matchingResponse.Rows != null)
                {
                    var result = matchingResponse.Rows
                        .Select((row, index) => new ResultItem
                        {
                            Id = index + 1,
                            CompanyName = row.Row.FirstOrDefault() ?? "",  // First element as "CompanyName"
                            AsOfDate = row.Row.ElementAtOrDefault(1) ?? ""  // Second element as "AsOfDate" if exists
                        })
                        .ToList();

                    return Ok(result);
                }

                return NotFound("No matching data found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while processing the request: {ex.Message}");
            }
        }

        [HttpGet("GetDataSourceSearch2")]
        public IActionResult GetDataSourceSearch2(string searchTerm)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return BadRequest("Search term cannot be empty.");
            }

            // Load data from JSON file
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "Header1.json");
            if (!System.IO.File.Exists(jsonFilePath))
            {
                return NotFound("Data file not found.");
            }

            try
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(jsonData);

                if (parsedData?.GDSSDKResponse == null)
                {
                    return NotFound("No data available in the file.");
                }

                // Find the matching Identifier and process Rows
                var matchingResponse = parsedData.GDSSDKResponse
                    .FirstOrDefault(r => string.Equals(r.Identifier, searchTerm, StringComparison.OrdinalIgnoreCase));

                if (matchingResponse != null && matchingResponse.Rows != null)
                {
                    var result = matchingResponse.Rows
                        .Select((row, index) => new ResultItem
                        {
                            Id = index + 1,
                            CompanyName = row.Row.FirstOrDefault() ?? "",  // First element as "CompanyName"
                            AsOfDate = row.Row.ElementAtOrDefault(1) ?? ""  // Second element as "AsOfDate" if exists
                        })
                        .ToList();

                    return Ok(result);
                }

                return NotFound("No matching data found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while processing the request: {ex.Message}");
            }
        }

        [HttpGet("GetDataSourceSearch3")]
        public IActionResult GetDataSourceSearch3(string searchTerm)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return BadRequest("Search term cannot be empty.");
            }

            // Load data from JSON file
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "Header1.json");
            if (!System.IO.File.Exists(jsonFilePath))
            {
                return NotFound("Data file not found.");
            }

            try
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(jsonData);

                if (parsedData?.GDSSDKResponse == null)
                {
                    return NotFound("No data available in the file.");
                }

                // Find the matching Identifier and process Rows
                var matchingResponse = parsedData.GDSSDKResponse
                    .FirstOrDefault(r => string.Equals(r.Identifier, searchTerm, StringComparison.OrdinalIgnoreCase));

                if (matchingResponse != null && matchingResponse.Rows != null)
                {
                    var result = matchingResponse.Rows
                        .Select((row, index) => new ResultItem
                        {
                            Id = index + 1,
                            CompanyName = row.Row.FirstOrDefault() ?? "",  // First element as "CompanyName"
                            AsOfDate = row.Row.ElementAtOrDefault(1) ?? ""  // Second element as "AsOfDate" if exists
                        })
                        .ToList();

                    return Ok(result);
                }

                return NotFound("No matching data found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while processing the request: {ex.Message}");
            }
        }

        [HttpGet("GetDataSourceSearch4")]
        public IActionResult GetDataSourceSearch4(string searchTerm)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return BadRequest("Search term cannot be empty.");
            }

            // Load data from JSON file
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "LexMachina.json");
            if (!System.IO.File.Exists(jsonFilePath))
            {
                return NotFound("Data file not found.");
            }

            try
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<LexMachina>(jsonData);

                if (parsedData?.parties == null)
                {
                    return NotFound("No data available in the file.");
                }

                // Find the matching Identifier and process Rows
                var matchingParties = parsedData.parties
                    .Where(p => p.name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                if (matchingParties.Any())
                {
                    return Ok(matchingParties);
                }

                return NotFound("No matching data found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while processing the request: {ex.Message}");
            }
        }

        [HttpGet("GetDataSourceSearch5")]
        public IActionResult GetDataSourceSearch5(string searchTerm)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return BadRequest("Search term cannot be empty.");
            }

            // Load data from JSON file
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "CMInfo.json");
            if (!System.IO.File.Exists(jsonFilePath))
            {
                return NotFound("Data file not found.");
            }

            try
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<List<CMDetail>>(jsonData);

                if (parsedData == null)
                {
                    return NotFound("No data available in the file.");
                }

                // Search across all fields of CMDetail
                var matchingParties = parsedData
                    .Where(p => p.GetType()
                                 .GetProperties()
                                 .Any(prop =>
                                 {
                                     var value = prop.GetValue(p)?.ToString();
                                     return !string.IsNullOrEmpty(value) &&
                                            value.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
                                 }))
                    .ToList();

                if (matchingParties.Any())
                {
                    return Ok(matchingParties);
                }

                return NotFound("No matching data found.");

            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while processing the request: {ex.Message}");
            }
        }

        [HttpPost("GetDataSourceSearch6")]
        public IActionResult GetDataSourceSearch6([FromBody] SearchRequest request)
        {
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "ContactInfo.json");
            if (!System.IO.File.Exists(jsonFilePath))
                return NotFound("Data file not found.");

            try
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<List<ContactDetail>>(jsonData);
                if (parsedData == null)
                    return NotFound("No data available in the file.");

                var firstName = request.FirstName?.Trim();
                var lastName = request.LastName?.Trim();

                var hasFirst = !string.IsNullOrWhiteSpace(firstName);
                var hasLast = !string.IsNullOrWhiteSpace(lastName);

                if (!hasFirst && !hasLast)
                {
                    // If no criteria, decide what you want. You said return all earlier:
                    return Ok(parsedData);
                    // Or: return BadRequest("Provide firstName and/or lastName.");
                }

                bool NameContains(string? value, string term) =>
                    !string.IsNullOrEmpty(value) &&
                    value.Contains(term, StringComparison.OrdinalIgnoreCase);

                var results = parsedData.Where(p =>
                    (hasFirst && (NameContains(p.contactFirstName, firstName!) ||
                                  NameContains(p.dlaFirstName, firstName!)))
                    ||
                    (hasLast && (NameContains(p.contactLastName, lastName!) ||
                                  NameContains(p.dlaLastName, lastName!)))
                ).ToList();

                if (results.Count > 0) return Ok(results);
                return NotFound("No matching data found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while processing the request: {ex.Message}");
            }
        }


        [HttpGet("GetAllDataSourceSearch6")]
        public IActionResult GetAllDataSourceSearch6()
        {
            // Load data from JSON file
            var jsonFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "ContactInfo.json");
            if (!System.IO.File.Exists(jsonFilePath))
            {
                return NotFound("Data file not found.");
            }

            try
            {
                var jsonData = System.IO.File.ReadAllText(jsonFilePath);
                var parsedData = JsonConvert.DeserializeObject<List<ContactDetail>>(jsonData);

                if (parsedData == null)
                {
                    return NotFound("No data available in the file.");
                }

                return Ok(parsedData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while processing the request: {ex.Message}");
            }
        }

        private JsonResult GetCompanyData(string companyName)
        {
            if (string.IsNullOrWhiteSpace(companyName))
            {
                return new JsonResult(new { message = "Company name is required." }) { StatusCode = 400 };
            }

            var companyIdentifier = string.Empty;

            // Step 1: Read from Header2.json to get companyIdentifier
            var header2Path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "Header2.json");
            if (System.IO.File.Exists(header2Path))
            {
                var header2Data = System.IO.File.ReadAllText(header2Path);
                var parsedHeader2 = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(header2Data);

                var matchingResponse = parsedHeader2?.GDSSDKResponse
                    .FirstOrDefault(r => string.Equals(r.Identifier, companyName, StringComparison.OrdinalIgnoreCase));

                if (matchingResponse != null && matchingResponse.Rows != null && matchingResponse.Rows.Count > 0)
                {
                    companyIdentifier = matchingResponse.Rows[0].Row[0];
                }
            }

            if (string.IsNullOrEmpty(companyIdentifier))
            {
                return new JsonResult(new { message = "Company identifier not found." }) { StatusCode = 404 };
            }

            // Step 2: Read from Detail1.json
            var detail1Path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "Detail1.json");
            var detail1Data = new List<dynamic>();
            if (System.IO.File.Exists(detail1Path))
            {
                var detail1JsonData = System.IO.File.ReadAllText(detail1Path);
                var parsedDetail1 = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(detail1JsonData);

                var matchingDetails = parsedDetail1?.GDSSDKResponse
                    .Where(r => string.Equals(r.Identifier, companyIdentifier, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingDetails != null && matchingDetails.Any())
                {
                    detail1Data = matchingDetails.Select(detail => new
                    {
                        Headers = detail.Headers?.Where(header => !string.IsNullOrWhiteSpace(header)).ToList(),
                        Values = detail.Rows
                            .Where(row => row.Row.Any(value => !string.IsNullOrWhiteSpace(value)))
                            .Select(row => row.Row.Where(value => !string.IsNullOrWhiteSpace(value)).ToList())
                    }).ToList<dynamic>();
                }
            }

            // Step 3: Read from TransactionHeader.json to find matching transaction identifiers
            var transactionHeaderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "TransactionHeader.json");
            var transactionIdentifiers = new List<string>();

            if (System.IO.File.Exists(transactionHeaderPath))
            {
                var headerJsonData = System.IO.File.ReadAllText(transactionHeaderPath);
                var parsedHeaderData = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(headerJsonData);

                var matchingHeader = parsedHeaderData?.GDSSDKResponse
                    .FirstOrDefault(r => string.Equals(r.Identifier, companyIdentifier, StringComparison.OrdinalIgnoreCase));

                if (matchingHeader != null && matchingHeader.Rows != null)
                {
                    transactionIdentifiers = matchingHeader.Rows
                        .Select(row => row.Row[0])
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .ToList();
                }
            }

            if (!transactionIdentifiers.Any())
            {
                return new JsonResult(new { message = "No transaction identifiers found." }) { StatusCode = 404 };
            }

            // Step 4: Read from TransactionDetails.json to get details for each transaction identifier
            var transactionDetailsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Data", "TransactionDetails.json");
            var transactionDetails = new List<dynamic>();

            if (System.IO.File.Exists(transactionDetailsPath))
            {
                var detailJsonData = System.IO.File.ReadAllText(transactionDetailsPath);
                var parsedDetailData = JsonConvert.DeserializeObject<GDSSDKResponseRoot>(detailJsonData);

                foreach (var identifier in transactionIdentifiers)
                {
                    var matchingDetails = parsedDetailData?.GDSSDKResponse
                        .Where(r => string.Equals(r.Identifier, identifier, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (matchingDetails != null && matchingDetails.Any())
                    {
                        transactionDetails.AddRange(matchingDetails.Select(detail => new
                        {
                            Identifier = detail.Identifier,
                            Headers = detail.Headers?.Where(header => !string.IsNullOrWhiteSpace(header)).ToList(),
                            Values = detail.Rows
                                .Where(row => row.Row.Any(value => !string.IsNullOrWhiteSpace(value)))
                                .Select(row => row.Row.Where(value => !string.IsNullOrWhiteSpace(value)).ToList())
                        }));
                    }
                }
            }

            // Combine Detail1 and TransactionDetails data into a single response
            return new JsonResult(new
            {
                CompanyIdentifier = companyIdentifier,
                Detail1Data = detail1Data,
                TransactionData = transactionDetails
            });
        }

    }

    public class StoreCompanyDetailsRequest
    {
        public string Title { get; set; }
        public string Details { get; set; }
    }

    public class SelectCompanyDetails
    {
        public string CompanyName { get; set; }
        public string DataSource { get; set; }
        public bool CompanyDetails { get; set; }
        public bool Professionals { get; set; }
        public bool Transactions { get; set; }
    }

    public class CompanyDetailsResponse
    {
        public List<DetailItem> Detail1Data { get; set; }
        public List<TransactionItem> TransactionData { get; set; }
    }

    public class DetailItem
    {
        public List<string> Headers { get; set; }
        public List<List<string>> Values { get; set; }
    }

    public class TransactionItem
    {
        public string Identifier { get; set; }
        public List<string> Headers { get; set; }
        public List<List<string>> Values { get; set; }
    }

    public class CMDetail
    {
        public string clientNumber { get; set; }
        public string clientName { get; set; }
        public string status { get; set; }
        public string openDate { get; set; }
    }

    public class ContactDetail
    {
        public string company { get; set; }
        public string clientMatterNumber { get; set; }
        public string contactFirstName { get; set; }
        public string contactLastName { get; set; }
        public string dlaFirstName { get; set; }
        public string dlaLastName { get; set; }
        public string dlaContactIdentity { get; set; }
    }

    public class SearchRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
