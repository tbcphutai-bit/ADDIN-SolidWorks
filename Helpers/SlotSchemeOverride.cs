using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Helpers
{
    public class SlotSchemeOverride : IDisposable
    {
        private readonly string baseRegistryPath;
        private readonly string[] slotTypes = { "holeslot", "cboreslot", "csinkslot" };
        private readonly Dictionary<string, object> oldValues = new Dictionary<string, object>();
        private bool isDisposed = false;

        public static SlotSchemeOverride TrySetOverallScheme(ISldWorks swApp, out string diagnostic)
        {
            diagnostic = string.Empty;
            try
            {
                string revStr = swApp.RevisionNumber();
                if (string.IsNullOrEmpty(revStr) || !revStr.Contains("."))
                {
                    diagnostic = $"[Registry] Failed to parse revision number: '{revStr}'";
                    return null;
                }

                int major = int.Parse(revStr.Split('.')[0]);
                int swYear = 1992 + major;
                string basePath = $@"Software\SolidWorks\SOLIDWORKS {swYear}\Hole Wizard";

                return new SlotSchemeOverride(basePath, major, revStr, out diagnostic);
            }
            catch (Exception ex)
            {
                diagnostic = $"[Registry] Unexpected error resolving SW version: {ex.GetType().Name} - {ex.Message}";
                return null;
            }
        }

        private SlotSchemeOverride(string basePath, int major, string revStr, out string diagnostic)
        {
            baseRegistryPath = basePath;
            diagnostic = $"[Registry] SW{major} ({revStr}) SlotDimScheme overrided for: ";

            foreach (var slotType in slotTypes)
            {
                string fullPath = $@"{baseRegistryPath}\{slotType}";
                // Dùng CreateSubKey để bắt buộc Windows phải tạo key nếu chưa tồn tại
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(fullPath, true))
                {
                    if (key != null)
                    {
                        oldValues[slotType] = key.GetValue("SlotDimScheme");
                        // Ghi đè giá trị "1" (Overall / Arc Tangent to Arc Tangent)
                        key.SetValue("SlotDimScheme", "1", RegistryValueKind.String);
                        diagnostic += $"{slotType} ";
                    }
                }
            }
        }

        public void Dispose()
        {
            if (isDisposed) return;

            try
            {
                foreach (var slotType in slotTypes)
                {
                    string fullPath = $@"{baseRegistryPath}\{slotType}";
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(fullPath, true))
                    {
                        if (key != null && oldValues.ContainsKey(slotType))
                        {
                            object oldVal = oldValues[slotType];
                            if (oldVal != null)
                            {
                                key.SetValue("SlotDimScheme", oldVal, RegistryValueKind.String);
                            }
                            else
                            {
                                key.DeleteValue("SlotDimScheme", false);
                            }
                        }
                    }
                }
                Debug.WriteLine("[Registry] Rollback successful. SlotDimScheme restored.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Registry] CRITICAL: Failed to rollback SlotDimScheme. {ex.Message}");
            }
            finally
            {
                isDisposed = true;
            }
        }
    }
}
