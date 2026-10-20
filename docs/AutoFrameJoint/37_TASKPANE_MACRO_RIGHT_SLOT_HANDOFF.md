# 37 — Position TAI TOOL > Model > Macro Auto Frame Joint launcher in the highlighted right-hand tile

**CODE OWNER:** ChatGPT. **ANTIGRAVITY ROLE:** copy the exact committed source; no reimplementation.

## User's exact visual target

In the supplied SOLIDWORKS screenshot, add/show the **Giao góc Frame** button inside TAI TOOL → Model → Macro, in the empty right-hand tile adjacent to **Check chấn**. Do NOT add a new row or a button to the PropertyManager just to implement this TaskPane launcher.

## Actual repository findings (based on verified C# files)

The launcher ALREADY EXISTS in source:
- `UI/AutoFrameJointTaskPaneLauncher.cs` defines `private ModernButton btnAutoFrameJoint` and `EnsureAutoFrameJointButton()`, and its Click handler calls `AutoFrameJointCommandEntry.Open(swApp)`.
- `UI/BomCommandLayout.cs` invokes `EnsureAutoFrameJointButton()` inside `EnsureModelMacroControls()`. The `macroTiles` array ends with `btnCheckBendMinimum, btnAutoFrameJoint`. The existing `LayoutTiles` fills TWO columns whenever the tile area is wide enough, making the 8th tile the **fourth-row right-hand** tile.
- `BomTaskPaneControl.cs` allows `btnAutoFrameJoint` only in Assembly through `IsModelMacroSupported` and `UpdateModelMacroCommandAvailability`.
- `ADDIN.csproj` already includes the launcher, PMP, CommandEntry, GhostPreview and GhostSessionManager once each.
- **No duplicated launcher/button should be created.**
- If the highlighted slot in the actual running SOLIDWORKS remains empty, the loaded DLL MAY predate this launcher. A source edit alone cannot change an already-running assembly; do not claim the UI was verified before a future deliberate Rebuild/Deploy.

## Exact real C# code change (committed)

File: `UI/AutoFrameJointTaskPaneLauncher.cs`

The `ModernButton` initializer should retain:
```csharp
Name = "btnAutoFrameJoint",
Text = "Giao góc Frame",
Subtitle = "Phân tích · Xem A/B",
Kind = ModernButtonKind.Tile,
Icon = TaiIcon.Assembly,
Tone = ModernButtonTone.Accent,
Size = new Size(180, 44),
AutoSize = false,
Enabled = false,
TabIndex = 33
```

Click handler remains:
```csharp
btnAutoFrameJoint.Click += (sender, args) =>
{
    if (!CanRunModelMacro(btnAutoFrameJoint))
        return;
    try
    {
        AutoFrameJointCommandEntry.Open(swApp);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Trace.WriteLine(
            "[AFJ-LAUNCH] " + ex.ToString());
        MessageBox.Show(this,
            "Không thể mở Auto Frame Joint: " + ex.Message,
            "Auto Frame Joint",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
};
```

And the informative tooltip is now:
```csharp
SetBomCommandToolTip(btnAutoFrameJoint,
    "Mở PropertyManager để chọn Frame A/B, phân tích dài/ngắn " +
    "và xem bản sao hình học hiện tại. Chưa mô phỏng vết cắt, không sửa Part.");
```

**DO NOT** change the order of the tiles, the existing Check chấn button, the TaskPane Designer, API selection marks, Ghost Session Manager, or other commands.

## Antigravity exact copy/paste instructions — now (SOURCE ONLY)

1. Open an **isolated worktree** on `feat/afj-taskpane-macro-right-slot-20261020`, preserving any worktrees or files used by another running SOLIDWORKS debug session.
2. Read the entire `UI/AutoFrameJointTaskPaneLauncher.cs` from the ChatGPT commit. Copy its exact contents into this one file ONLY, unless it already matches.
3. Verify layout **without editing** `UI/BomCommandLayout.cs`: the last two `macroTiles` are `btnCheckBendMinimum, btnAutoFrameJoint`; `LayoutTiles` uses two columns at the illustrated width.
4. Verify `ADDIN.csproj` includes `UI\AutoFrameJointTaskPaneLauncher.cs` once, plus the existing AutoFrameJoint CommandEntry and PMP, and that `PreBuildEvent` / `PostBuildEvent` remain empty.
5. Run only READ-ONLY checks:
```powershell
git rev-parse HEAD
git status --short
git hash-object "UI/AutoFrameJointTaskPaneLauncher.cs"
git hash-object "UI/BomCommandLayout.cs"
git hash-object "ADDIN.csproj"
```
6. Compare output to commit blob hashes provided in the ChatGPT handoff. If any differs, stop and provide the raw diff; do NOT rewrite backend yourself.
7. Report to ChatGPT: actual branch, HEAD, hashes, and whether any other file differs.

**HOLD:** No Build/Rebuild, COM registration, Copy/Deploy DLL/PDB, start/stop SOLIDWORKS, merge into active Check Hole worktree, or Visual Studio F5. The user's other command is still debugging.

## After user authorizes Rebuild/Deploy later

1. End the other debug session safely and save work.
2. Use the isolated project branch with safe `bin\x64\Debug\` output and no automatic copy/kill; capture hashes of installed `C:\Tao moi\ADDIN.dll` + `.pdb` before/after Rebuild.
3. Only after coordinating the deploy can a **new** Add-in DLL be loaded in SOLIDWORKS to check the Macro panel.
4. Open Assembly and navigate TAI TOOL → Model → Macro.
5. Confirm exactly one tile **Giao góc Frame** on the fourth row, right of **Check chấn**, enabled for Assembly and disabled otherwise.
6. Click it and confirm native `AutoFrameJointSelectionPage` appears. Test selecting distinct Frame A/B and the explicit `Xem trạng thái A/B` only on a copied sample assembly.
7. Verify UI Preview is of the **current** component geometry, not a cut; Cancel hides temporary graphics. No feature changes or Save.

## Acceptance at this source-only stage

The source text now describes both Analyze and read-only A/B preview, preserving the existing correct tile placement and launch callback. **Runtime UI still NOT TESTED and no DLL is touched.**
