# CG2 REVIEW #2 — CODE C# HOÀN CHỈNH CHO ANTIGRAVITY

**Repo:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Branch:** `feat/check-hole-cg2-part-relations`  
**PR:** https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33  
**Xuất phát:** ChatGPT CG2 Review #2, nhận xét code thực tế.  
**Trạng thái:** **CODE PATCHED BY CHATGPT — BUILD/CAD RETEST NOT YET VERIFIED**.

> Đây không chỉ là hướng dẫn: ChatGPT đã **chỉnh trực tiếp các file C# trên branch**.
> Anti phải pull và đọc diff thật trước khi làm. Không copy/nhân đôi method đã tồn tại.

## 1. Các file mã đã sửa trực tiếp

1. `docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs`:
   - `ValidateCg2ExpectedCandidates` kiểm tra candidates thực xuất phát từ `CheckHoleReport.PartRelations.ExpectedHoleCandidates` sau khi gọi `Run()`: tồn tại ID, source/target occurrence, SourceHoleLabel, classification CHECK_ONLY, khoảng cách số hợp lệ.
   - Fixture `Fixture_OK.SLDASM` và `Fixture_MultiLayer_ABC.SLDASM`: so EXACT source occurrence `1618-04-bh-A10-fr1-2` và target `1618-04-bh-A10-1-1`, không dùng `IndexOf("B")`, `IndexOf("fr")` và không fallback sang candidates khác.
   - `LogCandidateInvestigation` chỉ in danh sách ứng viên thực, không hardcode “12/12 suppressed”; ghi rõ material contact, missing-hole proof, suppression site proof = NOT_VERIFIED.
   - `VerifyCsvParity` gọi `CheckHoleCsv.Write` của cả CG1/CG2 rồi so sánh **File.ReadAllBytes(...).SequenceEqual(...)**. Không sort, không đổi component, không dùng `ReadAllLines` nên không làm hỏng CSV quoted multiline.
   - `--self-test` đã có negative test: candidate `Classification="NG"` bắt buộc bị từ chối (không chỉ test một giá trị đúng).
2. `docs/antigravity/cg2/CG2_TEST_HARNESS.cs`: phân loại lại chính xác `UNIT_PRIMITIVE_PASS`, `UNIT_SANITY_PASS`, `UNIT_EVALUATESTACK_PASS`, và phần integrated chưa được thử là `NOT_TESTED`. Không còn tuyên bố rằng T02/T10/T12 đã chạy end-to-end.
3. **Không sửa** `Commands/CheckAssemblyHole.PartRelations.cs`, `CheckAssemblyHole.cs`, `CheckHoleResultsForm.cs`, `ADDIN.csproj`, CG1 logic hay dung sai NG.

## 2. CODE ĐẦY ĐỦ — Candidate provenance / exact Part B assertion

Mã dưới đây **đã nằm trong `CG2_LIVE_CAD_RUNNER.cs`**, không cần chèn lại.

```csharp
        // Actual CG2 diagnostics. AABB candidates are NOT proven missing holes.
        private static void LogCandidateInvestigation(string modelName, Metrics m)
        {
            if (!string.Equals(modelName, "Fixture_OK.SLDASM", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(modelName, "Fixture_MultiLayer_ABC.SLDASM", StringComparison.OrdinalIgnoreCase))
                return;

            Console.WriteLine("\n  [CG2 AABB CANDIDATE EVIDENCE: " + modelName + "]");
            Console.WriteLine("  Actual recorded candidates=" + m.Cg2ExpectedCandidates.Count
                + " | Total candidate count=" + m.Cg2Candidates);
            foreach (var c in m.Cg2ExpectedCandidates.Take(6))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  Candidate #{0}: Source=[{1}] ({2}) -> Target=[{3}] | Gap={4:F3}mm | AxisDist={5:F3}mm | {6}",
                    c.Id, c.SourceOccurrence, c.SourceHoleLabel,
                    c.TargetOccurrence, c.BoxSeparationMm, c.AxisToBoxMm, c.Classification));
            Console.WriteLine("  PHYSICAL_MATERIAL_CONTACT=NOT_VERIFIED");
            Console.WriteLine("  MISSING_HOLE_AT_SITE=NOT_VERIFIED");
            Console.WriteLine("  COAXIAL_SITE_SUPPRESSION=NOT_VERIFIED");
        }

        // The fixture manifest identifies exact occurrence IDs; NO substring
        // matching like "B" / "fr", and NO fallback to unrelated candidates.
        // It proves candidate provenance ONLY, not a real missing hole.
        private static void ValidateCg2ExpectedCandidates(string modelName, Metrics m)
        {
            if (!m.HasCg2)
                throw new InvalidOperationException("CG2 diagnostics missing");

            if (m.Cg2ExpectedCandidates.Count > m.Cg2Candidates)
                throw new InvalidOperationException("Stored candidates exceed reported candidate count");

            var seenIds = new HashSet<int>();
            foreach (CandidateDetail c in m.Cg2ExpectedCandidates)
            {
                if (c == null || c.Id <= 0 || !seenIds.Add(c.Id)
                    || string.IsNullOrWhiteSpace(c.SourceOccurrence)
                    || string.IsNullOrWhiteSpace(c.TargetOccurrence)
                    || string.IsNullOrWhiteSpace(c.SourceHoleLabel)
                    || !string.Equals(c.Classification, "CHECK_ONLY", StringComparison.Ordinal)
                    || double.IsNaN(c.BoxSeparationMm) || double.IsInfinity(c.BoxSeparationMm)
                    || double.IsNaN(c.AxisToBoxMm) || double.IsInfinity(c.AxisToBoxMm))
                    throw new InvalidOperationException("CG2 candidate provenance/values invalid at ID="
                        + (c == null ? "<null>" : c.Id.ToString(CultureInfo.InvariantCulture)));
            }

            if (string.Equals(modelName, "Fixture_Unmatched.SLDASM", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modelName, "run07_Missing_Ura_BOM.SLDASM", StringComparison.OrdinalIgnoreCase))
            {
                if (m.Cg2Candidates != 0 || m.Cg2ExpectedCandidates.Count != 0)
                    throw new InvalidOperationException("Expected zero candidates for fixture " + modelName);
            }

            if (string.Equals(modelName, "Fixture_OK.SLDASM", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modelName, "Fixture_MultiLayer_ABC.SLDASM", StringComparison.OrdinalIgnoreCase))
            {
                const string sourceA = "1618-04-bh-A10-fr1-2";
                const string expectedB = "1618-04-bh-A10-1-1";
                var found = m.Cg2ExpectedCandidates.Where(c =>
                    string.Equals(c.SourceOccurrence, sourceA, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.TargetOccurrence, expectedB, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (found.Count == 0)
                    throw new InvalidOperationException(
                        "No actual CHECK_ONLY AABB candidate with exact source A and target B: " + modelName);
                Console.WriteLine("  EXACT OCCURRENCE A->B candidates observed: " + found.Count
                    + " (AABB screening ONLY; site material not proven)");
            }
        }
```

**Lưu ý:** Đây là **AABB_CANDIDATE_OBSERVED**, chưa chứng minh vật liệu thực hay vị trí thật cần lỗ. IDs hiện là fixture-specific expectations từ report trước; Anti cần xác nhận trong CAD fixture manifest. `SourceHoleLabel="Hole#n"` là nhãn theo thứ tự, **không phải geometric fingerprint ổn định**.

## 3. CODE ĐẦY ĐỦ — Strict CSV bytes

```csharp
        // STRICT CSV BYTE PARITY. No sorting, no swapping of components,
        // no ReadAllLines (which breaks quoted multiline CSV records).
        // This checks the REAL production CheckHoleCsv.Write() output.
        private static void VerifyCsvParity(
            string root, string modelName,
            object baselineReport, object candidateReport,
            Assembly baseAsm, Assembly candAsm)
        {
            string testDir = Path.Combine(root, "CsvTestParity");
            if (!Directory.Exists(testDir)) Directory.CreateDirectory(testDir);

            string stem = Path.GetFileNameWithoutExtension(modelName);
            string baseCsv = Path.Combine(testDir, stem + "_baseline.csv");
            string candCsv = Path.Combine(testDir, stem + "_candidate.csv");
            Type baseType = baseAsm.GetType("ADDIN.Commands.CheckHoleCsv", true);
            Type candType = candAsm.GetType("ADDIN.Commands.CheckHoleCsv", true);
            MethodInfo baseWrite = baseType.GetMethod("Write", BindingFlags.Public | BindingFlags.Static);
            MethodInfo candWrite = candType.GetMethod("Write", BindingFlags.Public | BindingFlags.Static);
            if (baseWrite == null || candWrite == null)
                throw new MissingMethodException("CheckHoleCsv.Write is missing");

            // Distinct report objects, same isolated output folder.
            baseWrite.Invoke(null, new object[] { baseCsv, baselineReport });
            candWrite.Invoke(null, new object[] { candCsv, candidateReport });

            if (!File.Exists(baseCsv) || !File.Exists(candCsv))
                throw new FileNotFoundException("CSV parity output was not created");
            byte[] a = File.ReadAllBytes(baseCsv);
            byte[] b = File.ReadAllBytes(candCsv);
            if (a.Length == 0 || b.Length == 0)
                throw new InvalidOperationException("CSV parity output is empty");

            if (!a.SequenceEqual(b))
            {
                int first = 0;
                int n = Math.Min(a.Length, b.Length);
                while (first < n && a[first] == b[first]) first++;
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "CSV_BYTE_PARITY_FAIL model={0}, baselineBytes={1}, candidateBytes={2}, firstDiffByte={3}",
                    modelName, a.Length, b.Length, first));
            }
            Console.WriteLine("  CSV_BYTE_PARITY=PASS; exact bytes=" + a.Length
                + "; sha256=" + Sha256(baseCsv));
        }
```

Nếu byte CSV khác nhau thì **phải FAIL** hoặc phân loại nguyên nhân khác biệt. Không được tự sắp xếp / đổi chỗ cột để biến FAIL thành PASS rồi ghi `CSV_BYTE_PARITY=PASS`. Thay đổi thứ tự dòng CSV là khác biệt đầu ra thực, cần được ghi nhận.

## 4. CODE TEST HARNESS đã được cập nhật nhãn trung thực

Bản code source đầy đủ: [CG2_TEST_HARNESS.cs](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-cg2-part-relations/docs/antigravity/cg2/CG2_TEST_HARNESS.cs).

Bản C# hiện hành của `RunScenarioTestsT01ToT12` có nhãn phân loại đã sửa:

```csharp
        private static int RunScenarioTestsT01ToT12(Assembly asm)
        {
            Console.WriteLine("\n--- 3. CG2 primitive tests (NOT full CG2-T01..T12 integrated coverage) ---");
            int errors = 0;

            Type checkerType = asm.GetType("ADDIN.Commands.CheckAssemblyHole");
            Type holeAxisType = checkerType.GetNestedType("HoleAxis", BindingFlags.NonPublic);
            Type cg2ReportType = asm.GetType("ADDIN.Commands.CheckHoleCg2Report");
            MethodInfo readBox = checkerType.GetMethod("Cg2ReadBox", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo boxGap = checkerType.GetMethod("Cg2BoxGap", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo axisCross = checkerType.GetMethod("Cg2AxisCrossesBoxNear", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo counterpart = checkerType.GetMethod("Cg2LikelyKnownCounterpart", BindingFlags.NonPublic | BindingFlags.Static);

            // CG2-T01: Two Parts, aligned holes
            // Assert that when coaxial counterpart hole exists within 4mm/3deg, candidate IS suppressed;
            // and when counterpart is missing or offset > 4mm, candidate IS NOT suppressed.
            {
                object boxA = readBox.Invoke(null, new object[] { "PartA-1", new double[] { 0, 0, 0, 0.020, 0.020, 0.010 } });
                object boxB = readBox.Invoke(null, new object[] { "PartB-1", new double[] { 0, 0, 0.011, 0.020, 0.020, 0.021 } });
                double gap = (double)boxGap.Invoke(null, new object[] { boxA, boxB });
                bool isNear = gap <= 0.002;

                object hA = Activator.CreateInstance(holeAxisType);
                holeAxisType.GetField("Center").SetValue(hA, new double[] { 0.010, 0.010, 0.005 });
                holeAxisType.GetField("Direction").SetValue(hA, new double[] { 0, 0, 1 });

                object[] parms = new object[] { new double[] { 0.010, 0.010, 0.005 }, new double[] { 0, 0, 1 }, boxB, 0.0 };
                bool hit = (bool)axisCross.Invoke(null, parms);

                // Target has coaxial hole
                object hBCoaxial = Activator.CreateInstance(holeAxisType);
                holeAxisType.GetField("Center").SetValue(hBCoaxial, new double[] { 0.010, 0.010, 0.015 });
                holeAxisType.GetField("Direction").SetValue(hBCoaxial, new double[] { 0, 0, 1 });
                bool suppressed = (bool)counterpart.Invoke(null, new object[] { hA, hBCoaxial });

                // Negative case: counterpart offset 6mm (> 4mm)
                object hBFar = Activator.CreateInstance(holeAxisType);
                holeAxisType.GetField("Center").SetValue(hBFar, new double[] { 0.016, 0.010, 0.015 });
                holeAxisType.GetField("Direction").SetValue(hBFar, new double[] { 0, 0, 1 });
                bool suppressedFar = (bool)counterpart.Invoke(null, new object[] { hA, hBFar });

                if (isNear && hit && suppressed && !suppressedFar)
                {
                    Console.WriteLine("UNIT_PRIMITIVE_PASS [CG2-T01]: Cg2LikelyKnownCounterpart returns expected values; full BuildCg2PartRelations test NOT_TESTED");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T01]: isNear=" + isNear + ", hit=" + hit + ", suppressed=" + suppressed + ", suppressedFar=" + suppressedFar);
                    errors++;
                }
            }

            // CG2-T02: A-B-C layered, B genuinely lacks hole
            // Part A has hole, Part C has hole. Part B is in between them with NO holes.
            // Assert that hole axis from A crosses B, and because B has no counterpart, candidate is generated targeting B with Classification=CHECK_ONLY.
            {
                object boxA = readBox.Invoke(null, new object[] { "PartA-1", new double[] { 0, 0, 0, 0.030, 0.030, 0.010 } });
                object boxB = readBox.Invoke(null, new object[] { "PartB-1", new double[] { 0, 0, 0.011, 0.030, 0.030, 0.021 } });
                object boxC = readBox.Invoke(null, new object[] { "PartC-1", new double[] { 0, 0, 0.022, 0.030, 0.030, 0.032 } });

                double gapAB = (double)boxGap.Invoke(null, new object[] { boxA, boxB });
                object[] crossParms = new object[] { new double[] { 0.015, 0.015, 0.005 }, new double[] { 0, 0, 1 }, boxB, 0.0 };
                bool hitB = (bool)axisCross.Invoke(null, crossParms);
                double distOnAxis = (double)crossParms[3];

                // B has zero holes
                List<object> bHoles = new List<object>();
                bool counterpartFound = bHoles.Any();

                // Candidate properties assertion
                bool candidateCreated = (gapAB <= 0.002) && hitB && !counterpartFound;
                double distMm = distOnAxis * 1000.0;

                if (candidateCreated && distMm < 60.0)
                {
                    Console.WriteLine("UNIT_PRIMITIVE_PASS [CG2-T02]: Axis intersects box B at " + distMm.ToString("F2") + "mm, but integrated ExpectedHoleCandidates for B NOT_TESTED");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T02]: candidateCreated=" + candidateCreated + ", distMm=" + distMm);
                    errors++;
                }
            }

            // CG2-T03: Blind / thread valid joint
            // Part B solid does not require through hole; axis crosses B.
            // Assert candidate remains strictly CHECK_ONLY and never creates a missing-hole NG.
            {
                object boxA = readBox.Invoke(null, new object[] { "Plate-1", new double[] { 0, 0, 0, 0.050, 0.050, 0.010 } });
                object boxB = readBox.Invoke(null, new object[] { "SolidBlock-1", new double[] { 0, 0, 0.010, 0.050, 0.050, 0.060 } });

                object[] crossParms = new object[] { new double[] { 0.025, 0.025, 0.005 }, new double[] { 0, 0, 1 }, boxB, 0.0 };
                bool hit = (bool)axisCross.Invoke(null, crossParms);

                // CG2 ExpectedHoleRow reason and classification check
                string classification = "CHECK_ONLY";
                string reason = "Axis crosses approximate component bounding box; actual material/contact/fastening intent NOT verified.";

                if (hit && classification == "CHECK_ONLY" && reason.Contains("NOT verified"))
                {
                    Console.WriteLine("UNIT_PRIMITIVE_PASS [CG2-T03]: axis crosses box; production intent/NG decision NOT_TESTED");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T03]: hit=" + hit + ", classification=" + classification);
                    errors++;
                }
            }

            // CG2-T04: Independent B-D site plus A-B-C
            // Assert separate candidate rows for site 1 (A->B) and site 2 (D->B) without global master on B.
            {
                object boxB = readBox.Invoke(null, new object[] { "PartB-1", new double[] { 0, 0, 0, 0.100, 0.100, 0.020 } });
                object boxA = readBox.Invoke(null, new object[] { "PartA-1", new double[] { 0, 0, -0.011, 0.040, 0.040, -0.001 } });
                object boxD = readBox.Invoke(null, new object[] { "PartD-1", new double[] { 0.060, 0.060, -0.011, 0.100, 0.100, -0.001 } });

                bool nearAB = (double)boxGap.Invoke(null, new object[] { boxA, boxB }) <= 0.002;
                bool nearDB = (double)boxGap.Invoke(null, new object[] { boxD, boxB }) <= 0.002;

                object[] pA = new object[] { new double[] { 0.020, 0.020, -0.005 }, new double[] { 0, 0, 1 }, boxB, 0.0 };
                object[] pD = new object[] { new double[] { 0.080, 0.080, -0.005 }, new double[] { 0, 0, 1 }, boxB, 0.0 };
                bool hitA = (bool)axisCross.Invoke(null, pA);
                bool hitD = (bool)axisCross.Invoke(null, pD);

                // Both sites hit Part B independently
                if (nearAB && nearDB && hitA && hitD)
                {
                    Console.WriteLine("UNIT_PRIMITIVE_PASS [CG2-T04]: Two rays intersect B AABB; actual occurrence rows NOT_TESTED");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T04]: nearAB=" + nearAB + ", nearDB=" + nearDB + ", hitA=" + hitA + ", hitD=" + hitD);
                    errors++;
                }
            }

            // CG2-T05: Concave Part AABB false positive
            // U-shaped part: AABB spans [0..100, 0..100, 0..20], but center [30..70, 0..100, 0..20] is hollow void.
            // Hole passes through void. Broad-phase AABB detects hit, but labeled CHECK_ONLY with explicit disclaimer.
            {
                object uBox = readBox.Invoke(null, new object[] { "U-Bracket-1", new double[] { 0, 0, 0, 0.100, 0.100, 0.020 } });
                object[] voidParms = new object[] { new double[] { 0.050, 0.050, -0.010 }, new double[] { 0, 0, 1 }, uBox, 0.0 };
                bool hitVoid = (bool)axisCross.Invoke(null, voidParms);

                // Candidate must be labeled CHECK_ONLY, NOT solid contact
                if (hitVoid)
                {
                    Console.WriteLine("UNIT_PRIMITIVE_PASS [CG2-T05]: Axis hits AABB; no real BREP material test conducted");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T05]: hitVoid=" + hitVoid);
                    errors++;
                }
            }

            // CG2-T06: Repeated Part file, distinct occurrences
            // Bracket-1 and Bracket-2 pointing to same Part file.
            // Assert Cg2ReadBox creates distinct boxes and tracks them by occurrence identity.
            {
                object b1 = readBox.Invoke(null, new object[] { "Bracket-1", new double[] { 0, 0, 0, 0.010, 0.010, 0.010 } });
                object b2 = readBox.Invoke(null, new object[] { "Bracket-2", new double[] { 0.100, 0, 0, 0.110, 0.010, 0.010 } });

                FieldInfo occField = b1.GetType().GetField("Occurrence");
                string occ1 = (string)occField.GetValue(b1);
                string occ2 = (string)occField.GetValue(b2);

                if (occ1 == "Bracket-1" && occ2 == "Bracket-2" && occ1 != occ2)
                {
                    Console.WriteLine("UNIT_PRIMITIVE_PASS [CG2-T06]: Cg2ReadBox preserves occurrence strings (" + occ1 + ", " + occ2 + ") ; full assembly occurrence reconciliation NOT_TESTED");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T06]: occ1=" + occ1 + ", occ2=" + occ2);
                    errors++;
                }
            }

            // CG2-T07: Suppressed / unreadable component / invalid bounds
            // Assert invalid raw bounds return null from Cg2ReadBox, and trigger CoverageStatus=PARTIAL_UNVERIFIED.
            {
                object bNull = readBox.Invoke(null, new object[] { "Bad-1", null });
                object bNaN = readBox.Invoke(null, new object[] { "Bad-2", new double[] { double.NaN, 0, 0, 1, 1, 1 } });
                object bInf = readBox.Invoke(null, new object[] { "Bad-3", new double[] { 0, 0, 0, double.PositiveInfinity, 1, 1 } });

                int skippedCount = (bNull == null ? 1 : 0) + (bNaN == null ? 1 : 0) + (bInf == null ? 1 : 0);
                bool partial = (skippedCount > 0);
                string coverageStatus = partial ? "PARTIAL_UNVERIFIED" : "BBOX_SCREENED_NOT_MATERIAL_VERIFIED";

                if (skippedCount == 3 && coverageStatus == "PARTIAL_UNVERIFIED")
                {
                    Console.WriteLine("UNIT_PRIMITIVE_PASS [CG2-T07]: invalid bounds rejected by Cg2ReadBox; integrated partial status NOT_TESTED");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T07]: skippedCount=" + skippedCount + ", coverageStatus=" + coverageStatus);
                    errors++;
                }
            }

            // CG2-T08: Zero-hole Assembly
            // When assembly has 0 detected holes, loop does not execute; candidate count is 0, nearby pairs may exist.
            {
                object boxA = readBox.Invoke(null, new object[] { "Plate-1", new double[] { 0, 0, 0, 0.050, 0.050, 0.010 } });
                object boxB = readBox.Invoke(null, new object[] { "Plate-2", new double[] { 0, 0, 0.011, 0.050, 0.050, 0.021 } });
                double gap = (double)boxGap.Invoke(null, new object[] { boxA, boxB });
                bool near = gap <= 0.002;

                int holeCount = 0;
                int candidateCount = 0;
                if (holeCount > 0) candidateCount++; // Loop does not run

                string coverage = "BBOX_SCREENED_NOT_MATERIAL_VERIFIED";

                if (near && candidateCount == 0 && coverage == "BBOX_SCREENED_NOT_MATERIAL_VERIFIED")
                {
                    Console.WriteLine("UNIT_SANITY_PASS [CG2-T08]: literal zero-hole branch sanity; production scan validated separately by CAD runner");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T08]: near=" + near + ", candidates=" + candidateCount);
                    errors++;
                }
            }

            // CG2-T09: Large Assembly work capping
            // Assert exact capping constant thresholds in CheckAssemblyHole.
            {
                FieldInfo fMaxPairs = checkerType.GetField("Cg2MaxPairChecks", BindingFlags.NonPublic | BindingFlags.Static);
                FieldInfo fMaxHoles = checkerType.GetField("Cg2MaxHoleChecks", BindingFlags.NonPublic | BindingFlags.Static);
                FieldInfo fMaxStoredPairs = checkerType.GetField("Cg2MaxStoredPairs", BindingFlags.NonPublic | BindingFlags.Static);
                FieldInfo fMaxStoredCand = checkerType.GetField("Cg2MaxStoredCandidates", BindingFlags.NonPublic | BindingFlags.Static);

                int maxPairs = (int)fMaxPairs.GetValue(null);
                int maxHoles = (int)fMaxHoles.GetValue(null);
                int maxStoredPairs = (int)fMaxStoredPairs.GetValue(null);
                int maxStoredCand = (int)fMaxStoredCand.GetValue(null);

                bool cappedLogic = (maxPairs == 300000 && maxHoles == 500000 && maxStoredPairs == 1500 && maxStoredCand == 2000);
                if (cappedLogic)
                {
                    Console.WriteLine(string.Format("UNIT_CONSTANTS_PASS [CG2-T09]: cap constants checked ({0}, {1}, {2}, {3}); actual capping branch NOT_TESTED",
                        maxPairs, maxHoles, maxStoredPairs, maxStoredCand));
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T09]: Capping constants mismatch");
                    errors++;
                }
            }

            // CG2-T10: Rerun / cancel / model state invariance
            // Verify OperationCanceledException propagation and model state checking logic.
            {
                MethodInfo cancelMethod = checkerType.GetMethod("ThrowIfCanceled", BindingFlags.NonPublic | BindingFlags.Instance);
                // When cancellation is triggered in CheckAssemblyHole, it must throw OperationCanceledException
                // and the catch block in CheckAssemblyHole.cs must rethrow it.
                bool cancelRethrown = false;
                try
                {
                    var cts = new CancellationTokenSource();
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelRethrown = true;
                }

                // Model state invariance logic test
                string stateBefore = "Sig_Features_10_Markers_0";
                string stateAfterSame = "Sig_Features_10_Markers_0";
                string stateAfterMutated = "Sig_Features_11_Markers_1";

                bool passesSame = string.Equals(stateBefore, stateAfterSame, StringComparison.Ordinal);
                bool rejectsMutated = !string.Equals(stateBefore, stateAfterMutated, StringComparison.Ordinal);

                if (cancelRethrown && passesSame && rejectsMutated)
                {
                    Console.WriteLine("UNIT_SANITY_PASS [CG2-T10]: synthetic OperationCanceledException and string comparison; actual cancellation/rerun NOT_TESTED");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T10]: cancelRethrown=" + cancelRethrown + ", passesSame=" + passesSame + ", rejectsMutated=" + rejectsMutated);
                    errors++;
                }
            }

            // CG2-T11: Regression parity against CG1 production baseline
            // Test EvaluateStack tolerance boundaries (0.09mm OK, 0.11mm NG, 0.35 deg NG).
            {
                try
                {
                    Type stackType = checkerType.GetNestedType("HoleStack", BindingFlags.NonPublic);
                    Type stackResultType = checkerType.GetNestedType("HoleStackResult", BindingFlags.NonPublic);
                    MethodInfo evalMethod = checkerType.GetMethod("EvaluateStack", BindingFlags.NonPublic | BindingFlags.Instance);

                    if (evalMethod != null && stackType != null && stackResultType != null)
                    {
                        object checkerInst = Activator.CreateInstance(checkerType, new object[] { null });

                        // EvaluateStack with 0.09mm lateral offset (<= 0.10mm) -> OK
                        object h1 = Activator.CreateInstance(holeAxisType);
                        holeAxisType.GetField("Center").SetValue(h1, new double[] { 0, 0, 0 });
                        holeAxisType.GetField("Direction").SetValue(h1, new double[] { 0, 0, 1 });
                        holeAxisType.GetField("ComponentOccurrence").SetValue(h1, "PartA-1");

                        object h2 = Activator.CreateInstance(holeAxisType);
                        holeAxisType.GetField("Center").SetValue(h2, new double[] { 0.00009, 0, 0.010 });
                        holeAxisType.GetField("Direction").SetValue(h2, new double[] { 0, 0, 1 });
                        holeAxisType.GetField("ComponentOccurrence").SetValue(h2, "PartB-1");

                        object stackOk = Activator.CreateInstance(stackType);
                        IList listHolesOk = (IList)stackType.GetField("Holes").GetValue(stackOk);
                        listHolesOk.Add(h1); listHolesOk.Add(h2);

                        object resOk = evalMethod.Invoke(checkerInst, new object[] { stackOk });
                        bool isNgOk = (bool)stackResultType.GetField("IsNg").GetValue(resOk);

                        // EvaluateStack with 0.11mm lateral offset (> 0.10mm) -> NG
                        object h3 = Activator.CreateInstance(holeAxisType);
                        holeAxisType.GetField("Center").SetValue(h3, new double[] { 0.00011, 0, 0.010 });
                        holeAxisType.GetField("Direction").SetValue(h3, new double[] { 0, 0, 1 });
                        holeAxisType.GetField("ComponentOccurrence").SetValue(h3, "PartB-1");

                        object stackNg = Activator.CreateInstance(stackType);
                        IList listHolesNg = (IList)stackType.GetField("Holes").GetValue(stackNg);
                        listHolesNg.Add(h1); listHolesNg.Add(h3);

                        object resNg = evalMethod.Invoke(checkerInst, new object[] { stackNg });
                        bool isNgNg = (bool)stackResultType.GetField("IsNg").GetValue(resNg);

                        if (!isNgOk && isNgNg)
                        {
                            Console.WriteLine("UNIT_EVALUATESTACK_PASS [CG2-T11]: production EvaluateStack threshold 0.09/0.11mm; cross-binary parity tested separately");
                        }
                        else
                        {
                            Console.WriteLine("FAIL [CG2-T11]: isNgOk=" + isNgOk + ", isNgNg=" + isNgNg);
                            errors++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL [CG2-T11] Exception: " + ex.GetType().FullName + " -> " + ex.Message);
                    if (ex.InnerException != null)
                        Console.WriteLine("  Inner: " + ex.InnerException.GetType().FullName + " -> " + ex.InnerException.Message);
                    errors++;
                }
            }

            // CG2-T12: Deliberate CG2 API Error Fallback
            // Test that when an exception occurs inside CG2 scan, the catch block sets
            // IsPartial=true and CoverageStatus="ERROR_UNVERIFIED", while production report is completely preserved.
            {
                object rep = Activator.CreateInstance(asm.GetType("ADDIN.Commands.CheckHoleReport"));
                asm.GetType("ADDIN.Commands.CheckHoleReport").GetField("LogicalHoles").SetValue(rep, 100);
                asm.GetType("ADDIN.Commands.CheckHoleReport").GetField("NgStacks").SetValue(rep, 5);

                // Simulate the exact catch block in CheckAssemblyHole.cs:
                Exception cg2Error = new InvalidOperationException("Simulated COM Failure in CG2");
                object cg2Fallback = Activator.CreateInstance(cg2ReportType);
                cg2ReportType.GetField("IsPartial").SetValue(cg2Fallback, true);
                cg2ReportType.GetField("CoverageStatus").SetValue(cg2Fallback, "ERROR_UNVERIFIED");
                IList warnings = (IList)cg2ReportType.GetField("Warnings").GetValue(cg2Fallback);
                warnings.Add(cg2Error.GetType().Name);
                asm.GetType("ADDIN.Commands.CheckHoleReport").GetField("PartRelations").SetValue(rep, cg2Fallback);

                int logHoles = (int)asm.GetType("ADDIN.Commands.CheckHoleReport").GetField("LogicalHoles").GetValue(rep);
                int ngSt = (int)asm.GetType("ADDIN.Commands.CheckHoleReport").GetField("NgStacks").GetValue(rep);
                bool isPart = (bool)cg2ReportType.GetField("IsPartial").GetValue(cg2Fallback);
                string cov = (string)cg2ReportType.GetField("CoverageStatus").GetValue(cg2Fallback);

                if (logHoles == 100 && ngSt == 5 && isPart && cov == "ERROR_UNVERIFIED" && warnings.Count == 1)
                {
                    Console.WriteLine("UNIT_OBJECT_SETUP_PASS [CG2-T12]: synthetic fallback object only; actual Run() error branch NOT_TESTED");
                }
                else
                {
                    Console.WriteLine("FAIL [CG2-T12]: logHoles=" + logHoles + ", ngSt=" + ngSt + ", cov=" + cov);
                    errors++;
                }
            }

            return errors;
        }
```

**Điều rất quan trọng:** các tests T01–T12 sử dụng primitives chưa phải pipeline-end-to-end, do đó **không được in `CG2-T01..T12 ALL PASS` trong báo cáo**. Test T11 `EvaluateStack` là threshold unit, baseline CG1 vs CG2 parity được kiểm bằng `CG2_LIVE_CAD_RUNNER`, không trộn hai loại bằng chứng.

## 5. Antigravity PHẢI làm

1. Pull branch CG2 mới; `git diff origin/feat/check-hole-connection-groups-cg1...HEAD`; kiểm tra code trên trước khi biên dịch.
2. Build `Debug|x64` và `Release|x64` bằng compiler thực, ghi log đầy đủ errors, warnings, commit SHA và DLL hash.
3. Chạy `CG2_LIVE_CAD_RUNNER.exe --self-test` với exit code 0 và negative candidate test PASS.
4. Dùng Pack and Go disposable sandbox, đóng user session SOLIDWORKS, chạy lại 6 fixtures; so đúng CSV bytes bằng `CheckHoleCsv.Write`, không dùng parser single-line.
5. Với `Fixture_OK` và `Fixture_MultiLayer_ABC`, xác minh exact component identities trong fixture. Phải có candidate A->B trên report thực để T02 screen đạt. Nếu fixture thay đổi IDs, không tự thay IDs để kiếm PASS; xác nhận rồi cập nhật manifest.
6. Với T07, T09, T10, T12: chỉ báo PASS khi thật sự kích hoạt đường production scan/partial/cancel/error; nếu không có fixture/injection, để `NOT_TESTED` và giải thích blocker.
7. **Không được ghi rằng 13 candidates = 13 lỗ thiếu**. CG2 hiện sàng lọc bounding boxes xấp xỉ.
8. Đối chiếu số rows, OK/NG, Pattern/Missing/Recovery, CSV, marker safety của CG1/CG2; marker generation/update parity vẫn `NOT_TESTED` nếu tắt AutoCreateMarkers.
9. Cập nhật `CG2_REPORT.md` bằng log mới của code mới; không tái sử dụng PASS của binary cũ.
10. Commit/push, phản hồi Review #2 và gửi full SHA + build/test logs. **GIỮ PR #33 DRAFT, KHÔNG MERGE, KHÔNG LÀM CG3**.

## 6. Tiêu chí nghiệm thu tách rõ

| Hạng mục | Trạng thái trước khi Anti retest |
|---|---|
| Source C# candidate assertions | Đã viết/push; compile chưa xác minh |
| Source C# strict CSV byte parity | Đã viết/push; chạy CAD chưa xác minh |
| Source C# negative candidate validation | Đã viết/push; self-test chưa xác minh |
| Full 12 scenarios CG2 end-to-end | NOT_VERIFIED |
| BREP/material contact & fastening intent | NOT_IMPLEMENTED |
| CG1 production result unaffected | Cần retest binary mới |
| 3D marker generation parity | NOT_TESTED |
| Ready to merge | **NO** |

**Quy tắc:** ChatGPT sẽ đọc lại source và test mới trên GitHub; Anti không tự nghiệm thu bằng PASS log. Nếu code mới lỗi compile, Anti sửa đúng code test và báo về GitHub; tuyệt đối không sửa production engine chỉ để test xanh.
