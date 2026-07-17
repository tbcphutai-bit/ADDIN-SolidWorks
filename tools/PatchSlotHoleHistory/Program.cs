using System.Text;

var root = args.Length > 0 ? args[0] : @"C:\SGN26\addin\ADDIN";
PatchControl(Path.Combine(root, "BomTaskPaneControl.cs"));
PatchDesigner(Path.Combine(root, "BomTaskPaneControl.Designer.cs"));
Console.WriteLine("patched slot hole history");

static void PatchControl(string path)
{
    var text = File.ReadAllText(path, Encoding.UTF8);

    text = text.Replace(
        "        private bool repairHolePanelMode;\r\n        private readonly Dictionary<string, string> loadedModelPropValues = new Dictionary<string, string>();",
        "        private bool repairHolePanelMode;\r\n        private readonly Dictionary<string, string> loadedModelPropValues = new Dictionary<string, string>();\r\n        private const string MakeHoleSizeHistoryFileName = \"make-hole-sizes.txt\";");

    text = text.Replace(
        "            txtMakeHolePitch.TextChanged += MakeHoleTrackedInputChanged;",
        "            txtMakeHolePitch.TextChanged += MakeHoleTrackedInputChanged;\r\n            cboRepairHoleDiameter.Leave += MakeHoleSizeHistory_Leave;");

    text = text.Replace(
        "            SelectComboItem(cboRepairHoleDiameter, \"4.2\");\r\n            SetMakeHolePanelMode(false);",
        "            InitializeMakeHoleSizeOptions();\r\n            SelectComboItem(cboRepairHoleDiameter, \"4.2\");\r\n            SetMakeHolePanelMode(false);");

    text = text.Replace(
        "            bool makeMode = !repairMode;\r\n            lblMakeHoleDirection.Visible = makeMode;",
        "            bool makeMode = !repairMode;\r\n            lblRepairHoleDiameter.Text = repairMode ? \"Hole Dia\" : \"Hole Size\";\r\n            lblRepairHoleDiameter.Visible = true;\r\n            cboRepairHoleDiameter.Visible = true;\r\n            lblMakeHoleDirection.Visible = makeMode;");

    text = text.Replace(
        "            lblRepairHoleDiameter.Visible = repairMode;\r\n            cboRepairHoleDiameter.Visible = repairMode;\r\n            btnMakeHoleAccept.Text = repairMode ? \"Repair\" : \"Accept\";",
        "            btnMakeHoleAccept.Text = repairMode ? \"Repair\" : \"Accept\";");

    text = text.Replace(
        "            if (repairHolePanelMode)\r\n            {\r\n                if (compact)\r\n                {\r\n                    btnMakeHoleAccept.Location = new Point(92, 196);\r\n                    btnMakeHoleAccept.Width = Math.Min(120, inputWidth);\r\n                    grpMakeHoleOptions.Height = 244;\r\n                }\r\n                else\r\n                {\r\n                    int innerLeft = 16;\r\n                    int innerWidth = groupWidth - 32;\r\n                    btnMakeHoleAccept.Location = new Point(innerLeft, 180);\r\n                    btnMakeHoleAccept.Width = Math.Min(140, innerWidth);\r\n                }\r\n            }",
        "            if (repairHolePanelMode)\r\n            {\r\n                if (compact)\r\n                {\r\n                    lblRepairHoleDiameter.Location = new Point(16, 148);\r\n                    cboRepairHoleDiameter.Location = new Point(92, 145);\r\n                    cboRepairHoleDiameter.Width = inputWidth;\r\n                    btnMakeHoleAccept.Location = new Point(92, 196);\r\n                    btnMakeHoleAccept.Width = Math.Min(120, inputWidth);\r\n                    grpMakeHoleOptions.Height = 244;\r\n                }\r\n                else\r\n                {\r\n                    int innerLeft = 16;\r\n                    int innerWidth = groupWidth - 32;\r\n                    lblRepairHoleDiameter.Location = new Point(innerLeft, 148);\r\n                    cboRepairHoleDiameter.Location = new Point(innerLeft + 76, 145);\r\n                    cboRepairHoleDiameter.Width = Math.Min(140, innerWidth - 76);\r\n                    btnMakeHoleAccept.Location = new Point(innerLeft, 180);\r\n                    btnMakeHoleAccept.Width = Math.Min(140, innerWidth);\r\n                }\r\n            }");

    text = text.Replace(
        "            if (repairHolePanelMode)\r\n                makeHoleCommand?.RunRepairHole(options);\r\n            else\r\n                makeHoleCommand?.Run(options);",
        "            SaveMakeHoleSizeHistory(cboRepairHoleDiameter.Text);\r\n\r\n            if (repairHolePanelMode)\r\n                makeHoleCommand?.RunRepairHole(options);\r\n            else\r\n                makeHoleCommand?.Run(options);");

    text = text.Replace(
        "            if (repairHolePanelMode && !TryParsePositiveMillimeter(cboRepairHoleDiameter.Text, out diameter))\r\n            {\r\n                MessageBox.Show(\"Hole Dia phai la so lon hon 0.\", \"Repair Hole\", MessageBoxButtons.OK, MessageBoxIcon.Information);\r\n                return false;\r\n            }",
        "            string holeSizeText = NormalizeMakeHoleSizeText(cboRepairHoleDiameter.Text);\r\n            bool slotHole = false;\r\n            if (!TryParseMakeHoleSize(holeSizeText, repairHolePanelMode, out diameter, out string looseSize, out slotHole))\r\n            {\r\n                string message = repairHolePanelMode\r\n                    ? \"Hole Dia phai la so lon hon 0.\"\r\n                    : \"Hole Size phai la so lon hon 0 hoac dang AxB, vi du 4.2x25.\";\r\n                MessageBox.Show(message, repairHolePanelMode ? \"Repair Hole\" : \"Make Hole\", MessageBoxButtons.OK, MessageBoxIcon.Information);\r\n                return false;\r\n            }");

    text = text.Replace(
        "                HoleType = \"Circle\",\r\n                LooseType = \"None\",",
        "                HoleType = slotHole ? \"Loose\" : \"Circle\",\r\n                LooseType = slotHole ? looseSize : \"None\",");

    var marker = "        private bool ComboContainsText(ComboBox combo, string text)\r\n";
    if (!text.Contains("private void InitializeMakeHoleSizeOptions("))
    {
        var helpers = """
        private void InitializeMakeHoleSizeOptions()
        {
            if (cboRepairHoleDiameter == null)
                return;

            EnsureComboItem(cboRepairHoleDiameter, "3");
            EnsureComboItem(cboRepairHoleDiameter, "3.2");
            EnsureComboItem(cboRepairHoleDiameter, "4.2");
            EnsureComboItem(cboRepairHoleDiameter, "5");
            EnsureComboItem(cboRepairHoleDiameter, "6");
            EnsureComboItem(cboRepairHoleDiameter, "8");
            EnsureComboItem(cboRepairHoleDiameter, "10");
            EnsureComboItem(cboRepairHoleDiameter, "12");
            EnsureComboItem(cboRepairHoleDiameter, "4.2x25");
            EnsureComboItem(cboRepairHoleDiameter, "10x16");

            foreach (string item in ReadMakeHoleSizeHistory())
                EnsureComboItem(cboRepairHoleDiameter, item);
        }

        private void MakeHoleSizeHistory_Leave(object sender, EventArgs e)
        {
            SaveMakeHoleSizeHistory(cboRepairHoleDiameter.Text);
        }

        private bool TryParseMakeHoleSize(string text, bool repairMode, out double diameter, out string looseSize, out bool slotHole)
        {
            diameter = 0.0;
            looseSize = "None";
            slotHole = false;
            text = NormalizeMakeHoleSizeText(text);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (text.IndexOf("x", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (repairMode)
                    return false;

                string[] parts = text.ToLowerInvariant().Split(new[] { 'x' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    return false;

                if (!TryParsePositiveMillimeter(parts[0], out double a) || !TryParsePositiveMillimeter(parts[1], out double b))
                    return false;

                double width = Math.Min(a, b);
                double length = Math.Max(a, b);
                if (length <= width)
                    return false;

                diameter = width;
                looseSize = FormatMillimeterText(width) + "x" + FormatMillimeterText(length);
                slotHole = true;
                return true;
            }

            return TryParsePositiveMillimeter(text, out diameter);
        }

        private string NormalizeMakeHoleSizeText(string text)
        {
            text = (text ?? "").Trim();
            if (text.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 2);

            return text
                .Replace(" ", "")
                .Replace("*", "x")
                .Replace("X", "x")
                .Replace(",", ".");
        }

        private string FormatMillimeterText(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string GetMakeHoleSizeHistoryPath()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TAI_TOOL");
            return Path.Combine(folder, MakeHoleSizeHistoryFileName);
        }

        private List<string> ReadMakeHoleSizeHistory()
        {
            string path = GetMakeHoleSizeHistoryPath();
            List<string> result = new List<string>();
            try
            {
                if (!File.Exists(path))
                    return result;

                foreach (string line in File.ReadAllLines(path))
                {
                    string item = NormalizeMakeHoleSizeText(line);
                    if (!string.IsNullOrWhiteSpace(item) && !ContainsText(result, item))
                        result.Add(item);
                }
            }
            catch
            {
            }

            return result;
        }

        private void SaveMakeHoleSizeHistory(string text)
        {
            string item = NormalizeMakeHoleSizeText(text);
            if (string.IsNullOrWhiteSpace(item))
                return;

            bool slotHole;
            double diameter;
            string looseSize;
            if (!TryParseMakeHoleSize(item, false, out diameter, out looseSize, out slotHole))
                return;

            item = slotHole ? looseSize : FormatMillimeterText(diameter);
            List<string> items = ReadMakeHoleSizeHistory();
            items.RemoveAll(value => string.Equals(value, item, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, item);
            while (items.Count > 20)
                items.RemoveAt(items.Count - 1);

            try
            {
                string path = GetMakeHoleSizeHistoryPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, items.ToArray());
            }
            catch
            {
            }

            EnsureComboItem(cboRepairHoleDiameter, item);
            cboRepairHoleDiameter.Text = item;
        }

        private bool ContainsText(List<string> items, string text)
        {
            if (items == null)
                return false;

            foreach (string item in items)
            {
                if (string.Equals(item, text, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

""";
        text = text.Replace(marker, helpers + marker);
    }

    File.WriteAllText(path, text, Encoding.UTF8);
    PatchControlLines(path);
}

static void PatchControlLines(string path)
{
    var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();

    InsertAfterIfMissing(lines,
        "        private readonly Dictionary<string, string> loadedModelPropValues = new Dictionary<string, string>();",
        "        private const string MakeHoleSizeHistoryFileName = \"make-hole-sizes.txt\";",
        "MakeHoleSizeHistoryFileName");

    ReplaceLine(lines,
        "            SelectComboItem(cboRepairHoleDiameter, \"4.2\");",
        "            InitializeMakeHoleSizeOptions();\r\n            SelectComboItem(cboRepairHoleDiameter, \"4.2\");");

    if (!lines.Any(line => line.Contains("private void InitializeMakeHoleSizeOptions(")))
    {
        var marker = lines.FindIndex(line => line.Contains("private bool ComboContainsText(ComboBox combo, string text)"));
        if (marker < 0)
            throw new InvalidOperationException("ComboContainsText marker not found.");

        lines.InsertRange(marker, GetHistoryHelpers());
    }

    File.WriteAllLines(path, lines, Encoding.UTF8);
}

static void InsertAfterIfMissing(List<string> lines, string afterLine, string newLine, string missingToken)
{
    if (lines.Any(line => line.Contains(missingToken)))
        return;

    var index = lines.FindIndex(line => line == afterLine);
    if (index >= 0)
        lines.Insert(index + 1, newLine);
}

static void ReplaceLine(List<string> lines, string oldLine, string newLines)
{
    var index = lines.FindIndex(line => line == oldLine);
    if (index < 0)
        return;

    var replacement = newLines.Split(new[] { "\r\n" }, StringSplitOptions.None);
    lines.RemoveAt(index);
    lines.InsertRange(index, replacement);
}

static string[] GetHistoryHelpers()
{
    return new[]
    {
        "        private void InitializeMakeHoleSizeOptions()",
        "        {",
        "            if (cboRepairHoleDiameter == null)",
        "                return;",
        "",
        "            EnsureComboItem(cboRepairHoleDiameter, \"3\");",
        "            EnsureComboItem(cboRepairHoleDiameter, \"3.2\");",
        "            EnsureComboItem(cboRepairHoleDiameter, \"4.2\");",
        "            EnsureComboItem(cboRepairHoleDiameter, \"5\");",
        "            EnsureComboItem(cboRepairHoleDiameter, \"6\");",
        "            EnsureComboItem(cboRepairHoleDiameter, \"8\");",
        "            EnsureComboItem(cboRepairHoleDiameter, \"10\");",
        "            EnsureComboItem(cboRepairHoleDiameter, \"12\");",
        "            EnsureComboItem(cboRepairHoleDiameter, \"4.2x25\");",
        "            EnsureComboItem(cboRepairHoleDiameter, \"10x16\");",
        "",
        "            foreach (string item in ReadMakeHoleSizeHistory())",
        "                EnsureComboItem(cboRepairHoleDiameter, item);",
        "        }",
        "",
        "        private void MakeHoleSizeHistory_Leave(object sender, EventArgs e)",
        "        {",
        "            SaveMakeHoleSizeHistory(cboRepairHoleDiameter.Text);",
        "        }",
        "",
        "        private bool TryParseMakeHoleSize(string text, bool repairMode, out double diameter, out string looseSize, out bool slotHole)",
        "        {",
        "            diameter = 0.0;",
        "            looseSize = \"None\";",
        "            slotHole = false;",
        "            text = NormalizeMakeHoleSizeText(text);",
        "            if (string.IsNullOrWhiteSpace(text))",
        "                return false;",
        "",
        "            if (text.IndexOf(\"x\", StringComparison.OrdinalIgnoreCase) >= 0)",
        "            {",
        "                if (repairMode)",
        "                    return false;",
        "",
        "                string[] parts = text.ToLowerInvariant().Split(new[] { 'x' }, StringSplitOptions.RemoveEmptyEntries);",
        "                if (parts.Length != 2)",
        "                    return false;",
        "",
        "                double a;",
        "                double b;",
        "                if (!TryParsePositiveMillimeter(parts[0], out a) || !TryParsePositiveMillimeter(parts[1], out b))",
        "                    return false;",
        "",
        "                double width = Math.Min(a, b);",
        "                double length = Math.Max(a, b);",
        "                if (length <= width)",
        "                    return false;",
        "",
        "                diameter = width;",
        "                looseSize = FormatMillimeterText(width) + \"x\" + FormatMillimeterText(length);",
        "                slotHole = true;",
        "                return true;",
        "            }",
        "",
        "            return TryParsePositiveMillimeter(text, out diameter);",
        "        }",
        "",
        "        private string NormalizeMakeHoleSizeText(string text)",
        "        {",
        "            text = (text ?? \"\").Trim();",
        "            if (text.EndsWith(\"mm\", StringComparison.OrdinalIgnoreCase))",
        "                text = text.Substring(0, text.Length - 2);",
        "",
        "            return text",
        "                .Replace(\" \", \"\")",
        "                .Replace(\"*\", \"x\")",
        "                .Replace(\"X\", \"x\")",
        "                .Replace(\",\", \".\");",
        "        }",
        "",
        "        private string FormatMillimeterText(double value)",
        "        {",
        "            return value.ToString(\"0.###\", CultureInfo.InvariantCulture);",
        "        }",
        "",
        "        private string GetMakeHoleSizeHistoryPath()",
        "        {",
        "            string folder = Path.Combine(",
        "                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),",
        "                \"TAI_TOOL\");",
        "            return Path.Combine(folder, MakeHoleSizeHistoryFileName);",
        "        }",
        "",
        "        private List<string> ReadMakeHoleSizeHistory()",
        "        {",
        "            string path = GetMakeHoleSizeHistoryPath();",
        "            List<string> result = new List<string>();",
        "            try",
        "            {",
        "                if (!File.Exists(path))",
        "                    return result;",
        "",
        "                foreach (string line in File.ReadAllLines(path))",
        "                {",
        "                    string item = NormalizeMakeHoleSizeText(line);",
        "                    if (!string.IsNullOrWhiteSpace(item) && !ContainsText(result, item))",
        "                        result.Add(item);",
        "                }",
        "            }",
        "            catch",
        "            {",
        "            }",
        "",
        "            return result;",
        "        }",
        "",
        "        private void SaveMakeHoleSizeHistory(string text)",
        "        {",
        "            string item = NormalizeMakeHoleSizeText(text);",
        "            if (string.IsNullOrWhiteSpace(item))",
        "                return;",
        "",
        "            bool slotHole;",
        "            double diameter;",
        "            string looseSize;",
        "            if (!TryParseMakeHoleSize(item, false, out diameter, out looseSize, out slotHole))",
        "                return;",
        "",
        "            item = slotHole ? looseSize : FormatMillimeterText(diameter);",
        "            List<string> items = ReadMakeHoleSizeHistory();",
        "            items.RemoveAll(value => string.Equals(value, item, StringComparison.OrdinalIgnoreCase));",
        "            items.Insert(0, item);",
        "            while (items.Count > 20)",
        "                items.RemoveAt(items.Count - 1);",
        "",
        "            try",
        "            {",
        "                string path = GetMakeHoleSizeHistoryPath();",
        "                Directory.CreateDirectory(Path.GetDirectoryName(path));",
        "                File.WriteAllLines(path, items.ToArray());",
        "            }",
        "            catch",
        "            {",
        "            }",
        "",
        "            EnsureComboItem(cboRepairHoleDiameter, item);",
        "            cboRepairHoleDiameter.Text = item;",
        "        }",
        "",
        "        private bool ContainsText(List<string> items, string text)",
        "        {",
        "            if (items == null)",
        "                return false;",
        "",
        "            foreach (string item in items)",
        "            {",
        "                if (string.Equals(item, text, StringComparison.OrdinalIgnoreCase))",
        "                    return true;",
        "            }",
        "",
        "            return false;",
        "        }",
        ""
    };
}

static void PatchDesigner(string path)
{
    var text = File.ReadAllText(path, Encoding.UTF8);
    text = text.Replace("            this.lblRepairHoleDiameter.Text = \"Hole Dia\";", "            this.lblRepairHoleDiameter.Text = \"Hole Size\";");
    text = text.Replace("            this.lblRepairHoleDiameter.Visible = false;\r\n", "");
    text = text.Replace(
        "            \"8\",\r\n            \"10\",\r\n            \"12\"});",
        "            \"8\",\r\n            \"10\",\r\n            \"12\",\r\n            \"4.2x25\",\r\n            \"10x16\"});");
    text = text.Replace("            this.cboRepairHoleDiameter.Visible = false;\r\n", "");
    File.WriteAllText(path, text, Encoding.UTF8);
}
