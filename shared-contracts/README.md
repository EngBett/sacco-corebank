# shared-contracts

Single source of truth for the API shape consumed by `frontend/portal`,
`frontend/public-site`, and `mobile/`.

Contains (once generated): the backend's OpenAPI spec, and/or a generated TypeScript client.
Do not hand-maintain a duplicate of the API shape in any frontend app — generate from here so
the contract cannot silently drift from the actual backend.

TODO: wire up the generation step (e.g. NSwag or `openapi-typescript` against
`backend/src/Sacco.Api`'s OpenAPI output) as part of Phase 1 tooling.
