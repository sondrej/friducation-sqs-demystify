# Friducation 

This repository contains some basic example material for a friducation session.

The `scripts.sh` file contains some operations you can do. Run `./scripts.sh -h` to see available commands

## The goal

- Demonstrate aws cli api of sqs, sns and s3 (localstack is 1=1 with aws)
- Demonstrate aws c# sdk for sqs, sns and s3
- Demonstrate simple error handling and happy-paths
- Demonstrate usage of s3 and sqs in combination where messages are to large to fit in an sqs message

## Out of scope (maybe topic for a future Friducation)

- Idempotency
- FIFO

### The (fruit) Consumer

Is very much not production-ready, nor is it clean-code. 
It only aims to show with the least amount of ceremony how to use AWS SQS, SNS and S3, and using localstack to run these locally.
There is probably a bunch of bugs and/or gotchas in this example.
