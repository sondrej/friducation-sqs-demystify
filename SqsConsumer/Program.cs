using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

var env = Environment.GetEnvironmentVariables();
var queueUrl = (string)env["SQS_QUEUE_URL"]!;
using var sqsClient = new AmazonSQSClient(new AmazonSQSConfig
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    ServiceURL = (string)env["AWS_ENDPOINT"]!,
    AuthenticationRegion = (string)env["AWS_REGION"]!
});
var handler = CreateHandleMessage(sqsClient, queueUrl);
var receiveRequest = new ReceiveMessageRequest
{
    QueueUrl = queueUrl,
    AttributeNames = new List<string>{ "All" }, // Useful attributes on message for tracing, should be tuned for usage.
    MaxNumberOfMessages = 20, // can be tuned how many messages we want to receive, tune to fit thread-count of host
    VisibilityTimeout = 5, // 1 second visibility timout. We need to process the message within this time-frame
    WaitTimeSeconds = 20 // long polling, maximum wait time is 20 seconds. This reduces costs by reducing amount of empty responses
};

static Action<Message> CreateHandleMessage(IAmazonSQS sqsClient, string queueArn)
{
    return message =>
    {
        Console.WriteLine($"Received message: {message.MessageId}");
        if (message.Body.Contains("fail"))
            throw new HandleMessageException(message);

        Console.WriteLine($"Message Body: {message.Body}");
        // We're done processing this message, it can now be removed from the queue
        sqsClient.DeleteMessageAsync(queueArn, message.ReceiptHandle, CancellationToken.None);
    };
}

Console.WriteLine($"Subscribed to SQS Queue {queueUrl}");

// repeat ReceiveMessage call as applicable
// In the case of a lambda, you don't have to call ReceiveMessage
// If running in a fargate, you typically want a thread spinning on ReceiveMessage as we do here
while (true)
{
    try
    {
        var msg = await sqsClient.ReceiveMessageAsync(receiveRequest);

        // Any message we receive here is "invisible" on the queue with a timeout
        // If we fail to process the message (IE remove the message from the queue) within the timeout, 
        // it will be made receivable for other consumers.
        msg
            .Messages
            .AsParallel()
            .ForAll(handler);
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
}

internal sealed class HandleMessageException : Exception
{
    // Could be useful to include some properties from the message in the logs, hence this property
    public Message SqsMessage { get; }

    public HandleMessageException(Message message) : base($"Could not handle message {message.MessageId}")
    {
        SqsMessage = message;
    }
}