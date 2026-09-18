---
title: "Secure Direct Uploads to Amazon S3 with Presigned URLs"
excerpt: "Design direct-to-S3 uploads with presigned URLs while constraining object keys, expiration, content, authorization, and post-upload processing."
category: "Cloud"

seo:
  focusKeyword: "secure S3 presigned URL uploads"
  description: "Secure Amazon S3 presigned URL uploads with short lifetimes, server-owned keys, checksums, IAM boundaries, validation, and safe processing."
  socialTitle: "Secure S3 Presigned URL Uploads"
  socialDescription: "Move file bytes off your API without turning a presigned upload into an unrestricted storage capability."
---

# Secure Direct Uploads to Amazon S3 with Presigned URLs

Sending every user upload through an application server consumes its bandwidth, memory, and connection time. A presigned Amazon S3 URL lets an authorized client upload directly to a specific S3 operation without receiving AWS credentials.

The URL is still a capability. Anyone who obtains it can use it within its constraints and lifetime.

AWS documents the credential and expiration rules in its [S3 presigned URL guide](https://docs.aws.amazon.com/AmazonS3/latest/userguide/using-presigned-url.html). The signer delegates only an operation it is already permitted to perform.

> **Quick answer:** Authenticate and authorize the intent in your API, generate an unpredictable server-owned object key, issue a short-lived URL from a narrowly scoped role, and treat the resulting object as untrusted until post-upload validation completes.

## Separate Authorization from Byte Transfer

A typical flow is:

```text
Client -> API: request permission to upload metadata
API -> Client: upload ID, object key, presigned URL, required headers
Client -> S3: upload bytes directly
S3 -> processing workflow: validate and scan object
Client -> API: observe upload/processing status
```

The API still decides who may upload, which tenant owns the file, acceptable size and type, and what business record the upload belongs to. S3 handles the byte transfer. A presigned URL does not replace application authorization.

This is a PaaS-style responsibility trade: less application data-plane work, but more explicit object lifecycle and IAM design. [IaaS vs PaaS vs SaaS: A Developer's Guide](https://dev.jun-kim.net/2026/09/10/iaas-vs-paas-vs-saas-a-developers-guide/) discusses why managed infrastructure moves responsibilities rather than eliminating them.

## Let the Server Choose the Object Key

Do not use a raw browser filename as the S3 key. Filenames collide, leak user-controlled text into logs and URLs, and make tenant-boundary mistakes easier.

Generate an opaque upload ID and build a key from trusted server values:

```text
incoming/{tenant-id}/{upload-id}
```

Store the original display name as validated application metadata. Keep the bucket private and block public access unless public objects are a deliberate, separately reviewed requirement.

If a key already exists, a presigned `PUT` can replace that object. Unpredictable, single-purpose keys reduce accidental overwrites. S3 versioning can improve recovery, but it does not make overwriting authorized or free.

## Sign the Smallest Useful Capability

The signing identity must have permission for the underlying operation. Give it access only to the intended bucket and prefix, and avoid using broad administrative credentials.

Generate a URL for one method, bucket, key, and short expiration. Its effective lifetime cannot exceed the lifetime of the credentials that signed it. Revoking or expiring those credentials can cause the URL to stop working earlier than its requested expiration.

Do not log the query string. The signature is part of the URL, so normal request logs, analytics tools, support screenshots, browser history, and copied chat messages can leak the capability.

A presigned URL can generally be reused until it expires. If the business requires one logical submission, track the upload ID in application state and make finalization idempotent. Do not describe the URL itself as single-use.

## Bind and Validate What Matters

If the signer includes a content type or checksum in the signed request, the client must send the matching header. A checksum lets S3 verify integrity in transit; with Signature Version 4, S3 supports algorithms beyond MD5.

Content type is client-supplied metadata, not proof of file format. Validate magic bytes or parse the file in a quarantined processing step. Malware scanning, image decoding limits, archive expansion limits, and document sanitization depend on the accepted formats and threat model.

For strict upload-size enforcement before accepting bytes, consider presigned POST policies with a `content-length-range` condition. A `PUT` URL alone should not be treated as a complete size-policy mechanism. Also set bucket or access-point policies that constrain signature age and network origin where appropriate.

CORS is a browser permission layer, not authentication. Configure only the application origins, methods, and headers required by the upload client; non-browser clients are not constrained by browser CORS enforcement.

## Treat Upload Completion as a State Machine

Receiving a successful client response does not mean the application should immediately expose the object. Model states such as `Pending`, `Uploaded`, `Scanning`, `Ready`, `Rejected`, and `Expired`.

S3 event notifications can start processing, but delivery can be duplicated and event order is not a general business ordering guarantee. Make the handler idempotent using the upload ID and current state. [Reliable Background Processing on AWS with SQS and .NET](https://dev.jun-kim.net/2026/09/15/reliable-background-processing-on-aws-with-sqs-and-net/) covers the duplicate-delivery and dead-letter concerns when SQS buffers that work.

Before marking an upload ready, verify that:

- the object key belongs to the expected upload record and tenant;
- the observed size is within policy;
- required checksum or integrity checks succeeded;
- detected content matches an allowed format;
- scanning and transformation completed;
- metadata that will later control downloads is safe.

Move or copy validated objects to a separate trusted prefix or bucket when that makes permissions easier to reason about. Ensure users cannot read from the quarantine location.

## Multipart Uploads Need Their Own Lifecycle

Large files may need S3 multipart upload. The API must authorize initiation, sign individual part operations, and authorize completion. Persist the upload ID and expected ownership; never accept an arbitrary multipart upload ID from a client without checking it.

Abandoned multipart uploads consume storage. Configure lifecycle rules to abort incomplete uploads after an appropriate period, and monitor completion failures. Checksums and ETags for multipart objects have different semantics from a simple single-part MD5; use S3's documented checksum features instead of assuming the ETag is always a content hash.

## A Secure Design Still Needs Operations

Track requested, completed, rejected, and expired uploads; bytes by tenant; scan latency; and incomplete multipart storage. Apply quotas before issuing URLs, not only after storage costs have accumulated.

Use lifecycle rules for abandoned quarantine objects and retention policies for accepted objects. Test expired URLs, wrong headers, duplicate uploads, overwritten keys, cancelled browser requests, event redelivery, and a scanner outage.

Direct upload reduces application-server load, but it adds a small distributed workflow. The design is successful when authorization, object state, validation, and cleanup remain understandable even when the client disconnects halfway through.
