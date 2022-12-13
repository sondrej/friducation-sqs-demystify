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
}

if [[ "$1" == "start" ]]; then
  [[ "$2" == "aws" ]] && docker-compose up -d localstack && exit $?
  [[ "$2" == "consumers" ]] && docker-compose up apples bananas pineapples && exit $?
  
  echo "Expected one of aws or consumers to start" && usage && exit 1
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

function localstack_exec() {
  docker exec -i localstack sh -c "$*"
}

if [ -n "$s3Content" ]; then
  path="/pineapples/$body"
  localstack_exec "echo $s3Content > $path"
  localstack_exec "awslocal s3 cp $path s3://$type/$body"
  localstack_exec "rm $path"
fi

localstack_exec "awslocal sqs send-message --queue-url http://localhost:4566/000000000000/$type --message-body \"$body\""
