# Remaining Unverified Findings

The previous inability to read trees is resolved: all 15 Parts have readable native history and all 43 explicit Cut-Extrude nodes are API_VERIFIED. The following gaps concern interpretation or completeness, not fabricated feature identities.

1. **Direction reference semantics:** GetDirectionReference returns -1/null even after AccessSelections on byte-identical analysis copies. ReverseDirection/BothDirections and sketch normals are read, but treating missing explicit direction references as the default cut direction is INFERRED. The documented default cut direction is opposite the sketch normal; no setter was used to test this interpretation.
2. **Selected contours:** Contours is empty on the inspected definitions. The full parent Sketch is read, including construction flags, line/arc segments and transforms. Absence of a selected-subcontour object is reported; no selected region is invented. Open outlines must not be arbitrarily closed by an algorithm.
3. **Strict completeness:** 43 scalar/Sketch parameter sets are available and all three Up To Vertex endpoints were retrieved on copies. Nevertheless the report counts 0 definitions as strictly complete for deterministic replay because direction/selected-profile interpretation remains unresolved. Scope body names were retrieved on copies and refer to their feature stage.
4. **Before/after material:** removed volume, retained stock mask and exact per-region stock difference are NOT_AVAILABLE. No suppression/rebuild study was performed, including on analysis copies. AccessSelections temporarily rolls copies back solely for reference reading and releases them afterwards; it is not a recorded before/after study.
5. **Assembly mapping:** body→component occurrence, configuration and local→assembly transforms are UNVERIFIED. Native volume agreement with STEP is useful correspondence evidence, not an occurrence proof.
6. **Winner/loser:** no verified drawing assignment or KCC product-class applicability is recorded for the Frame pairs. Long/short length alone does not settle it. Outer/inner Lip labels need assembly orientation.
7. **Configuration coverage:** per-configuration suppression was read without activating configurations via a setter. Geometric/parameter reads cover the primary configurations listed. T1 Default and saved flat snapshots are both available; every alternative state has not been replayed.
8. **Other operations:** HoleWzd, CurvePattern, LibraryFeature, SheetMetal/SMBaseFlange are inventoried. Pattern instances and library-internal boolean manufacturing actions are not misreported as additional independent Cut-Extrude nodes.
9. **4114 dirty flag:** a source getter traversal changed an in-memory save flag, with the source document then closed without saving. The source hash is unchanged. The getter that triggered the flag has not been isolated.
10. **Missing example:** exact 2354 B11 native source remains NOT_AVAILABLE.

No API access action is currently required from the user. The sandbox restriction was overcome using the authorized external command context and an existing SOLIDWORKS session.
