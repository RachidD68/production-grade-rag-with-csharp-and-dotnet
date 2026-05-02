# 0001 — Record architecture decisions

Date: 2026-05-02

## Status

Accepted

## Context

This codebase will evolve across 25 chapters and 9 build phases. Decisions made early (project layout, package pins, naming conventions, infra choices) will be revisited often. Without a written trail, future-us will not remember *why* a thing is the way it is, and the book's author cannot reference the rationale in errata.

## Decision

We will record every non-obvious architecture decision as a short ADR in `docs/decisions/NNNN-title.md`, using Michael Nygard's lightweight format:

```
# NNNN — Title

Date: YYYY-MM-DD

## Status
{Proposed | Accepted | Superseded by NNNN | Deprecated}

## Context
What is the issue we are facing?

## Decision
What did we decide, and what alternatives did we reject?

## Consequences
Positive, negative, and neutral consequences.
```

Each ADR is two paragraphs each per section — short enough to scan in a code review.

## Consequences

- New contributors can read the ADR sequence to understand the codebase's evolution.
- The book's author can quote the ADR text in errata or chapter notes when readers ask "why X over Y?".
- Maintaining ADRs adds a small overhead per decision; we accept that cost as much smaller than the cost of forgotten rationale.
