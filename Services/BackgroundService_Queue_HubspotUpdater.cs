using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using static HubspotService;

public class BackgroundService_Queue_HubspotUpdater : BackgroundService
{
    private readonly QueueService<HubspotUpdateQueueMessage> queueService;
    private readonly LogBufferService logger;
    private readonly MySettings mySettings;
    private readonly HubspotService hubspotService;


    public BackgroundService_Queue_HubspotUpdater(
        QueueService<HubspotUpdateQueueMessage> _queueService, 
        LogBufferService logger,
        IOptions<MySettings> mySettings,
        HubspotService hubspotService
    )
    {
        queueService = _queueService;
        this.logger = logger;
        this.mySettings = mySettings.Value;
        this.hubspotService = hubspotService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // while (!stoppingToken.IsCancellationRequested)
        // {
        //     var result = await queueService.GetMessageAsync();
        //     if (result != null)
        //     {
        //         QueueMessage queueMessage = result.Value.Item1;
        //         HubspotUpdateQueueMessage msg = result.Value.Item2;
        //         try
        //         {
        //             ProcessMessage(msg);
        //             await queueService.DeleteMessageAsync(queueMessage);
        //         }
        //         catch (Exception ex)
        //         {
        //             logger.Error("Exception");
        //             logger.Error(ex.ToString());
        //         }
        //     } else {
        //         // When there is no message waiting then sleep between polls
        //         await Task.Delay(mySettings.HubspotUpdateQueuePollRateMs, stoppingToken);
        //     }
        // }
    }

    private async void ProcessMessage(HubspotUpdateQueueMessage msg)
    {
        var stopwatch = Stopwatch.StartNew();
        await hubspotService.UpdateContactObj(msg);
        stopwatch.Stop();
    }
}

