# Security Policy

This is a client-side Unity library with no network, storage, or authentication surface of its own. The realistic risk area is a malformed asset (a model, animation clip, or texture) that crashes the Unity importer or the runtime baker.

## Reporting a vulnerability

Please report anything you believe is exploitable privately, not as a public issue:

- Open a private advisory via the repository's **Security** tab (**Report a vulnerability**), or
- Reach out to [@sinanata](https://x.com/sinanata).

I will acknowledge the report and, if it is valid, fix it on `main` and credit you unless you prefer otherwise.

## Supported versions

The latest release on `main` is the only supported version. Fixes land there first.
