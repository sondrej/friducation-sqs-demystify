#!/bin/bash

echo "Init localstack sqs"

IFS=', ' read -r -a queues <<< "$SQS_QUEUES"
for queue in "${queues[@]}"
do
  echo "creating sns queue ${queue}"
  awslocal sqs create-queue --queue-name "${queue}-dlq"
  # some carefully crafted json escaped string here. the RedrivePolicy must be a valid json STRING (not object)
  awslocal sqs create-queue --queue-name "${queue}" --attributes '{"RedrivePolicy":"{\"deadLetterTargetArn\":\"arn:aws:sqs:eu-west-1:00000000:'"$queue"'-dlq\",\"maxReceiveCount\":\"5\"}", "ReceiveMessageWaitTimeSeconds":"20" }'
done