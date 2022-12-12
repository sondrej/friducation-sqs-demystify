#!/bin/bash

echo "Init localstack s3"

IFS=', ' read -r -a buckets <<< "$BUCKET_NAMES"
for bucket in "${buckets[@]}"
do
  echo "creating sns bucket $bucket"
  awslocal s3 mb "s3://$bucket"
done
