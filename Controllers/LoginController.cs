using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;
using SalesBotApi.Models;
using Microsoft.Azure.Cosmos;
using System;

namespace SalesBotApi.Controllers
{
    [Route("api/[controller]")] 
    [ApiController]
    public class LoginController : Controller
    {

        private readonly Container usersContainer;
        private readonly PasswordHasherService passwordHasherService;

        public LoginController(
            CosmosDbService cosmosDbService,
            PasswordHasherService passwordHasherService
        )
        {
            usersContainer = cosmosDbService.UsersContainer;
            this.passwordHasherService = passwordHasherService;
        }

        // POST: api/login
        [HttpPost]
        public async Task<ActionResult<UserWithJwt>> LoginUser([FromBody] LoginRequest loginReq)
        {
            string sqlQueryText = $"SELECT * FROM c WHERE c.user_name = '{loginReq.user_name}' OFFSET 0 LIMIT 1";
            QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);

            UserWithPassword userObjCandidate = null;
            try
            {
                using (FeedIterator<UserWithPassword> feedIterator = usersContainer.GetItemQueryIterator<UserWithPassword>(queryDefinition))
                {
                    if (feedIterator.HasMoreResults)
                    {
                        FeedResponse<UserWithPassword> response = await feedIterator.ReadNextAsync();
                        userObjCandidate = response.First();
                    }
                }
            }
            catch (CosmosException)
            {
                return Unauthorized();
            }
            catch (Exception)
            {
                return Unauthorized();
            }

            string hashedPassword = passwordHasherService.HashPasswordWithSalt(loginReq.password, userObjCandidate.salt);
            if(hashedPassword != userObjCandidate.password) {
                return Unauthorized();
            }

            Response.Headers.Add("Access-Control-Allow-Origin", "*");

            if(userObjCandidate != null) {
                UserWithJwt authorizedUser = new UserWithJwt(userObjCandidate);
                authorizedUser.jwt = JwtService.CreateToken(authorizedUser);
                return Ok(authorizedUser);
            }
            else return Unauthorized();
        }
    }
}
