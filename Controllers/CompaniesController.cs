using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SalesBotApi.Models;
using Microsoft.Azure.Cosmos;
using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.Net.Mail;
using System.Net;

namespace SalesBotApi.Controllers
{
    [Route("api/[controller]")] 
    [ApiController]
    public class CompaniesController : Controller
    {
        private readonly Container companiesContainer;
        private readonly Container usersContainer;
        private readonly Container chatbotsContainer;
        private readonly LogBufferService logger;
        private readonly ICacheProvider<Company> cacheCompany;
        private readonly ICacheProvider<Chatbot> cacheChatbot;
        private readonly SharedQueriesService sharedQueriesService;
        private readonly HubspotService hubspotService;

        public CompaniesController(
            CosmosDbService cosmosDbService, 
            LogBufferService logger,
            InMemoryCacheService<Company> cacheCompany,
            SharedQueriesService sharedQueriesService,
            InMemoryCacheService<Chatbot> cacheChatbot,
            HubspotService hubspotService
            )
        {
            this.logger = logger;
            companiesContainer = cosmosDbService.CompaniesContainer;
            usersContainer = cosmosDbService.UsersContainer;
            chatbotsContainer = cosmosDbService.ChatbotsContainer;
            this.cacheCompany = cacheCompany;
            this.sharedQueriesService = sharedQueriesService;
            this.cacheChatbot = cacheChatbot;
            this.hubspotService = hubspotService;
        }

        // GET: api/companies
        [HttpGet]
        [JwtAuthorize]
        public async Task<ActionResult<IEnumerable<Company>>> GetCompanies()
        {
            JwtPayload userData = HttpContext.Items["UserData"] as JwtPayload;
            string company_id = userData.company_id;
            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            IEnumerable<Company> companies = await GetCompanies(company_id);
            return Ok(companies);
        }

        // GET: api/companies/client?company_id=...
        [HttpGet("client")]
        public async Task<ActionResult<Company>> GetCompaniesNoAuth([FromQuery] string company_id)
        {
            if (company_id == null)
            {
                return BadRequest("Missing company_id parameter");
            }
            Company company = await sharedQueriesService.GetCompanyById(company_id);
            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            if(company == null) {
                return NotFound();
            }
            return Ok(company);
        }

        // POST: api/companies
        [HttpPost]
        [JwtAuthorize]
        public async Task<ActionResult<NewCompanyResponse>> CreateNewCompany([FromBody] NewCompanyRequest newCompanyReq)
        {
            if (newCompanyReq.name == null || newCompanyReq.description == null)
            {
                return BadRequest("Invalid request, missing parameters");
            }

            JwtPayload userData = HttpContext.Items["UserData"] as JwtPayload;
            string user_id = userData.id;

            UserWithPassword user = await GetUserById(user_id);
            if(user.company_id != "XXX" && user.company_id != "all") {
                // Right now we're only letting each user create one company
                return BadRequest();
            }
            string oldPartitionKeyValue = user.company_id;
            if(oldPartitionKeyValue == null || oldPartitionKeyValue == "") {
                logger.Error($"oldPartitionKeyValue is empty or null:{oldPartitionKeyValue} - This means this request will probably fail.  You need to prime new accounts with 'company_id' == 'XXX'");
                return BadRequest("Invalid request, check the logs");
            }
            if (user == null)
            {
                return BadRequest("User not found.");
            }

            string companyId = Regex.Replace(newCompanyReq.name.ToLower(), @"[^a-z0-9]", "");

            Company company = await sharedQueriesService.GetCompanyById(companyId);
            if(company != null)
            {
                return Conflict("Company already exists");
            }

            user.company_id = companyId;
            await usersContainer.CreateItemAsync(user, new PartitionKey(user.company_id));
            await usersContainer.DeleteItemAsync<UserWithPassword>(user.id, new PartitionKey(oldPartitionKeyValue));
            await usersContainer.ReplaceItemAsync(user, user.id, new PartitionKey(user.company_id));

            Company newCompany = new Company
            {
                id = Guid.NewGuid().ToString(),
                company_id = companyId,
                name = newCompanyReq.name,
                description = newCompanyReq.description,
                email_for_leads = newCompanyReq.email_for_leads,
            };

            await companiesContainer.CreateItemAsync(newCompany, new PartitionKey(companyId));
            cacheCompany.Clear(companyId);

            // These are the "DEFAULT" values for new chatbots
            Chatbot newChatbot = new Chatbot
            {
                id = Guid.NewGuid().ToString(),
                company_id = companyId,
                llm_model = "gpt-3.5-turbo-0125",
                greeting = $"Hi! I'm GreeterBot! I can answer your questions about {newCompany.name}.",
                avatar_view = "headshot",
                role_sales = true,
                role_support = true
            };
            await chatbotsContainer.CreateItemAsync(newChatbot, new PartitionKey(companyId));
            cacheChatbot.Clear(newChatbot.company_id);
            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            NewCompanyResponse newCompanyResponse = new NewCompanyResponse
            {
                company = newCompany,
                updated_jwt = JwtService.CreateToken(user)
            };
            return Ok(newCompanyResponse);
        }

        // PUT: api/companies
        [HttpPut]
        [JwtAuthorize]
        public async Task<IActionResult> UpdateCompany([FromBody] Company company)
        {
            JwtPayload userData = HttpContext.Items["UserData"] as JwtPayload;
            string company_id = userData.company_id;
            if(company_id != "all") {
                if(company.company_id != company_id) {
                    return Unauthorized();
                }
            }

            try
            {
                await sharedQueriesService.PatchCompany(company);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return NotFound();
            }

            if(!string.IsNullOrEmpty(company.hubspot_access_token)) {
                try
            {
                if(!company.hubspot_initialized) {
                    await hubspotService.InitializeHubspotAccount(company);
                }
            } catch(Exception e) {
                logger.Error($"Failed to initialize Hubspot for company: {company.company_id}: {e.Message}");
                throw;
            }
            }

            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            return NoContent();
        }

        private async Task<UserWithPassword> GetUserById(string user_id) {
            string sqlQueryText = $"SELECT * FROM c WHERE c.id = '{user_id}' OFFSET 0 LIMIT 1";

            QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);

            UserWithPassword user = null;
            using (FeedIterator<UserWithPassword> feedIterator = usersContainer.GetItemQueryIterator<UserWithPassword>(queryDefinition))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<UserWithPassword> response = await feedIterator.ReadNextAsync();
                    user = response.First();
                    break;
                }
            }
            return user;
        }

        private async Task<IEnumerable<Company>> GetCompanies(string company_id) {
            if (company_id != "all") {
                Company[] singleCompany = new Company[1];
                singleCompany[0] = await sharedQueriesService.GetCompanyById(company_id);
                return singleCompany;
            }

            // Get "all" companies
            string sqlQueryText = $"SELECT * FROM c";

            QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);

            List<Company> companies = new List<Company>();
            using (FeedIterator<Company> feedIterator = companiesContainer.GetItemQueryIterator<Company>(queryDefinition))
            {
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<Company> response = await feedIterator.ReadNextAsync();
                    companies.AddRange(response.ToList());
                }
            }
            return companies;
        }

    }
}
