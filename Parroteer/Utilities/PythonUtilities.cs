using System;
using System.IO;

namespace Parroteer.Utilities
{
    public static class PythonUtilities
    {
        private static bool m_HasDetectedPython = false;
        private static string m_PythonPath = null;

        public static bool GetPythonPath(out string path) {
            if (!m_HasDetectedPython) {
                m_PythonPath = GetPythonEnvVar();
                m_HasDetectedPython = true;
            }

            path = m_PythonPath;
            return !string.IsNullOrEmpty(path);
        }

        private static string GetPythonEnvVar() {
            string pathVars = Environment.GetEnvironmentVariable("Path");
            if (string.IsNullOrEmpty(pathVars)) {
                return null;
            }

            string[] pathVarsArray = pathVars.Split(';');
            foreach (string path in pathVarsArray) {
                string trimmedPath = path.Trim(Path.DirectorySeparatorChar);
                string[] pathArray = trimmedPath.Split(Path.DirectorySeparatorChar);
                if (pathArray[pathArray.Length - 1].Contains("Python", StringComparison.OrdinalIgnoreCase)) {
                    return trimmedPath;
                }
            }

            return null;
        }
    }
}
