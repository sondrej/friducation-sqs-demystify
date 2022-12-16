using System.Runtime.InteropServices;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;

var tokenSource = new CancellationTokenSource();
PosixSignalRegistration.Create(PosixSignal.SIGINT, _ =>
{
    Console.WriteLine("Interrupted");
    tokenSource.Cancel();
});
PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ =>
{
    Console.WriteLine("Terminated");
    tokenSource.Cancel();
});

var sqsClient = new AmazonSQSClient(new AmazonSQSConfig
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    // these should only be set when targeting localstack
    ServiceURL = Environment.GetEnvironmentVariable("AWS_ENDPOINT")!,
    AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION")!
});
var snsClient = new AmazonSimpleNotificationServiceClient(new AmazonSimpleNotificationServiceConfig
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    // these should only be set when targeting localstack
    ServiceURL = Environment.GetEnvironmentVariable("AWS_ENDPOINT")!,
    AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION")!
});

var queueUrl = Environment.GetEnvironmentVariable("SQS_QUEUE_URL")!;
var topicArns = Environment.GetEnvironmentVariable("SNS_TOPIC_ARNS")!.Split(",");

await snsClient.SubscribeQueueToTopicsAsync(topicArns, sqsClient, queueUrl);

var handlerType = queueUrl.Split("/")[^1];
var sqsReceiveRequest = new ReceiveMessageRequest
{
    QueueUrl = queueUrl,
    // Useful attributes on message for tracing, should be tuned for usage.
    AttributeNames = new List<string>{ "All" },
    // max 10, tune for use-case and thread count
    MaxNumberOfMessages = 10,
    // 1 second visibility timout. We need to process the message within this time-frame
    VisibilityTimeout = 5,
    // long polling, maximum wait time is 20 seconds. This reduces costs by reducing amount of empty responses
    // A default for this can be configured when creating the queue as well
    WaitTimeSeconds = 20
};

Console.WriteLine($"Started {handlerType} consumer");

Task HandleMessage(Message message)
{
    var body = message.Body;
    var eventNotification = JsonSerializer.Deserialize<SqsEventNotification>(body);
    if (eventNotification == null)
    {
        throw new Exception($"Unable to deserialize sqs event notification with body {body}");
    }

    var snsEvent = JsonSerializer.Deserialize<SnsEvent>(eventNotification.Message);
    if (snsEvent == null)
    {
        throw new Exception($"Unable to deserialize message with body {eventNotification.Message}");
    }
    Console.WriteLine($"Received {handlerType} event of type {snsEvent.Type} with body: {snsEvent.Body}. Original message id: {snsEvent.MessageId}");
    return Task.CompletedTask;
}

// repeat ReceiveMessage call as applicable
// In the case of a lambda, you don't have to call ReceiveMessage
// If running in a fargate, you typically want a thread spinning on ReceiveMessage as we do here
while (!tokenSource.IsCancellationRequested)
{
    var msg = await sqsClient.ReceiveMessageAsync(sqsReceiveRequest, tokenSource.Token);
    // Any message we receive here is "invisible" on the queue with a timeout
    // If we fail to process the message (IE remove the message from the queue) within the timeout, 
    // it will be made receivable for other consumers.

    await Task.WhenAll(msg.Messages.Select(async message =>
    {
        //Console.WriteLine($"Received message with id: {message.MessageId}");
        try
        {
            await HandleMessage(message);
            // We're done processing this message, it can now be removed from the queue
            await sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle);
            //Console.WriteLine($"Processed message with id: {message.MessageId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            // In some cases we might want to let the error bubble up and crash the service, then letting aws restart the service for us.
            // This is because some exceptions are such that we cannot recover without a restart.
        }
    }));
}

[Serializable]
internal record SqsEventNotification(
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string Type, 
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string MessageId, 
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string TopicArn, 
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string Message, 
    // ReSharper disable once NotAccessedPositionalProperty.Global
    DateTime Timestamp
);

[Serializable]
internal record SnsEvent(
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string Type,
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string Body,
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string MessageId
);