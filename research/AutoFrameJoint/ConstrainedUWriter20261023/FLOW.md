# Prepared route and qualification boundary

```mermaid
flowchart TD
  UI[Existing A/B SelectionPage] --> Entry[CommandEntry and SessionManager]
  Entry --> Preview[GhostPreview and solver]
  Preview --> Intent[Retained reviewed construction intent]
  Intent --> Gate{U route qualified?}
  Gate -->|Default false| Stop[UNVERIFIED_APPLY before native mutation]
  Gate -->|Detached harness define only| Tx[Pair transaction and owned-feature journal]
  Tx --> Sketch[Existing-face linked constrained Sketches]
  Sketch --> Cut[Sequential Cut-Extrude and target checks]
  Cut --> Final[Body, gap, placement and graph checks]
  Final --> Commit[Commit only after all checks]
  Tx -->|Failure| Rollback[Global owned-feature rollback]
  Rollback --> Unverified[Currently UNVERIFIED: native COM traversal fault]
```

The candidate full entrypoint has compiled only; it has not executed in the installed Add-in. The earlier four-Cut roundtrip and latest partial transaction driver have different scope.
