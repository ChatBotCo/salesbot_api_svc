using System.Net.Http;
using System.Threading.Tasks;
using SalesBotApi.Models;
using static OpenAiHttpRequestService;

public class EmailService
{
    private readonly IHttpClientFactory clientFactory;
    private readonly QueueService<EmailRequest> queueService;

    public EmailService(
        IHttpClientFactory _clientFactory,
        QueueService<EmailRequest> _queueService
    )
    {
        clientFactory = _clientFactory;
        queueService = _queueService;
    }

    public async Task SendEmail(string _sender_email, string _sender_name, string _recipient_email, string _subject, string _body)
    {
        EmailRequest emailReq = new EmailRequest
        {
            sender_email = _sender_email,
            sender_name = _sender_name,
            recipient_email = _recipient_email,
            subject = _subject,
            body = _body
        };

        await queueService.EnqueueMessageAsync(emailReq);
    }

    public async Task SendLeadGeneratedEmail(
        string recipient_email, 
        AssistantResponse assistantResponse,
        string convo_id
    ) {
            await SendEmail(
                "hello@greet.bot", 
                "GreeterBot",
                recipient_email, 
                "New website lead from GreeterBot", 
                $@"
Hello!

Good news -- you have a new website lead from GreeterBot. Here is their information:

First Name: {assistantResponse.user_first_name}
Last Name: {assistantResponse.user_last_name}
Email:  {assistantResponse.user_email}
Phone: {assistantResponse.user_phone_number}
Conversation history: https://app.greeter.bot/messages?convo_id={convo_id}

Please reach out to them as soon as possible.

Thank you,
The GreeterBot team
"
            );
        }

    public async Task SendRegistrationEmail(string recipient_email) {
            await SendEmail(
                "hello@greeter.bot", 
                "GreeterBot",
                recipient_email, 
                "GreeterBot registration received", 
                @"
Hello! 

Thanks for registering for a free account at GreeterBot. We will approve your account as quickly as possible (usually within 24 hours) and let you know. If you haven't heard from us within a few days, feel free to reply to this email. 

Thank you,
The GreeterBot team
"
            );
        }

    public async Task SendRegistrationApprovalEmail(string recipient_email) {
            await SendEmail(
                "hello@greeter.bot", 
                "GreeterBot",
                recipient_email, 
                "Your GreeterBot registration was approved", 
                @"
Hello! 

Good news -- your GreeterBot registration was approved, and your account is now active. Please log in here: https://app.greeter.bot

See our Getting Started guide at https://help.greeter.bot

If you have any questions, feel free to reply to this email.

Thank you,
GreeterBot team
"
            );
        }

        public async Task SendRegistrationDeniedEmail(string recipient_email) {
            await SendEmail(
                "hello@greeter.bot", 
                "GreeterBot", 
                recipient_email, 
                "Your GreeterBot registration was declined", 
                @"
Hello! 

Unfortunately, your GreeterBot registration was declined. Be sure to use a company email address when registering for an account. We don't accept personal emails, e.g. Gmail, Yahoo, Hotmail, etc. See here for more information: https://help.greeter.bot

If you have any questions, feel free to reply to this email.

Thank you,
The GreeterBot team
"
            );
        }

        public async Task SendNewRegistrationAdminEmail() {
            await SendEmail(
                "hello@greeter.bot", 
                "GreeterBot", 
                "james@greeter.bot,shawn@greeter.bot", 
                "GreeterBot: New account registration", 
                @"
Hello! 

A new account has been registered in the GreeterBot Admin Portal.  It is waiting for approval.

https://app.greeter.bot/users

Thank you,
The GreeterBot team
"
            );
        }

}
