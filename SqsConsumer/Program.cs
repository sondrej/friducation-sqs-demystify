using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

var env = Environment.GetEnvironmentVariables();
var queueUrl = (string)env["SQS_QUEUE_URL"]!;
var handlerName = queueUrl.Split("/")[^1];
var sqsClient = new AmazonSQSClient(new AmazonSQSConfig
{
    DefaultConfigurationMode = DefaultConfigurationMode.Standard,
    ServiceURL = (string)env["AWS_ENDPOINT"]!,
    AuthenticationRegion = (string)env["AWS_REGION"]!
});

var handler = CreateHandleMessage(sqsClient, queueUrl, handlerName);
Console.WriteLine($"{handlerName}::Subscribed to SQS Topic");

while (true)
{
    try
    {
        var msg = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 60
        });

        // Any message we receive here is "invisible" on the queue with a timeout
        // If we fail to process the message (IE remove the message from the queue) within the timeout, 
        // it will be made receivable for other consumers.
        foreach (var message in msg.Messages)
        {
            await handler(message);
        }
    }
    catch (HandleMessageException ex)
    {
        // No need to crash the consumer process here, but assume any other exception is one we cannot recover from.
        // In a fargate instance, we'd let AWS restart the instance when we get any other exception.
        // In a lambda we would just let this error propagate as well, and always delete a message when we're done with it.
        Console.Error.WriteLine($"{handlerName}::{ex.Message}");
    }
}

static Func<Message, Task> CreateHandleMessage(IAmazonSQS sqsClient, string queueArn, string handlerName)
{
    return async message =>
    {
        Console.WriteLine($"{handlerName}::Received message: {message.Body}");
        if (message.Body.Contains("fail"))
            throw new HandleMessageException(message);
        // We're done processing this message, it can now be removed from the queue
        await sqsClient.DeleteMessageAsync(queueArn, message.ReceiptHandle, CancellationToken.None);
    };
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