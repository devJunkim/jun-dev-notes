---
title: "Amazon ECS Task Roles: Least-Privilege Access Without Static AWS Keys"
excerpt: "Separate ECS task and execution roles, scope application permissions to real operations, and diagnose credential and authorization failures without embedding AWS keys."
category: "Cloud"

seo:
  focusKeyword: "Amazon ECS task roles"
  description: "Design Amazon ECS task roles with least-privilege policies, separate execution permissions, temporary credentials, and safe access-denied diagnostics."
  socialTitle: "Amazon ECS Task Roles and Least Privilege"
  socialDescription: "Give application containers the AWS permissions they need while keeping deployment, platform, and runtime identities distinct."
---

# Amazon ECS Task Roles: Least-Privilege Access Without Static AWS Keys

An ECS task can pull its image successfully and still receive `AccessDenied` when application code reads an S3 object. Giving more permissions to the role that pulled the image may change nothing because it is not the identity making the application request.

Reliable access starts by separating platform startup from application behavior, then granting each identity only the operations it owns.

> **Quick answer:** Give the application a task role, give ECS startup operations a separate execution role, and let a supported AWS SDK obtain temporary credentials from the task environment. Scope permissions to the workload's actions and resources, and diagnose the actual calling identity before broadening a policy.

## Identify Who Makes Each Request

ECS has several identities around a deployment. They are not interchangeable:

| Identity | Typical responsibility |
| --- | --- |
| Deployment principal | Register a task definition and deploy a service; pass approved roles |
| Task execution role | Platform actions such as pulling an ECR image and sending configured logs |
| Task role | AWS API calls made by application code inside the task |
| EC2 container instance role, when applicable | Host and ECS agent responsibilities |

AWS documents the [task role](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/task-iam-roles.html) and [execution role](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/task_execution_IAM_role.html) separately. Application permissions placed only on the execution role do not become the application's credentials.

For example, an image-processing worker may need `s3:GetObject` for validated source files. That permission belongs on its task role. Permissions used by ECS to retrieve a configured startup secret belong on the execution role; code that calls Secrets Manager directly needs the corresponding permission on the task role.

[AWS Lambda vs ECS for .NET](https://dev.jun-kim.net/2026/09/13/aws-lambda-vs-ecs-choosing-the-right-compute-option-for-net-applications/) discusses compute selection. Here the focus is the identity used after ECS has been selected.

## Configure the Roles Independently

A task definition references the two roles through separate fields. This is only a fragment, not a deployable task definition:

```json
{
  "taskRoleArn": "arn:aws:iam::111122223333:role/example-image-reader",
  "executionRoleArn": "arn:aws:iam::111122223333:role/example-ecs-execution"
}
```

All account numbers, role names, bucket names, and Regions in this article are illustrative. Replace them through reviewed infrastructure configuration, not by copying credentials into the task definition.

A role's trust policy answers who may assume it. Its permissions policy answers what the resulting identity may do. A task-role trust policy can constrain the ECS service using the source account and source ARN:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": { "Service": "ecs-tasks.amazonaws.com" },
      "Action": "sts:AssumeRole",
      "Condition": {
        "StringEquals": { "aws:SourceAccount": "111122223333" },
        "ArnLike": {
          "aws:SourceArn": "arn:aws:ecs:us-east-1:111122223333:*"
        }
      }
    }
  ]
}
```

The wildcard is deliberate: AWS's task-role guidance does not support narrowing this source ARN condition to a specific cluster. Workload separation also depends on controlling who can deploy task definitions and pass each role. A narrow trust policy is not a substitute for deployment authorization.

Review `iam:PassRole` permissions on the deployment identity. A principal that can run arbitrary code with a powerful task role can use that role's application capabilities even without reading a static secret.

## Grant the Operations the Code Actually Performs

If the worker receives an exact S3 key and only reads current object contents, start with a policy such as:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "s3:GetObject",
      "Resource": "arn:aws:s3:::example-validated-assets/ready/*"
    }
  ]
}
```

This grants neither listing nor upload nor deletion. S3 bucket-level and object-level actions use different resource shapes; AWS's [S3 permissions examples](https://docs.aws.amazon.com/IAM/latest/UserGuide/reference_policies_examples_s3_rw-bucket.html) illustrate the distinction.

Add `s3:ListBucket` only if discovery is part of the workload, with a bucket ARN and appropriate prefix conditions. Version-specific reads need their own permission analysis. Objects encrypted with a customer-managed KMS key also require the relevant KMS authorization and key-policy configuration.

The policy is not proof of effective access. A bucket policy, permissions boundary, organization policy, or other applicable control can restrict the request. Cross-account access requires reviewing both sides. AWS's [policy evaluation rules](https://docs.aws.amazon.com/IAM/latest/UserGuide/reference_policies_evaluation-logic.html) explain why an explicit deny is not repaired by attaching another allow.

Use separate roles for services with different responsibilities. An upload issuer and a validated-file reader should not share a broad storage role merely because they use the same bucket. [Secure Direct Uploads to S3](https://dev.jun-kim.net/2026/09/17/secure-direct-uploads-to-amazon-s3-with-presigned-urls/) covers the separate upload capability and quarantine workflow.

## Let the SDK Obtain Temporary Credentials

The ECS container credential provider lets supported SDKs obtain the task's temporary role credentials. The application's deployment supplies the role association; ordinary application code should not need to read an access key and secret from its own configuration. See AWS's [container credential provider documentation](https://docs.aws.amazon.com/sdkref/latest/guide/feature-container-credentials.html).

Do not copy a resolved credential set into a long-lived custom object or log it for diagnostics. Keep the SDK's credential provider behavior intact so expiring credentials can be refreshed. A task role normally does not need to assume itself again.

Check the credential resolution chain for the exact SDK version. Explicitly supplied credentials or other configured credential sources can cause code to use an unintended identity. The [AWS SDK for .NET V4 resolution guide](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html) documents its search order. Remove obsolete static-key configuration when adopting task roles rather than assuming the role must win.

Local development uses a separate identity setup, such as an approved short-lived developer session. An application working locally proves little about the permissions attached to its deployed task.

## Distinguish IAM Credentials from Application Secrets

A task role replaces static AWS access keys. It does not eliminate a third-party API token or database credential that the workload still requires.

Choose whether ECS injects such a secret during startup or application code retrieves it at runtime. Startup injection and runtime retrieval use different identities and have different rotation behavior. An injected environment value is not automatically refreshed when the stored secret rotates; AWS describes the redeployment implications in its [ECS sensitive-data guidance](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/specifying-sensitive-data.html).

Runtime retrieval needs a caching and refresh policy appropriate to the consumer. Never turn a secret-fetch failure into a log line containing the previous secret or a full environment dump.

## Diagnose Failures Without Expanding to Administrator Access

First distinguish a task that cannot start from application code receiving an AWS error after startup. Then identify the failing service operation, resource, Region, deployed task definition revision, and calling role. Use safe request identifiers and the relevant audit events; do not print credential values.

An `AccessDenied` response and a network timeout are different failure categories. Check permission evaluation for the former and routing, DNS, endpoints, and security controls for the latter. Repeated retries do not fix either a permanent policy denial or an incorrect role association.

Test both allowed and forbidden operations with the actual deployed role in an isolated environment. Confirm that reading the approved prefix works and reading outside it fails. Policy validation alone cannot prove the workload uses the identity you intended.

Finally, a task role does not isolate containers inside the same task from one another. On EC2-backed ECS, also address access to host metadata and neighboring workloads according to the platform's isolation guidance. Least privilege limits what a compromised workload can do; it does not remove the need to protect its execution environment.
