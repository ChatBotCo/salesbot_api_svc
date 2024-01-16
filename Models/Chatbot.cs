namespace SalesBotApi.Models

{
    public class Chatbot
    {
        public string id { get; set; }
        public string company_id { get; set; }
        public bool show_avatar { get; set; }
        public string llm_model { get; set; }
        public string contact_prompt { get; set; }
        public string contact_link { get; set; }
        public string contact_method { get; set; }
        public string greeting { get; set; }
    }
}