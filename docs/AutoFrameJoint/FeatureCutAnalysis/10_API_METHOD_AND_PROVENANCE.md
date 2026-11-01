# API Method and Provenance

An independent C# x64 console tool was compiled with the .NET Framework C# compiler and the installed official SOLIDWORKS interop assembly. It lives outside the Add-in project. No Add-in build, project-file edit or DLL deployment was performed. A readable source snapshot is supplied as `evidence/STA_READER_SOURCE.cs.txt`; compiled EXE/interop DLLs stay local.

The STA entry point first used GetActiveObject and enumerated matching ROT monikers. It attached to an existing ISldWorks session (revision 32.5.0); it never called Activator/CreateInstance, Process.Start(SLDWORKS), ExitApp or a COM registration command. Two process instances were visible outside sandbox; the reader only used the accessible existing ROT session.

The sample `1618-04-bh-A6-fr1.SLDPRT` was opened first with OpenDoc6(type=1, options=3: Silent | ReadOnly), verified non-null, and read successfully before widening to all 15 Parts. It checked title, path match, document type, configurations, ReadOnly, save flag, native body metrics, ordered features and source SHA256. Documents newly opened by the reader were closed without saving; pre-existing writable documents would be skipped.

The primary traversal uses FirstFeature/GetNextFeature and subfeature getters. Actual Cut identities use GetTypeName2 with GetTypeName fallback for ICE. GetDefinition is queried as IExtrudeFeatureData2 for scalar properties and end conditions; parent/subfeature Sketch data includes line/arc geometry, construction flags, contours/regions, transforms and supporting entities. Suppression is queried for each named configuration with IsSuppressed2; it is not changed. The reported full tree order is API order, not visual UI order.

Four T1 Parts initially opened in DefaultSM-FLAT-PATTERN. They were closed and opened Read-Only again with ConfigurationName=Default; no ShowConfiguration2 or suppression setter was called. Flat snapshots stay in the local raw extraction. The publication includes folded primary metadata and the configuration warning.

Selection reference getters on source Parts returned null/-1. The tool then created independently named byte-identical analysis copies. Only copyrefs mode can call AccessSelections, protected by a copy-path/name guard, with ReleaseSelectionAccess in finally. This briefly rolls the copies back to read feature scope and vertex endpoints, without writing files. It never rolls original documents back. Source/copy hashes were compared. No model Save/SaveAs, rebuild, feature suppression setter, ModifyDefinition or feature creation method is called. A dirty flag on 4114 is disclosed in the reports.

Source scalar values and analysis-copy reference values have separate provenance in JSON. Complete native body/face geometry and base-profile reconstruction stay local; public evidence contains report metrics, ordered tree data and the requested Cut Sketch data. Local machine/library paths are redacted. No original CAD, source PDF, source ZIP, unapproved source image, compiled tool or Add-in binary is published.

## Official API references

- [GetTypeName2: use legacy GetTypeName for ICE](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~GetTypeName2.html)
- [GetEndCondition](https://help.solidworks.com/2026/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IExtrudeFeatureData2~GetEndCondition.html)
- [GetEndConditionReference](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IExtrudeFeatureData2~GetEndConditionReference.html)
- [GetDirectionReference](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IExtrudeFeatureData2~GetDirectionReference.html)
- [AccessSelections changes rollback state](https://help.solidworks.com/2026/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IExtrudeFeatureData2~AccessSelections.html)
- [FlipSideToCut](https://help.solidworks.com/2023/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IExtrudeFeatureData2~FlipSideToCut.html)
- [Default Cut direction opposite Sketch normal](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.ifeaturemanager~featureextrusion3.html)
- [Body mass-property array and metric units](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~GetMassProperties.html)
