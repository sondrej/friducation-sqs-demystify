#!/bin/bash

echo "Init localstack sqs"

IFS=', ' read -r -a queues <<< "$SQS_QUEUES"
for queue in "${queues[@]}"
do
  echo "creating sns queue $queue"
  awslocal sqs create-queue --queue-name "$queue"
done
