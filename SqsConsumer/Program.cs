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
    var msg = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
    {
        QueueUrl = queueUrl,
        MaxNumberOfMessages = 1,
        WaitTimeSeconds = 60
    });

    foreach (var message in msg.Messages)
    {
        try
        {
            await handler(message);
        }
        catch (HandleMessageException ex)
        {
            Console.Error.WriteLine($"{handlerName}::{ex.Message}");
        }
    }
}

static Func<Message, Task> CreateHandleMessage(IAmazonSQS sqsClient, string queueArn, string handlerName) => async message =>
{
    Console.WriteLine($"{handlerName}::Received message: {message.Body}");
    if (message.Body.Contains("fail"))
        throw new HandleMessageException(message);
    await sqsClient.DeleteMessageAsync(queueArn, message.ReceiptHandle, CancellationToken.None);
};

internal sealed class HandleMessageException : Exception
{
    public Message SqsMessage { get; }

    public HandleMessageException(Message message) : base($"Could not handle message {message.MessageId}")
    {
        SqsMessage = message;
    }
}