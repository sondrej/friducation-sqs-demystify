using System.Runtime.InteropServices;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
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
using var snsClient = new AmazonSimpleNotificationServiceClient(new AmazonSimpleNotificationServiceConfig
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    // these should only be set when targeting localstack
    ServiceURL = Environment.GetEnvironmentVariable("AWS_ENDPOINT")!,
    AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION")!
});

var queueUrl = Environment.GetEnvironmentVariable("SQS_QUEUE_URL")!;
using var sqsClient = new AmazonSQSClient(new AmazonSQSConfig
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    // these should only be set when targeting localstack
    ServiceURL = Environment.GetEnvironmentVariable("AWS_ENDPOINT")!,
    AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION")!
});

var bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME");
using var s3Client = new AmazonS3Client(new AmazonS3Config
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    // these should only be set when targeting localstack
    ServiceURL = Environment.GetEnvironmentVariable("AWS_ENDPOINT")!,
    AuthenticationRegion = Environment.GetEnvironmentVariable("AWS_REGION")!,
    ForcePathStyle = true
});

var handlerType = queueUrl.Split("/")[^1];
var handler = CreateHandleMessage(sqsClient, snsClient, s3Client, queueUrl, snsTopicArn, bucketName, handlerType);
var sqsReceiveRequest = new ReceiveMessageRequest
{
    QueueUrl = queueUrl,
    // Useful attributes on message for tracing, should be tuned for usage.
    AttributeNames = new List<string>{ "All" },
    // can be tuned how many messages we want to receive, tune to fit thread-count of host
    MaxNumberOfMessages = 20, 
    // 1 second visibility timout. We need to process the message within this time-frame
    VisibilityTimeout = 5,
    // long polling, maximum wait time is 20 seconds. This reduces costs by reducing amount of empty responses
    // A default for this can be configured when creating the queue as well
    WaitTimeSeconds = 20
};

static Func<Message, CancellationToken, Task> CreateHandleMessage(
    IAmazonSQS sqsClient,
    IAmazonSimpleNotificationService snsClient,
    IAmazonS3 s3Client,
    string queueUrl,
    string topicArn,
    string? bucketName,
    string handlerType
    )
{
    return async (message, token) =>
    {
        Console.WriteLine($"Received message with id: {message.MessageId}");
        if (message.Body.Contains("fail"))
            throw new HandleMessageException(message);

        // Do stuff with message here, eg store in db etc.
        if (bucketName != null)
        {
            var bucketKey = message.Body;
            try
            {
                var bucketItem = await s3Client.GetObjectAsync(bucketName, bucketKey, token);
                using var reader = new StreamReader(bucketItem.ResponseStream);
                var content = await reader.ReadToEndAsync(token);
                if (content.Contains("fail"))
                {
                    throw new HandleMessageException(message);
                }
                Console.WriteLine($"Processed message with S3 content: {content}");

                token.ThrowIfCancellationRequested();
                await s3Client.DeleteObjectAsync(bucketName, bucketKey);
            }
            catch (Exception ex)
            {
                throw new HandleMessageException(message, $"Error reading s3 item with key {bucketKey}. Exception message {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"Processed message with Body: {message.Body}");
        }

        token.ThrowIfCancellationRequested();
        // We're done processing this message, it can now be removed from the queue
        await sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, token);
        await snsClient.PublishAsync(topicArn, JsonSerializer.Serialize(new SnsEvent($"{handlerType}-updated", message.Body)), token);
    };
}

Console.WriteLine($"Started {handlerType} consumer");

// repeat ReceiveMessage call as applicable
// In the case of a lambda, you don't have to call ReceiveMessage
// If running in a fargate, you typically want a thread spinning on ReceiveMessage as we do here
while (!tokenSource.IsCancellationRequested)
{
    try
    {
        var msg = await sqsClient.ReceiveMessageAsync(sqsReceiveRequest, tokenSource.Token);
        // Any message we receive here is "invisible" on the queue with a timeout
        // If we fail to process the message (IE remove the message from the queue) within the timeout, 
        // it will be made receivable for other consumers.
        msg
            .Messages
            .AsParallel()
            .ForAll(message => handler(message, tokenSource.Token));
    }
    // No need to crash the consumer process here, but assume any other exception is one we cannot recover from.
    // In a fargate instance, we'd let AWS restart the instance when we get any other exception.
    // In a lambda we would just let this error propagate as well, and always delete a message when we're done with it.
    catch (AggregateException ex) when (ex.InnerException is HandleMessageException)
    {
        foreach (var innerException in ex.InnerExceptions)
        {
            Console.Error.WriteLine(innerException.Message);
            //Console.Error.WriteLine(JsonSerializer.Serialize((innerException as HandleMessageException)!.SqsMessage.MessageAttributes));
        }
    }
    catch (HandleMessageException ex)
    {
        Console.Error.WriteLine(ex.Message);
        //Console.Error.WriteLine(JsonSerializer.Serialize(ex.SqsMessage.MessageAttributes));
    }
    catch (Exception)
    {
        tokenSource.Cancel();
        throw;
    }
}

internal sealed class HandleMessageException : Exception
{
    // Could be useful to include some properties from the message in the logs, hence this property
    public Message SqsMessage { get; }

    public HandleMessageException(Message message) : base($"Could not handle message {message.MessageId}")
    {
        SqsMessage = message;
    }

    public HandleMessageException(Message message, string exceptionMessage) : base(exceptionMessage)
    {
        SqsMessage = message;
    }
}

[Serializable]
internal record SnsEvent(
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string Type,
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string Body
);