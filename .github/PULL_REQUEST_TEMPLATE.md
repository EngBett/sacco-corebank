## What changed and why

## Which roadmap phase does this belong to?
(see docs/roadmap/ROADMAP.md)

## Seed data
- [ ] Seed data added/updated for any new entity or workflow introduced here
- [ ] A reviewer can pull this branch and demo the change end-to-end with zero production keys

## Compliance
- [ ] No change to FOSA/BOSA GL tagging without updating docs/compliance/sasra-mapping.md
- [ ] No change to maker-checker / segregation-of-duties logic without compliance-reviewer sign-off
- [ ] If this touches a payment provider, idempotency keys are unaffected or explicitly re-verified

## Tests
- [ ] Unit tests
- [ ] Integration tests (real Postgres, sandbox payment providers)
