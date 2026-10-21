# STEP vs Native Part

The supplied 1618a STEP inventory is preserved without changing its provenance: six solids, supplied B-rep validity and metrics, not independently re-imported here. The new native API independently measures the six 1618a Parts. Matching volumes and permuted bounding spans establish strong geometric candidate correspondences, but do not establish the component transform or exact occurrence/configuration mapping.

| STEP solid | Native candidate by invariant metrics | STEP volume mm³ | Native volume mm³ | Δ mm³ | Evidence / limitation |
| --- | --- | --- | --- | --- | --- |
| 1 | 1618-04-bh-A6-1.SLDPRT | 12388528.372 | 12388528.372081 | 0.000081 | GEOMETRY_VERIFIED metrics agreement; occurrence mapping UNVERIFIED |
| 2 | 1618-04-bh-A6-fr1.SLDPRT | 841441.673 | 841441.673422 | 0.000422 | GEOMETRY_VERIFIED metrics agreement; occurrence mapping UNVERIFIED |
| 3 | 1618-04-bh-A6-fr2.SLDPRT | 189468.111 | 189468.110862 | -0.000138 | GEOMETRY_VERIFIED metrics agreement; occurrence mapping UNVERIFIED |
| 4 | 1618-04-bh-A6-fr3.SLDPRT | 1170179.378 | 1170179.377746 | -0.000254 | GEOMETRY_VERIFIED metrics agreement; occurrence mapping UNVERIFIED |
| 5 | 1618-04-bh-A6-fr4.SLDPRT | 224057.615 | 224057.615028 | 0.000028 | GEOMETRY_VERIFIED metrics agreement; occurrence mapping UNVERIFIED |
| 6 | 1618-04-bh-A6-中骨1.SLDPRT | 884097.488 | 884097.487985 | -0.000015 | GEOMETRY_VERIFIED metrics agreement; occurrence mapping UNVERIFIED |

STEP lacks the ordered native operation history. The 43 cuts come from SLDPRT API reads, not STEP reconstruction. The native history has 503 top-level records across 15 files.

3615a T1/T2 STEP structural counts from the earlier scan remain in JSON/history. Their occurrence transforms and solid-level kernel matches have not been established. In particular T1 flat/folded configuration differs, so matching raw axis-aligned flat bounds to assembled STEP bounds would be invalid. The 4114 Part has no matching STEP in the package.

No original was suppressed, rebuilt or saved. No before/after B-rep difference or exact assembly contact proof was performed.
