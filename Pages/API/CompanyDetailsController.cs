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
            if (string.IsNullOrWhiteSpace(request.Title))
                return BadRequest("Title is required.");

            // Expect: request.Details is a JSON array of SelectCompanyDetails like the sample you posted
            var selectCompanyDetails = JsonConvert.DeserializeObject<List<CompanyDetailInfo>>(request.Details) ?? new();

            string connectionString = "Server=KEITHLAPTOP\\SQLEXPRESS;Database=CompanyData;Integrated Security=True;TrustServerCertificate=True;";

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    foreach (var companyDetails in selectCompanyDetails)
                    {
                        // ----- fetch your rich details as before (for CoreData/Professionals/Transactions) -----
                        var companyDetailsResult = GetCompanyData(companyDetails.CompanyName.Replace("\\'", "'"));
                        var details = companyDetailsResult.Value;

                        var detailsJson = JsonConvert.SerializeObject(details);
                        var detailsData = JsonConvert.DeserializeObject<CompanyDetailsResponse>(detailsJson);

                        var detail1Data = detailsData?.Detail1Data ?? new List<DetailItem>();
                        var transactionData = detailsData?.TransactionData ?? new List<TransactionItem>();

                        // ----- Request row per selected company (keeping your original behavior) -----
                        int requestId;
                        const string insertRequestQuery = @"
                    INSERT INTO [dbo].[Request] (RequestName, RequestType, CreateUserIdentity, CreateDate)
                    OUTPUT INSERTED.RequestId
                    VALUES (@RequestName, @RequestType, @CreateUserIdentity, GETDATE())";

                        using (var cmd = new SqlCommand(insertRequestQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@RequestName", request.Title);
                            cmd.Parameters.AddWithValue("@RequestType", companyDetails.CompanyName ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@CreateUserIdentity", "SystemUser");
                            requestId = (int)await cmd.ExecuteScalarAsync();
                        }

                        // ====== NEW: Contacts insert (Data Source 4 / RowData) ======
                        // If the selected item includes RowData (your contact row), persist it.
                        if (companyDetails.RowData != null)
                        {
                            // Map RowData to your Contacts table columns
                            var rd = companyDetails.RowData;

                            const string insertContactSql = @"
                        INSERT INTO [dbo].[Contacts]
                        (
                            fkRequestID, CreateDate, CreateUserIdentity, ModifiedDate, ModifiedUserIdentity,
                            ClientNumber, ClientName, ClientContactFirstName, ClientContactLastName,
                            DLAPContactFirstName, DLAPContactLastName, DLAPContactIdentity
                        )
                        VALUES
                        (
                            @fkRequestID, GETDATE(), @CreateUserIdentity, GETDATE(), @ModifiedUserIdentity,
                            @ClientNumber, @ClientName, @ClientContactFirstName, @ClientContactLastName,
                            @DLAPContactFirstName, @DLAPContactLastName, @DLAPContactIdentity
                        );";

                            using (var cmd = new SqlCommand(insertContactSql, connection))
                            {
                                cmd.Parameters.AddWithValue("@fkRequestID", requestId);
                                cmd.Parameters.AddWithValue("@CreateUserIdentity", "SystemUser");
                                cmd.Parameters.AddWithValue("@ModifiedUserIdentity", "SystemUser");

                                // RowData → DB columns
                                cmd.Parameters.AddWithValue("@ClientNumber", (object?)rd.clientMatterNumber ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@ClientName", (object?)rd.company ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@ClientContactFirstName", (object?)rd.contactFirstName ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@ClientContactLastName", (object?)rd.contactLastName ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@DLAPContactFirstName", (object?)rd.dlaFirstName ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@DLAPContactLastName", (object?)rd.dlaLastName ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@DLAPContactIdentity", (object?)rd.dlaContactIdentity ?? DBNull.Value);

                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // ====== Existing: CompanyDetails → CoreData ======
                        if (companyDetails.CompanyDetails)
                        {
                            string entityName = null, entityAddress = null, entityPhone = null,
                                   entityRevenue = null, entityEmployees = null, entityYearFounded = null,
                                   entityWebUrl = null, entityDescription = null;

                            foreach (var item in detail1Data)
                            {
                                var headers = item.Headers as List<string>;
                                var values = item.Values as List<List<string>>;
                                if (headers == null || values == null) continue;

                                for (int i = 0; i < headers.Count; i++)
                                {
                                    var header = headers[i];
                                    var value = values.FirstOrDefault()?.ElementAtOrDefault(0);

                                    switch (header)
                                    {
                                        case "IQ_COMPANY_NAME": entityName = value; break;
                                        case "IQ_COMPANY_ADDRESS": entityAddress = value; break;
                                        case "IQ_COMPANY_PHONE": entityPhone = value; break;
                                        case "IQ_TOTAL_REV": entityRevenue = value; break;
                                        case "IQ_EMPLOYEES": entityEmployees = value; break;
                                        case "IQ_YEAR_FOUNDED": entityYearFounded = value; break;
                                        case "IQ_COMPANY_WEBSITE": entityWebUrl = value; break;
                                        case "IQ_BUSINESS_DESCRIPTION": entityDescription = value; break;
                                    }
                                }
                            }

                            const string insertCoreDataSql = @"
                        INSERT INTO CoreData
                        (fkRequestID, EntityName, EntityAddress, EntityPhone, EntityRevenue, EntityEmployeeCount,
                         EntityYearFounded, EntityWebUrl, EntityOverviewer, CreateDate, CreateUserIdentity)
                        VALUES
                        (@fkRequestID, @EntityName, @EntityAddress, @EntityPhone, @EntityRevenue, @EntityEmployeeCount,
                         @EntityYearFounded, @EntityWebUrl, @EntityOverviewer, @CreateDate, @CreateUserIdentity)";

                            using (var cmd = new SqlCommand(insertCoreDataSql, connection))
                            {
                                cmd.Parameters.AddWithValue("@fkRequestID", requestId);
                                cmd.Parameters.AddWithValue("@EntityName", (object?)entityName ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@EntityAddress", (object?)entityAddress ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@EntityPhone", (object?)entityPhone ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@EntityRevenue", (object?)entityRevenue ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@EntityEmployeeCount", (object?)entityEmployees ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@EntityYearFounded", (object?)entityYearFounded ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@EntityWebUrl", (object?)entityWebUrl ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@EntityOverviewer", (object?)entityDescription ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                cmd.Parameters.AddWithValue("@CreateUserIdentity", "SystemUser");
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // ====== Existing: Professionals → KeyExecutives ======
                        if (companyDetails.Professionals)
                        {
                            var professionalData = detail1Data
                                .Where(item => item.Headers.Contains("IQ_PROFESSIONAL"))
                                .SelectMany(item => item.Values)
                                .ToList();

                            var professionalTitles = detail1Data
                                .Where(item => item.Headers.Contains("IQ_PROFESSIONAL_TITLE"))
                                .SelectMany(item => item.Values)
                                .ToList();

                            const string insertExecSql = @"
                        INSERT INTO [dbo].[KeyExecutives]
                        (fkRequestID, Title, Name, CreateDate, CreateUserIdentity)
                        VALUES
                        (@RequestId, @Title, @Name, GETDATE(), @CreateUserIdentity)";

                            for (int i = 0; i < professionalData.Count; i++)
                            {
                                var title = professionalTitles.ElementAtOrDefault(i)?.FirstOrDefault() ?? "Unknown Title";
                                var name = professionalData.ElementAtOrDefault(i)?.FirstOrDefault() ?? "Unknown Name";

                                using (var cmd = new SqlCommand(insertExecSql, connection))
                                {
                                    cmd.Parameters.AddWithValue("@RequestId", requestId);
                                    cmd.Parameters.AddWithValue("@Title", title);
                                    cmd.Parameters.AddWithValue("@Name", name);
                                    cmd.Parameters.AddWithValue("@CreateUserIdentity", "SystemUser");
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        // ====== Existing: Transactions → TransactionSummary ======
                        if (companyDetails.Transactions)
                        {
                            const string insertTxnSql = @"
                        INSERT INTO [dbo].[TransactionSummary]
                        (fkRequestID, TransactionDate, TransactionDescription, TransactionValue, TransactionCounsel, CreateDate, CreateUserIdentity)
                        VALUES
                        (@RequestId, @TransactionDate, @TransactionDescription, @TransactionValue, @TransactionCounsel, GETDATE(), @CreateUserIdentity)";

                            foreach (var txn in transactionData)
                            {
                                var headers = txn.Headers ?? new List<string>();
                                var values = txn.Values ?? new List<List<string>>();

                                for (int i = 0; i < headers.Count; i++)
                                {
                                    var desc = headers[i];
                                    var val = values.ElementAtOrDefault(i)?.FirstOrDefault();

                                    using (var cmd = new SqlCommand(insertTxnSql, connection))
                                    {
                                        cmd.Parameters.AddWithValue("@RequestId", requestId);
                                        cmd.Parameters.AddWithValue("@TransactionDate", DateTime.Now);
                                        cmd.Parameters.AddWithValue("@TransactionDescription", (object?)desc ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@TransactionValue", (object?)val ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@TransactionCounsel", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@CreateUserIdentity", "SystemUser");
                                        await cmd.ExecuteNonQueryAsync();
                                    }
                                }
                            }
                        }
                    } // foreach selection
                } // using connection

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

    public class CompanyDetailInfo
    {
        public string CompanyName { get; set; }
        public string DataSource { get; set; }
        public bool CompanyDetails { get; set; }
        public bool Professionals { get; set; }
        public bool Transactions { get; set; }
        public bool? DLAPiperContacts { get; set; } // optional flag if you want to gate contacts
        public ContactDetail RowData { get; set; }  // <-- your cached contact row
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
