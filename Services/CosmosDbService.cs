using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

public class CosmosDbService
{
    public Container MessagesContainer { get; }
    public Container ConversationsContainer { get; }
    public Container CompaniesContainer { get; }
    public Container ChatbotsContainer { get; }
    public Container UsersContainer { get; }
    public Container LinksContainer { get; }
    public Container RefinementsContainer { get; }
    public Container LogsContainer { get; }
    public Container MetricsContainer { get; }

    public CosmosDbService(
        IOptions<MySettings> _mySettings,
        IOptions<MyConnectionStrings> _myConnectionStrings
    )
    {
        MySettings mySettings = _mySettings.Value;
        MyConnectionStrings myConnectionStrings = _myConnectionStrings.Value;
        var client = new CosmosClient(
            mySettings.CosmosUri,
            myConnectionStrings.CosmosPrimaryAuthKey,
            new CosmosClientOptions
            {
                ApplicationRegion = Regions.WestUS,
            });

        var database = client.GetDatabase("keli");
        MessagesContainer = database.GetContainer(mySettings.TableMessages);
        ConversationsContainer = database.GetContainer(mySettings.TableConversations);
        CompaniesContainer = database.GetContainer(mySettings.TableCompanies);
        ChatbotsContainer = database.GetContainer(mySettings.TableChatbots);
        UsersContainer = database.GetContainer(mySettings.TableUsers);
        LinksContainer = database.GetContainer(mySettings.TableLinks);
        RefinementsContainer = database.GetContainer(mySettings.TableRefinements);
        LogsContainer = database.GetContainer(mySettings.TableLogs);
        MetricsContainer = database.GetContainer(mySettings.TableMetrics);
    }
}
