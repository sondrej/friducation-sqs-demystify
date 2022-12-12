#!/bin/bash

alias build='docker-compose build --no-cache'
alias stop='docker-compose down'
alias start_aws='docker-compose up -d localstack'
alias start='docker-compose up apples bananas pineapples'

alias send_banana='docker exec -i localstack sh -c "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/banana --message-body banana"'
alias send_apple='docker exec -i localstack sh -c "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/apple --message-body apple"'
alias send_rotten_banana='docker exec -i localstack sh -c "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/banana --message-body \"banana fail\""'
alias send_rotten_apple='docker exec -i localstack sh -c "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/apple --message-body \"apple fail\""'

alias send_pineapple='docker exec -i localstack sh -c "awslocal s3 cp /pineapples/pineapple_good s3://pineapple/pineapple_good && awslocal sqs send-message --queue-url http://localhost:4566/000000000000/pineapple --message-body pineapple_good"'
alias send_rotten_pineapple='docker exec -i localstack sh -c "awslocal s3 cp /pineapples/pineapple_bad s3://pineapple/pineapple_bad && awslocal sqs send-message --queue-url http://localhost:4566/000000000000/pineapple --message-body pineapple_bad"'