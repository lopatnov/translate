# Security Policy

## Supported Versions

Only the latest version of Lopatnov.Translate (on the `main` branch) receives security updates.

| Version | Supported |
| ------- | --------- |
| latest  | ✅        |
| older   | ❌        |

## Reporting a Security Issue

Please **do not** open a public GitHub issue for security-sensitive findings.

Instead, contact the maintainer directly via
**[LinkedIn](https://www.linkedin.com/in/lopatnov/)** with:

1. A description of the issue and its potential impact
2. Steps to reproduce (or a proof-of-concept, if applicable)
3. Any suggested mitigation or fix

You can expect an initial response within **72 hours**.

## Disclosure Policy

- The maintainer will confirm receipt and investigate the report
- A fix will be prepared and released as soon as reasonably possible
- Credit will be given in the release notes (unless you prefer to remain anonymous)
- Public disclosure will be coordinated with the reporter

## Scope

This policy covers the Lopatnov.Translate application code in this repository.
Third-party dependencies should be reported to their respective maintainers.

## Best Practices for Self-Hosters

- Run behind HTTPS (terminate TLS at nginx or a reverse proxy)
- Keep the Docker images and host OS up to date
- Do not expose the gRPC port (5100) directly to the public internet — place it behind an authenticated API gateway
- Model files (`models/`) contain no secrets, but restrict write access to prevent tampering
