#!/bin/bash

alias build='docker-compose build --no-cache'
alias stop='docker-compose down'
alias start_aws='docker-compose up -d localstack'
alias start_consumers='docker-compose up apples bananas'

alias send_banana='docker exec -i localstack sh -c "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/bananas --message-body banana"'
alias send_apple='docker exec -i localstack sh -c "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/apples --message-body apple"'
alias send_rotten_banana='docker exec -i localstack sh -c "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/bananas --message-body \"banana fail\""'
alias send_rotten_apple='docker exec -i localstack sh -c "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/apples --message-body \"apple fail\""'
