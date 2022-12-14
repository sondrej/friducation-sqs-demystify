using System.Runtime.InteropServices;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
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

var snsTopicArn = Environment.GetEnvironmentVariable("SNS_TOPIC_ARN")!;
var snsClient = new AmazonSimpleNotificationServiceClient(new AmazonSimpleNotificationServiceConfig
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    // these should only be set when targeting localstack
    ServiceURL = Environment.GetEnvironmentVariable("AWS_ENDPOINT")!,
    AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION")!
});

var queueUrl = Environment.GetEnvironmentVariable("SQS_QUEUE_URL")!;
var sqsClient = new AmazonSQSClient(new AmazonSQSConfig
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    // these should only be set when targeting localstack
    ServiceURL = Environment.GetEnvironmentVariable("AWS_ENDPOINT")!,
    AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION")!
});

var bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME");
var s3Client = new AmazonS3Client(new AmazonS3Config
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    // these should only be set when targeting localstack
    ServiceURL = Environment.GetEnvironmentVariable("AWS_ENDPOINT")!,
    AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION")!,
    ForcePathStyle = true
});

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

async Task HandleMessage(Message message)
{
    if (message.Body.Contains("fail"))
        throw new Exception($"Could not handle message {message.MessageId}");

    // Do stuff with message here, eg store in db etc.
    if (bucketName != null)
    {
        var bucketKey = message.Body;
        GetObjectResponse response;
        try
        { 
            response = await s3Client.GetObjectAsync(bucketName, bucketKey);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error reading S3 file, exception message {ex.Message}");
        }
        using var reader = new StreamReader(response.ResponseStream);
        var content = await reader.ReadToEndAsync();
        if (content.Contains("fail"))
        {
            throw new Exception($"Could not read s3 content. messageId {message.MessageId}");
        }
        Console.WriteLine($"Processed message with S3 content: {content}");

        await s3Client.DeleteObjectAsync(bucketName, bucketKey);
    }
    else
    {
        Console.WriteLine($"Message Body: {message.Body}");
    }

    await snsClient.PublishAsync(snsTopicArn, JsonSerializer.Serialize(new SnsEvent($"{handlerType}-updated", message.Body)));
}

Console.WriteLine($"Started {handlerType} consumer");

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
        Console.WriteLine($"Received message with id: {message.MessageId}");
        try
        {
            await HandleMessage(message);
            // We're done processing this message, it can now be removed from the queue
            await sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle);
            Console.WriteLine($"Processed message with id: {message.MessageId}");
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
internal record SnsEvent(
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string Type,
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string Body
);