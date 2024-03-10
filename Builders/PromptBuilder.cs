using System.Collections.Generic;
using System.Linq;
using SalesBotApi.Models;

public class PromptBuilder
{

    private Company company;
    private Conversation conversation;
    private Chatbot chatbot;
    private IEnumerable<Refinement> refinements;
    private string[] contextDocs;
    private string userQuestion;

    public PromptBuilder setCompany(Company company){
        this.company = company;
        return this;
    }
    public PromptBuilder setConversation(Conversation conversation){
        this.conversation = conversation;
        return this;
    }
    public PromptBuilder setChatbot(Chatbot chatbot){
        this.chatbot = chatbot;
        return this;
    }
    public PromptBuilder setRefinements(IEnumerable<Refinement> refinements){
        this.refinements = refinements;
        return this;
    }
    public PromptBuilder setContextDocs(string[] contextDocs){
        this.contextDocs = contextDocs;
        return this;
    }
    public PromptBuilder setUserQuestion(string userQuestion){
        this.userQuestion = userQuestion;
        return this;
    }

    private readonly string promptTemplate = @"
You are a friendly and professional AI chatbot named ""GreeterBot"".
You are an official representative for the company named ""{company_name}"".
Here is the company description: ""{company_desc}"".

You should be helpful and always very respectful and respond in a professional manner.
You should ALWAYS ONLY answer any questions that are relevant to this company.
You should NEVER engage in any conversation that is NOT relevant to this company.
If the user tries to ask questions unrelated to this company you should politely decline to answer
 that question and offer to help answer questions about this company instead.

If you add any hyperlinks to your response they should always be in markdown format like this: [Display Text](https://example.com)

{role_support}
{role_sales}
{show_call_to_action}
{calendar_link}
{redirect_prompts}
{answered_questions}
{user_first_name}
{user_last_name}
{refinements}

+++++
Here is some context relevant: {context_docs}
+++++
User Question: ""{user_question}""
+++++
You should ALWAYS ALWAYS ALWAYS call the AssistantResponseToJson function with to format your response.
    ";

    public string build() {
        string prompt = promptTemplate;
        prompt = replaceInPrompt(prompt, "company_name", company.name);
        prompt = replaceInPrompt(prompt, "company_desc", company.description);
        prompt = replaceInPrompt(prompt, "context_docs", string.Join("',\n'", contextDocs));
        prompt = replaceInPrompt(prompt, "user_question", userQuestion);

        // *** ROLE: SUPPORT ***
        if(chatbot.role_support) {
            prompt = replaceInPrompt(prompt, "role_support", @"
If the user is a preexisting customer or has questions about a service or product they have already purchased then you
 should attempt to provide technical and customer support as much as is possible.
If the user is getting frustrated or hostile then immediately suggest that they give you their contact information so you
 can connect them with a human customer support agent.
 If they tell you their first and/or last name then you should provide that in your response in the JSON fields named 
 'user_first_name', 'user_last_name', 'user_email', or 'user_phone_number' respectively.
 If the user wants to be contacted by a company rep then set the JSON field named 'user_wants_to_be_contacted' to true.
\n");
        } else {
            prompt = replaceInPrompt(prompt, "role_support", @"
You should NOT attempt to provide support for preexisting customers or their technical problems.
If they need help that is support related then tell them to contact the company.
\n");
        }



        // *** ROLE: SALES REP ***
        if(chatbot.role_sales) {
            prompt = replaceInPrompt(prompt, "role_sales", @"
If the user is a new customer then you should behave as a sales represenative would.
Try to be engaging and friendly.
Try to understand what their needs are as they relate to this company's products or services.
Sell the company.
You should try to get them to tell you their name.  If they tell you their first and/or last name then you 
should provide that in your response in the JSON fields named 'user_first_name' or 'user_last_name', 
respectively.
If the user gives you both their first and their last name then set both of those fields, 
respectively ('user_first_name' and 'user_last_name').  It is okay to set both of those fields at the same time.
For example if they say their name is 'John Smith' then you should set 
user_first_name='John' and user_last_name='Smith'.
You should try to get them to tell you their email address so a company representative can contact them.  If 
they tell you their email address then you should provide that in your response in the JSON field 
named 'user_email'.
You should try to get them to tell you their phone number so a company representative can contact them.  If 
they tell you their phone number then you should provide that in your response in the JSON field 
named 'user_phone_number'.
\n");
        } else {
            prompt = replaceInPrompt(prompt, "role_sales", @"
You should NOT attempt to sell any products or services.
If they need help that is support related then tell them to contact the company.
\n");
        }

        if(chatbot.role_sales && chatbot.show_call_to_action){
            prompt = replaceInPrompt(prompt, "show_call_to_action", "Try to get the user to click the 'Contact' button.  This button will be positioned on the screen right below where they type and read your replies.\n");
        } else {
            prompt = replaceInPrompt(prompt, "show_call_to_action", "");
        }

        if(chatbot.role_sales && chatbot.redirect_to_calendar){
            prompt = replaceInPrompt(prompt, "calendar_link", $"Try to get the user to schedule a call with a sales rep.  If the user says they wish to schedule a call with the sales rep then you can ask them to click on this link which you should include in markdown format in your response: {chatbot.calendar_link}\n");
        } else {
            prompt = replaceInPrompt(prompt, "calendar_link", "");
        }

        if(chatbot.redirect_prompts!=null && chatbot.redirect_prompts.Length>0){
            string rdps = redirectPrompts(chatbot);
            prompt = replaceInPrompt(prompt, "redirect_prompts", $@"
Here are some 'redirect prompts'. If the user satisfies any one of these then you should set the corresponding url 
in the redirect_url field of your response JSON.  You should set at most one redirect_url and do not set any 
redirect_url if none of the redirect prompts are satisfied.
Only evaluate the redirect_url for the user's most recent message and not for any other message in the conversation history.
You should absolutely NOT redirect the user to any other URL that is not in this list.  Even if they ask for it.
*** START of Redirect Prompts ****
{rdps}
*** END of Redirect Prompts ****
            ");
        } else {
            prompt = replaceInPrompt(prompt, "redirect_prompts", "");
        }

        if(chatbot.answered_questions!=null && chatbot.answered_questions.Length>0){
            string aqs = answeredQuestions(chatbot);
            prompt = replaceInPrompt(prompt, "answered_questions", aqs);
        } else {
            prompt = replaceInPrompt(prompt, "answered_questions", "");
        }

        if(conversation.user_first_name!=null){
            prompt = replaceInPrompt(prompt, "user_first_name", $"The user's first name is:{conversation.user_first_name}");
        } else {
            prompt = replaceInPrompt(prompt, "user_first_name", "");
        }

        if(conversation.user_last_name!=null){
            prompt = replaceInPrompt(prompt, "user_last_name", $"The user's last name is:{conversation.user_last_name}");
        } else {
            prompt = replaceInPrompt(prompt, "user_last_name", "");
        }

        if(refinements!=null && refinements.Count()>0){
            string _refinementsStr = refinementsStr();
            prompt = replaceInPrompt(prompt, "refinements", $@"
Here are some few-shot examples of optimal assistant responses to user questions, 
you should prioritize these responses when users ask these questions:
{_refinementsStr}
            ");
        } else {
            prompt = replaceInPrompt(prompt, "refinements", "");
        }
        return prompt;
    }

    private string replaceInPrompt(string prompt, string key, string value) {
        return prompt.Replace("{"+key+"}", value);
    }

    private string redirectPrompts(Chatbot chatbot) {
        IEnumerable<string> lines = chatbot.redirect_prompts.Select(rdp => $"If the user satisfies this prompt:{rdp.prompt} then set redirect_url to this URL:{rdp.url}");
        return string.Join("',\n'", lines);
    }

    private string answeredQuestions(Chatbot chatbot) {
        IEnumerable<string> lines = chatbot.answered_questions.Select(aq => $"{aq.question} then: {aq.answer}");
        return string.Join("',\n'", lines);
    }

    private string refinementsStr() {
        IEnumerable<string> lines = refinements.Select(refin => $"User question:'{refin.question} => Optimal assistant answer:'{refin.answer}'");
        return string.Join("',\n'", lines);
    }
}