using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;

namespace ADDIN.HoleManagement
{
    internal sealed class HoleNamePresetManager
    {
        private readonly string path;

        public HoleNamePresetManager()
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ADDIN-SolidWorks", "hole-name-presets.json");
        }

        public string FilePath { get { return path; } }

        public List<string> Load()
        {
            if (!File.Exists(path))
                return new List<string>();
            using (FileStream stream = File.OpenRead(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(List<string>));
                return Normalize(serializer.ReadObject(stream) as List<string>);
            }
        }

        public void Save(IEnumerable<string> values)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporaryPath = path + ".tmp";
            using (FileStream stream = File.Create(temporaryPath))
            {
                var serializer = new DataContractJsonSerializer(typeof(List<string>));
                serializer.WriteObject(stream, Normalize(values));
            }
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }

        public List<string> Add(IEnumerable<string> values, string value)
        {
            var list = Normalize(values);
            value = (value ?? "").Trim();
            if (value.Length == 0)
                throw new ArgumentException("Hole Type khong duoc de trong.");
            if (!list.Exists(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
                list.Add(value);
            Save(list);
            return list;
        }

        public List<string> Update(IEnumerable<string> values, string oldValue, string newValue)
        {
            var list = Normalize(values);
            int index = list.FindIndex(item => string.Equals(item, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new ArgumentException("Hole Type khong con trong danh sach.");
            newValue = (newValue ?? "").Trim();
            if (newValue.Length == 0 || list.Exists(item =>
                !string.Equals(item, oldValue, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item, newValue, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Hole Type trong hoac trung ten.");
            list[index] = newValue;
            Save(list);
            return list;
        }

        public List<string> Delete(IEnumerable<string> values, string value)
        {
            var list = Normalize(values);
            list.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
            Save(list);
            return list;
        }

        private static List<string> Normalize(IEnumerable<string> values)
        {
            var list = new List<string>();
            if (values == null)
                return list;
            foreach (string value in values)
            {
                string trimmed = (value ?? "").Trim();
                if (trimmed.Length > 0 && !list.Exists(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase)))
                    list.Add(trimmed);
            }
            return list;
        }
    }
}
