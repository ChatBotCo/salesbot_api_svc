using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System;
using SalesBotApi.Models;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using static OpenAiHttpRequestService;
using static AzureSpeechService;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using static HubspotService;

namespace SalesBotApi.Controllers
{
    [Route("api/[controller]")] 
    [ApiController]
    public class AiController : Controller
    {
        private readonly OpenAiHttpRequestService openAiHttpRequestService;
        private readonly MemoryStoreService memoryStoreService;
        private readonly SharedQueriesService sharedQueriesService;
        private readonly Container messagesContainer;
        private readonly AzureSpeechService azureSpeechService;
        private readonly EmailService emailService;
        private readonly QueueService<HubspotUpdateQueueMessage> queueService;
        private readonly LogBufferService logger;
        private readonly MetricsBufferService metrics;

        public AiController(
            OpenAiHttpRequestService _openAiHttpRequestService, 
            MemoryStoreService _memoryStoreService,
            SharedQueriesService _sharedQueriesService,
            CosmosDbService cosmosDbService,
            AzureSpeechService azureSpeechService,
            EmailService emailService,
            HubspotService hubspotService,
            LogBufferService logger,
            MetricsBufferService metricsBufferService,
            QueueService<HubspotUpdateQueueMessage> queueService
        )
        {
            openAiHttpRequestService = _openAiHttpRequestService;
            memoryStoreService = _memoryStoreService;
            sharedQueriesService = _sharedQueriesService;
            messagesContainer = cosmosDbService.MessagesContainer;
            this.azureSpeechService = azureSpeechService;
            this.emailService = emailService;
            this.logger = logger;
            this.metrics = metricsBufferService;
            this.queueService = queueService;
        }

        // PUT: api/ai/submit_user_question
        [HttpPut("submit_user_question")]
        public async Task<ActionResult<SubmitResponse>> SubmitUserQuestion(
            [FromBody] SubmitRequest req,
            [FromQuery] string companyid,
            [FromQuery] string convoid
        )
        {
            Stopwatch stopwatch1 = Stopwatch.StartNew();
            if(req==null || req.user_msg==null){
                return BadRequest();
            }

            Console.WriteLine($"METRICS *** START ***");

            Company company = null;
            Conversation convo = null;
            Chatbot chatbot = null;
            IEnumerable<Message> messages = null;
            IEnumerable<Refinement> refinements = null;
            // float[] vectorFloatArrAzure = null;
            float[] vectorFloatArrOpenai = null;

            Stopwatch stopwatch = Stopwatch.StartNew();
            Task<IEnumerable<Message>> msgsTask;
            try
            {
                // Message cannot by effectively cached so I'm pulling this out
                msgsTask = sharedQueriesService.GetRecentMsgsForConvo(convoid, 4);
                var companyTask = sharedQueriesService.GetCompanyById(companyid);
                var convoTask = sharedQueriesService.GetConversationById(convoid);
                var chatbotTask = sharedQueriesService.GetFirstChatbotByCompanyId(companyid);
                var refinementsTask = sharedQueriesService.GetRefinementsByCompanyId(companyid);
                // var vectorTaskAzure = memoryStoreService.GetVectorAzure(req.user_msg);
                var vectorTaskOpenai = memoryStoreService.GetVectorOpenAi(req.user_msg);

                await Task.WhenAll(
                    companyTask, 
                    convoTask, 
                    chatbotTask, 
                    refinementsTask,
                    // msgsTask,
                    // vectorTaskAzure,
                    vectorTaskOpenai
                );

                // After all tasks are complete, you can assign the results
                company = await companyTask;
                convo = await convoTask;
                chatbot = await chatbotTask;
                refinements = await refinementsTask;
                // messages = await msgsTask;
                // vectorFloatArrAzure = await vectorTaskAzure;
                vectorFloatArrOpenai = await vectorTaskOpenai;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return NotFound();
            }
            stopwatch.Stop();
            Console.WriteLine($"METRICS (COSMOS & OpenAI-Embeddings) Load cosmos data: {stopwatch.ElapsedMilliseconds} ms");
            metrics.Duration("submit_user_question.load_cosmos_data.ms", stopwatch.ElapsedMilliseconds);

            // CompareFloatArrays(vectorFloatArrAzure, vectorFloatArrOpenai);

            float[] vectorFloatArr = vectorFloatArrOpenai;

            string[] contextDocs = await GetRelevantContexts(vectorFloatArr, companyid) ;

            messages = await msgsTask;

            AssistantResponse assistantResponse = await SubmitUserQuestion(
                req.user_msg, 
                contextDocs, 
                company, 
                convo, 
                chatbot, 
                refinements,
                messages
            );

            stopwatch = Stopwatch.StartNew();
            var speechTask = GetSpeech(assistantResponse.assistant_response, req.mute);
            string newMsgId = Guid.NewGuid().ToString();
            var insertMsgTask = InsertNewMessage(
                newMsgId,
                convoid,
                companyid,
                req.user_msg,
                assistantResponse
            );
            var leadsEmailTask = SendLeadGenEmailIfNeeded(
                company.email_for_leads, 
                assistantResponse,
                convoid
            );
            var hubspotTask = UpdateHubspotIfNeeded(
                newMsgId,
                company, 
                assistantResponse,
                convo
            );
            
            await Task.WhenAll(speechTask, insertMsgTask, leadsEmailTask, hubspotTask);
            
            SpeechResults speechResults = await speechTask;
            await insertMsgTask;
            await leadsEmailTask;
            await hubspotTask;
            stopwatch.Stop();
            Console.WriteLine($"METRICS (COSMOS & Azure-Speech) Speech & insert-msg: {stopwatch.ElapsedMilliseconds} ms");
            metrics.Duration("submit_user_question.speech_and_insert_msg.ms", stopwatch.ElapsedMilliseconds);

            SubmitResponse submitResponse = new SubmitResponse() {
                assistant_response = new SubmitResponseAssistantResponse(){role="assistant", content=assistantResponse.assistant_response},
                redirect_url = assistantResponse.redirect_url,
                lipsync = speechResults.lipsync,
                audio = speechResults.audio
            };

            Console.WriteLine($"METRICS *** END ***\n");
            stopwatch1.Stop();
            Console.WriteLine($"METRICS (Full) SubmitUserMessage: {stopwatch1.ElapsedMilliseconds} ms");
            metrics.Duration("submit_user_question.func_duration.ms", stopwatch1.ElapsedMilliseconds);

            return Ok(submitResponse);
        }

        private async Task SendLeadGenEmailIfNeeded(
            string recipient_email, 
            AssistantResponse assistantResponse,
            string convo_id
        ) {
            if (
                assistantResponse.user_email != null ||
                assistantResponse.user_phone_number != null ||
                assistantResponse.user_wants_to_schedule_call_with_sales_rep ||
                assistantResponse.user_wants_to_be_contacted
            ) {
                await emailService.SendLeadGeneratedEmail(recipient_email, assistantResponse, convo_id);
            }
        }

        private async Task UpdateHubspotIfNeeded(
            string newMsgId,
            Company company, 
            AssistantResponse assistantResponse,
            Conversation convo
        ) {
            // if (
            //     assistantResponse.user_first_name != null ||
            //     assistantResponse.user_last_name != null ||
            //     assistantResponse.user_email != null ||
            //     assistantResponse.user_phone_number != null ||
            //     assistantResponse.user_wants_to_schedule_call_with_sales_rep ||
            //     assistantResponse.user_wants_to_be_contacted
            // ) {
            //     HubspotUpdateQueueMessage msg = new HubspotUpdateQueueMessage{
            //         company_id = company.company_id,
            //         convo_id = convo.id,
            //         msg_id = newMsgId
            //     };
            //     await queueService.EnqueueMessageAsync(msg);
            // }
        }

        private async Task<SpeechResults> GetSpeech(string text, bool mute) {
            SpeechResults speechResults;
            if(!mute) {
                speechResults = await azureSpeechService.GetSpeech(text);
            } else {
                Stopwatch stopwatch = Stopwatch.StartNew();
                speechResults = new SpeechResults() {
                    lipsync = new LipSyncResults(),
                    audio = ""
                };
                stopwatch.Stop();
                string mutedStr = mute ? "(muted)": "";
                Console.WriteLine($"--> METRICS (Azure-Speech) Get speech response: {stopwatch.ElapsedMilliseconds} ms {mutedStr}");
                metrics.Duration("azure_speech_response.ms", stopwatch.ElapsedMilliseconds);
            }
            return speechResults;
        }

        private async Task<string[]> GetRelevantContexts(float[] vectorFloatArr, string companyId) 
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string[] contextDocs = await memoryStoreService.GetRelevantContexts(vectorFloatArr, companyId);
            Console.WriteLine($"contextDocs:{contextDocs.Length}");
            metrics.Count("pinecone_get_contexts", contextDocs.Length, tag:companyId);
            stopwatch.Stop();
            Console.WriteLine($"METRICS (PINECONE) Get vector contexts: {stopwatch.ElapsedMilliseconds} ms");
            metrics.Duration("pinecone_get_contexts.ms", stopwatch.ElapsedMilliseconds);
            return contextDocs;
        }

        private async Task<AssistantResponse> SubmitUserQuestion(
            string user_msg, 
            string[] contextDocs,
            Company company,
            Conversation convo,
            Chatbot chatbot,
            IEnumerable<Refinement> refinements,
            IEnumerable<Message> messages
        )
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            AssistantResponse assistantResponse = await openAiHttpRequestService.SubmitUserQuestion(
                user_msg, 
                contextDocs, 
                company, 
                convo, 
                chatbot, 
                refinements,
                messages
            );
            stopwatch.Stop();
            Console.WriteLine($"METRICS (OpenAI) Get chat assistant response: {stopwatch.ElapsedMilliseconds} ms");
            metrics.Duration("openai_llm_request.ms", stopwatch.ElapsedMilliseconds);
            return assistantResponse;
        }

        private async Task InsertNewMessage(
            string newMsgId,
            string convoid,
            string company_id,
            string user_msg,
            AssistantResponse assistantResponse
        ) {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Message message = new Message {
                id = newMsgId,
                conversation_id = convoid,
                user_msg = user_msg,
                assistant_response = assistantResponse.assistant_response,
                company_id = company_id,
                user_wants_to_be_contacted = assistantResponse.user_wants_to_be_contacted,
                user_wants_to_install_the_demo = assistantResponse.user_wants_to_install_the_demo,
                user_wants_to_schedule_call_with_sales_rep = assistantResponse.user_wants_to_schedule_call_with_sales_rep,
                user_first_name = assistantResponse.user_first_name,
                user_last_name = assistantResponse.user_last_name,
                user_email = assistantResponse.user_email,
                user_phone_number = assistantResponse.user_phone_number,
                redirect_url = assistantResponse.redirect_url
            };
            await messagesContainer.CreateItemAsync(message, new PartitionKey(convoid));
            stopwatch.Stop();
            Console.WriteLine($"--> METRICS (Cosmos) Insert new message: {stopwatch.ElapsedMilliseconds} ms");
            metrics.Duration("cosmos_insert_new_msg.ms", stopwatch.ElapsedMilliseconds);
        }

        // This is only for debugging, evaluation, and testing.  This function can be deleted at anytime
        private void CompareFloatArrays(float[] arr1, float[] arr2)
        {
            // Check if either array is null
            if (arr1 == null || arr2 == null)
            {
                Console.WriteLine("METRICS (CompareFloatArrays) One or both arrays are null.");
                return;
            }

            // Check if arrays have the same length
            if (arr1.Length != arr2.Length)
            {
                Console.WriteLine("METRICS (CompareFloatArrays) Arrays have different lengths.");
                return;
            }

            // Tolerance for floating point comparison
            float tolerance = 0.000001f;

            for (int i = 0; i < arr1.Length; i++)
            {
                if (Math.Abs(arr1[i] - arr2[i]) > tolerance)
                {
                    Console.WriteLine($"METRICS (CompareFloatArrays) Arrays differ at index {i}. Values are {arr1[i]} and {arr2[i]}.");
                    return;
                }
            }

            Console.WriteLine("METRICS (CompareFloatArrays) Arrays are equal.");
        }

    }//class AiController
    
    public class SubmitResponse {
        public SubmitResponseAssistantResponse assistant_response { get; set; }
        public string redirect_url { get; set; }
        public LipSyncResults lipsync { get; set; }
        public string audio { get; set; }
    }
    public class SubmitResponseAssistantResponse {
        public string role { get; set; }
        public string content { get; set; }
    }

    public class SubmitRequest {
        public string user_msg { get; set; }
        public bool mute { get; set; }
    }

    public class GptMessage {
        public string role { get; set; }
        public string content { get; set; }
    }
}
