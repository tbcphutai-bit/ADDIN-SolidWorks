# Log excerpt — R54 close investigation

Selected original lines; machine paths and unrelated startup/binding messages omitted.

```text
[AFJ-R52] stage=COMMAND_OPEN MVID=71279ae1-72e7-4f90-9a36-d8c03e0e0ddd
[AFJ-R53] SOURCE=UNIFIED_U_ADJUSTED_BISECTOR_FACE_SKETCH plane_fallback=DISABLED MVID=71279ae1-72e7-4f90-9a36-d8c03e0e0ddd
[AFJ-R52] stage=AUTO_FOCUS_END seq=25 epoch=1 thread=1 owner=1 cbDepth=0 focusCache=0 callbackCounts=1/-1 shown=True closing=False activated=True faulted=False B_focus=True; awaiting B submit/list callback
[AFJ-SEL] READY=False A-count=1 B-count=0 reason=Đã chọn Frame A — tiếp tục chọn Frame B.
[AFJ-R52] stage=CLOSE seq=32 epoch=1 thread=1 owner=1 cbDepth=0 focusCache=0 callbackCounts=1/-1 shown=True closing=False activated=True faulted=False reason=1
[AFJ-R52] stage=APPLY_CLOSE_WAIT seq=33 epoch=1 thread=1 owner=1 cbDepth=0 focusCache=0 callbackCounts=1/-1 shown=True closing=False activated=True faulted=False retained OK intent; selectionRevision=2 committed=0
An exception of type 'System.Runtime.InteropServices.COMException' occurred in ADDIN.dll but was not handled in user code
Auto Frame Joint is finishing the current Preview
The program '[PID] SLDWORKS.exe' has exited with code 4294967295 (0xffffffff).
```
