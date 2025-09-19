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

        // Classes to hold filtered results
        public class ResultItem
        {
            public int Id { get; set; }
            public string CompanyName { get; set; }
            public string AsOfDate { get; set; }
        }
    }
}
