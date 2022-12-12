#!/bin/bash

echo "Init localstack sns"

IFS=', ' read -r -a topics <<< "$SNS_TOPICS"
for topic in "${topics[@]}"
do
  echo "creating sns topic $topic"
  awslocal sns create-topic --name "$topic"
done
