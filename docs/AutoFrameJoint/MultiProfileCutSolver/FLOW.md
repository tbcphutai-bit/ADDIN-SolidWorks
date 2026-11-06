# Existing Preview and Apply flow

```mermaid
flowchart TD
    S[Existing A/B SelectionPage callbacks] --> Q[Deferred callback dispatch and R50 protection]
    Q --> E[CommandEntry callback]
    E --> M[One GhostSessionManager session]
    M --> P[GhostPreview source and cache guards]
    P --> A[API Profile Analyzer]
    A --> R[Joint Rule Library]
    R --> U[Existing qualified U rule]
    R --> F[Measured open folded profile rule]
    U --> C[Independent ordered Cut Plans A/B]
    F --> C
    C --> B[Boolean on temporary Body2 copies]
    B --> V[KEEP, validity, gap, interference validation]
    V --> D[Existing Display3 Ghost Preview]
    D --> T[Reviewed current-source Apply ticket]
    T --> N[Native OK writes only owned features]
    N --> W{Rule representation}
    W --> L[Legacy U datum-plane writer]
    W --> G[New end-Face Sketch: Coincident and Tangent]
    L --> Z[Native per-step and final B-rep validation]
    G --> Z
    Z --> X[Success without automatic Save]
    Z --> Y[Failure: remove owned features and verify rollback]
```

No A/B selection callbacks or SelectionPage source were changed in this patch. Native work stays on the existing owner-thread path. Failed rules can be reused for at most 30 seconds only when the full qualified source signature still matches; native errors are not cached as rule refusals.

For the new rule, actual B-rep support can be a perpendicular planar Face or a certified native Vertex/Edge boundary. The direction still comes from the measured Frame axes. This support witness does not establish an associative reference to the other Part.

The new face writer applies to the folded BUTT rule. The existing U representation remains a separate, explicitly documented limitation. Cross-Frame relation and end-condition probes are research only; they are not a second production Preview pipeline.
