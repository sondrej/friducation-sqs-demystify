#!/bin/bash
set -euo pipefail

function usage() {
    echo "Usage"
    echo -e "build: force rebuild of containers"
    echo -e "start: start services"
    echo -e "  aws: start aws services"
    echo -e "  consumers: start consumer services"
    echo -e "stop: stop services"
    echo -e "send: send a message"
    echo -e "  -h\t display help"
    echo -e "  -t\t type: one of apple, banana or pineapple"
    echo -e "  -b\t body: message body"
    echo -e "dlq: get all messages in dead letter queues"
    echo -e "  purge: get and purge dead letter queues"
}

function localstack_exec() {
  docker exec -i localstack sh -c "$*"
  return $?
}

if [[ "$1" == "start" ]]; then
  [[ "$2" == "aws" ]] && docker-compose up localstack && exit $?
  [[ "$2" == "consumers" ]] && docker-compose up apples bananas pineapples fruitcake && exit $?
  
  echo "Expected one of aws or consumers to start" && usage && exit 1
fi
if [[ "$1" == "dlq" ]]; then
  types=( apple banana pineapple )
  for type in "${types[@]}"; do
    echo "Receiving up to 10 messages in $type-dlq"
    localstack_exec "awslocal sqs receive-message --queue-url http://localhost:4566/000000000000/$type-dlq --max-number-of-messages 10 --wait-time-seconds 1"
  done
  if [[ "${2-}" == "purge" ]]; then
    for type in "${types[@]}"; do
      echo "Purging all messages in queue $type-dlq"
      localstack_exec "awslocal sqs purge-queue --queue-url http://localhost:4566/000000000000/$type-dlq"
    done
  fi
  exit 0
fi
[[ "$1" == "build" ]] && docker-compose build --no-cache && exit $?
[[ "$1" == "stop" ]] && docker-compose down && exit $?
[[ "$1" != "send" ]] && echo "Unknown command $1" && usage && exit 1

type=""
body=""
s3Content=""

shift

while getopts "ht:b:c:" opt; do
  case $opt in
    h) usage
      exit 0
      ;;
    b)
      body="$OPTARG"
      ;;
    t)
      if [[ "$OPTARG" =~ ^(apple|banana|pineapple)$ ]]; then
        type="$OPTARG"
      else
        echo "Incorrect type $OPTARG, expected one of apple, banana or pineapple"
        usage
        exit 1
      fi
      ;;
    c)
      s3Content="$OPTARG"
      ;;
    *)
      usage
      exit 1
      ;;
  esac
done

[ -z "$type" ] && echo "-t apple|banana|pineapple arg is required" && usage && exit 1
[ -z "$body" ] && echo "-b body arg is required" && usage && exit 1

if [ -n "$s3Content" ]; then
  key="$body"
  path="/s3items/$key"
  localstack_exec "[ ! -d /s3items ] && mkdir /s3items || exit 0"
  localstack_exec "echo $s3Content > $path"
  localstack_exec "awslocal s3 cp $path s3://$type/$key"
  #localstack_exec "rm $path"
fi

localstack_exec "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/$type --message-body \"$body\""
