# Third-party software

NitroDrp is MIT licensed ([LICENSE](LICENSE)). It builds on the open-source software below, each under its own
license. This list covers what the app ships with or loads; it was checked against the installed packages' own
license fields.

## Shipped in the web app

All seven runtime npm packages (`react`, `react-dom`, `react-router`, `react-router-dom`, `scheduler`, `cookie`
and `set-cookie-parser`) are **MIT**. The exact set is pinned in `web/package-lock.json`.

## Shipped with the API

The AWS SDK for .NET (`AWSSDK.S3`, `AWSSDK.DynamoDBv2`, `AWSSDK.CloudFront` and its core) is **Apache-2.0**.
Anyone redistributing a build that includes it, such as a container image, should keep those license notices,
which come inside the NuGet packages. Versions are pinned in `api/**/packages.lock.json`.

Passkey sign-in uses `Fido2` (the FIDO2/WebAuthn library for .NET) with its dependencies `Fido2.Models`,
`NSec.Cryptography`, `System.Formats.Cbor` and `Microsoft.IdentityModel.*` (tokens, logging, abstractions), all
**MIT**. `NSec.Cryptography` bundles the `libsodium` native library, which is **ISC** licensed. Versions are pinned in
`api/**/packages.lock.json`.

## Loaded at runtime

The pages load **Inter** and **Space Mono** from Google Fonts (both SIL Open Font License 1.1). They are not
bundled in this repository. This means a visitor's browser contacts Google to fetch them; to avoid that, host
the font files yourself.

## Build and test tooling (not shipped)

Development dependencies are mostly MIT, with some Apache-2.0 (including Playwright), one ISC, one BSD-3-Clause
and `lightningcss` (with its platform builds) under MPL-2.0, which Vite uses at build time to process CSS. None of it is part of what
the app delivers.

## Not covered by MIT

The Jaded Media logo and name: see [web/src/assets/NOTICE.md](web/src/assets/NOTICE.md).
